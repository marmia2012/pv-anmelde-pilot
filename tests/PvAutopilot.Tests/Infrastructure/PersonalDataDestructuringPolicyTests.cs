using PvAutopilot.Infrastructure.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PvAutopilot.Tests.Infrastructure;

public class PersonalDataDestructuringPolicyTests
{
    [Fact]
    public void Destructured_data_card_contains_no_personal_data()
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .Destructure.With<PersonalDataDestructuringPolicy>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Karte {@Card}", TestData.Card());

        var rendered = sink.Events.Single().Properties["Card"].ToString();
        foreach (var secret in new[] { "Erika", "Mustermann", "erika@example.org", "Musterstraße", "DE00", "1ESY" })
            Assert.DoesNotContain(secret, rendered);
        Assert.Contains("47051", rendered);
        Assert.Contains("Hersteller A", rendered);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
