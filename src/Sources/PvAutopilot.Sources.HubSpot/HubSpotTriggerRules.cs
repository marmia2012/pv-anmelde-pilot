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
        if (string.IsNullOrWhiteSpace(options.ObjectTypeId))
            errors.Add("HubSpot:ObjectTypeId darf nicht leer sein (Deal: 0-3).");
        if (string.IsNullOrWhiteSpace(options.TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone.Trim(), out _))
            errors.Add("HubSpot:TimeZone muss eine gültige IANA-Zeitzone sein (Default Europe/Berlin).");
        if (!Enum.IsDefined(options.AuditLevel))
            errors.Add("HubSpot:AuditLevel muss Verbose, Info oder Error sein.");
        if (string.IsNullOrWhiteSpace(options.VollmachtFileNamePattern))
            errors.Add("HubSpot:VollmachtFileNamePattern darf nicht leer sein, sonst gälte jedes PDF als EVU-Vollmacht.");
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
        if (!string.IsNullOrWhiteSpace(options.ProjectObjectTypeId))
            warnings.Add("HubSpot:ProjectObjectTypeId ist veraltet und wird ignoriert; wirksam ist HubSpot:ObjectTypeId "
                + $"({options.ObjectTypeId}). Schlüssel umbenennen bzw. entfernen.");
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

/// <summary>
/// Loggt beim Start die wirksame HubSpot-Konfiguration (ohne Secrets) und meldet unvollständige Trigger-Regeln,
/// damit ein fehlendes Ereignis nicht unbemerkt bleibt.
/// </summary>
public sealed class HubSpotTriggerRulesStartupCheck(IOptions<HubSpotOptions> options, ILogger<HubSpotTriggerRulesStartupCheck> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        var o = options.Value;
        logger.LogInformation("HubSpot: Objekttyp {ObjectTypeId}, WriteEnabled {WriteEnabled}, AuditLevel {AuditLevel}",
            o.ObjectTypeId, o.WriteEnabled, o.AuditLevel);
        logger.LogInformation("HubSpot: Zeitzone für Datum-Uhrzeit-Werte {TimeZone}", o.TimeZone);
        foreach (var warning in HubSpotOptionsValidator.Warnings(options.Value))
            logger.LogWarning("{TriggerRuleWarning}", warning);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// HubSpot liefert Datums-Properties als Epoch-Millisekunden (Mitternacht UTC) oder als <c>yyyy-MM-dd</c>,
/// Datum-Uhrzeit-Properties als Epoch-Millisekunden mit Uhrzeit bzw. über die CRM-API als ISO 8601.
/// Reine Datumswerte gelten als Kalendertag, wie sie sind. Werte mit Uhrzeit werden in die Instanz-Zeitzone
/// (<see cref="HubSpotOptions.TimeZone"/>) umgerechnet und dann der Kalendertag genommen – sonst wäre ein Termin
/// um 00:30 deutscher Zeit in UTC noch der Vortag.
/// </summary>
public static class HubSpotDate
{
    private const long MillisecondsPerDay = 24L * 60 * 60 * 1000;

    public static bool TryParse(string? value, TimeZoneInfo timeZone, out DateOnly date)
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
                var instant = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                // Mitternacht UTC = reines Datum (Datums-Property): der UTC-Kalendertag ist gemeint.
                date = ms % MillisecondsPerDay == 0 ? DateOnly.FromDateTime(instant.UtcDateTime) : LocalDay(instant, timeZone);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        // Datum-Uhrzeit als ISO 8601 (z. B. 2026-11-20T08:00:00.000Z); ohne Offset gilt UTC.
        if (value.Contains('T') && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
        {
            date = LocalDay(timestamp, timeZone);
            return true;
        }
        return false;
    }

    private static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);
}
