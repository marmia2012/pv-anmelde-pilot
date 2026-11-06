using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Processes;

namespace PvAutopilot.Sources.HubSpot;

/// <summary>Prüft <see cref="HubSpotOptions.TriggerRules"/> beim Start.</summary>
public sealed class HubSpotOptionsValidator : IValidateOptions<HubSpotOptions>
{
    public ValidateOptionsResult Validate(string? name, HubSpotOptions options)
    {
        var errors = Errors(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static IReadOnlyList<string> Errors(HubSpotOptions options)
    {
        var errors = new List<string>();
        if (!options.TriggerRules.Any(r => r.Kind == ProcessTriggerKind.Start && !string.IsNullOrWhiteSpace(r.PropertyName)))
            errors.Add("HubSpot:TriggerRules braucht mindestens eine Regel mit Kind=Start und PropertyName.");

        for (var i = 0; i < options.TriggerRules.Count; i++)
        {
            var rule = options.TriggerRules[i];
            if (rule.Kind == ProcessTriggerKind.ReviewApproved && rule.Submission is null)
                errors.Add($"HubSpot:TriggerRules:{i}: ReviewApproved braucht Submission (Stufe).");
            if (rule.Kind == ProcessTriggerKind.RueckfrageResolved && rule.Resolution is null)
                errors.Add($"HubSpot:TriggerRules:{i}: RueckfrageResolved braucht Resolution.");
        }
        return errors;
    }

    /// <summary>Regeln, die gültig, aber noch nicht vollständig konfiguriert sind und deshalb nichts auslösen.</summary>
    public static IReadOnlyList<string> Warnings(HubSpotOptions options)
    {
        var warnings = new List<string>();
        for (var i = 0; i < options.TriggerRules.Count; i++)
        {
            var rule = options.TriggerRules[i];
            if (string.IsNullOrWhiteSpace(rule.PropertyName))
                warnings.Add($"HubSpot:TriggerRules:{i} ({rule.Kind}) hat keinen PropertyName und ist inaktiv.");
            else if (!rule.IsActive)
                warnings.Add($"HubSpot:TriggerRules:{i} ({rule.Kind}) hat keine Values und ist inaktiv (sonst löste jeder Wert aus).");
        }
        return warnings;
    }
}

/// <summary>Meldet beim Start unvollständige Trigger-Regeln, damit ein fehlendes Ereignis nicht unbemerkt bleibt.</summary>
public sealed class HubSpotTriggerRulesStartupCheck(IOptions<HubSpotOptions> options, ILogger<HubSpotTriggerRulesStartupCheck> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        foreach (var warning in HubSpotOptionsValidator.Warnings(options.Value))
            logger.LogWarning("{TriggerRuleWarning}", warning);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>HubSpot liefert Datums-Properties als Epoch-Millisekunden (Mitternacht UTC) oder als <c>yyyy-MM-dd</c>.</summary>
public static class HubSpotDate
{
    public static bool TryParse(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        value = value.Trim();

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
        {
            try
            {
                // Mitternacht UTC: der UTC-Kalendertag ist das gemeinte Datum.
                date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
        return false;
    }
}
