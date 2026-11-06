using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Infrastructure;
using PvAutopilot.Infrastructure.Jobs;

namespace PvAutopilot.Tests.Infrastructure;

public class InfrastructureOptionsTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings, string environment = "Staging")
    {
        settings.TryAdd("ConnectionStrings:Autopilot", "Host=localhost;Database=test");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new FakeHostEnvironment { EnvironmentName = environment })
            .AddAutopilotInfrastructure(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void Continue_after_dry_run_is_rejected_in_production()
    {
        using var provider = Build(new() { ["Submission:Mode"] = "DryRun", ["Submission:ContinueAfterDryRun"] = "true" }, "Production");

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SubmissionOptions>>().Value);
        Assert.Contains("Production", error.Message);
    }

    [Theory]
    [InlineData("DryRun")]
    [InlineData("Live")]
    public void Production_without_continue_after_dry_run_is_valid(string mode)
    {
        using var provider = Build(new() { ["Submission:Mode"] = mode, ["Submission:ContinueAfterDryRun"] = "false" }, "Production");

        Assert.False(provider.GetRequiredService<IOptions<SubmissionOptions>>().Value.ContinueAfterDryRun);
    }

    [Fact]
    public void Continue_after_dry_run_in_production_stops_the_host_start()
    {
        using var provider = Build(new() { ["Submission:ContinueAfterDryRun"] = "true" }, "Production");

        // ValidateOnStart: diesen Validator ruft der Host beim Start auf.
        Assert.Throws<OptionsValidationException>(provider.GetRequiredService<IStartupValidator>().Validate);
    }

    [Theory]
    [InlineData("DryRun", "true")]
    [InlineData("Review", "false")]
    [InlineData("Live", "false")]
    public void Valid_submission_options_resolve(string mode, string continueAfterDryRun)
    {
        using var provider = Build(new() { ["Submission:Mode"] = mode, ["Submission:ContinueAfterDryRun"] = continueAfterDryRun });

        Assert.Equal(Enum.Parse<SubmissionMode>(mode), provider.GetRequiredService<IOptions<SubmissionOptions>>().Value.Mode);
    }

    [Theory]
    [InlineData("Review")]
    [InlineData("Live")]
    public void Continue_after_dry_run_is_rejected_outside_dry_run(string mode)
    {
        using var provider = Build(new() { ["Submission:Mode"] = mode, ["Submission:ContinueAfterDryRun"] = "true" });

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SubmissionOptions>>().Value);
        Assert.Contains("ContinueAfterDryRun", error.Message);
    }

    [Fact]
    public void Watchdog_defaults_are_valid_and_can_be_overridden()
    {
        using var provider = Build(new()
        {
            ["Watchdog:Schedule"] = "30 6 * * *",
            ["Watchdog:MaxWait:RueckfrageOpen"] = "3.00:00:00",
        });

        var options = provider.GetRequiredService<IOptions<ProcessWatchdogOptions>>().Value;

        Assert.Equal(TimeSpan.FromDays(3), options.MaxWait[ProcessState.RueckfrageOpen]);
        Assert.Equal(TimeSpan.FromDays(30), options.MaxWait[ProcessState.NetzanmeldungSubmitted]);
        Assert.Equal(TimeSpan.FromHours(1), options.StalledAfter);
    }

    [Theory]
    [InlineData("Watchdog:Schedule", "jeden Morgen", "Schedule")]
    [InlineData("Watchdog:Schedule", "0 25 * * *", "Schedule")]
    [InlineData("Watchdog:StalledAfter", "00:00:00", "StalledAfter")]
    [InlineData("Watchdog:MaxWait:RueckfrageOpen", "-1.00:00:00", "MaxWait")]
    public void Invalid_watchdog_options_fail(string key, string value, string expected)
    {
        using var provider = Build(new() { [key] = value });

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ProcessWatchdogOptions>>().Value);
        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("0 7 * * 1-5", true)]
    [InlineData("0 */5 * * * *", true)]
    [InlineData("@daily", true)]
    [InlineData("", false)]
    [InlineData("0 7 * *", false)]
    public void Cron_validation_matches_hangfire_formats(string cron, bool valid) =>
        Assert.Equal(valid, RecurringJobs.IsValidCron(cron));

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public required string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "PvAutopilot.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
