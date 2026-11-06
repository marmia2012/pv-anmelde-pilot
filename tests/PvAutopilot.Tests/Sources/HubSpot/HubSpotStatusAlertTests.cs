using System.Net;
using PvAutopilot.Core.Processes;
using PvAutopilot.Sources.HubSpot;
using PvAutopilot.Tests.Core;

namespace PvAutopilot.Tests.Sources.HubSpot;

/// <summary>Status-PATCH: 4xx (außer 429) ist dauerhaft - Error-Log und ein Alarm je Vorgang und Zustand; sonst wird weiter geworfen.</summary>
public class HubSpotStatusAlertTests
{
    private const string Path = "crm/v3/objects/0-3/101";

    private readonly HubSpotRig _rig = new();
    private readonly InMemoryCrmWriteLedger _ledger = new();
    private readonly RecordingAlert _alert = new();
    private readonly CapturingLogger<HubSpotStatusSink> _log = new();
    private HttpStatusCode _status = HttpStatusCode.BadRequest;

    public HubSpotStatusAlertTests()
    {
        _rig.Options.WriteEnabled = true;
        _rig.Handler.Map(HttpMethod.Patch, Path, _ => HubSpotHandler.Status(_status, """{"message":"Erika Mustermann Property status ungueltig"}"""));
    }

    private HubSpotStatusSink Sink() => new(_rig.Api, _ledger, _alert, HubSpotRig.Wrap(_rig.Options), _log);

    private static AnmeldeProcess Process() => new("101", TestData.Now);

    [Fact]
    public async Task Bad_request_does_not_throw_logs_an_error_and_sends_one_alert()
    {
        await Sink().PublishAsync(Process(), default);

        Assert.Single(_alert.Bodies);
        Assert.Contains(_log.Messages, m => m.StartsWith("Error"));
    }

    [Fact]
    public async Task Second_bad_request_in_the_same_state_sends_no_second_alert_but_logs_again()
    {
        var process = Process();

        await Sink().PublishAsync(process, default);
        await Sink().PublishAsync(process, default);

        Assert.Single(_alert.Bodies);
        Assert.Equal(2, _log.Messages.Count(m => m.StartsWith("Error")));
        Assert.Equal(2, _rig.Handler.Writes.Count());
    }

    [Fact]
    public async Task Another_state_of_the_same_process_alerts_again()
    {
        var process = Process();
        await Sink().PublishAsync(process, default);

        process.TransitionTo(ProcessState.DataCardAggregated, TestData.Now);
        await Sink().PublishAsync(process, default);
        await Sink().PublishAsync(process, default);

        Assert.Equal(2, _alert.Bodies.Count);
    }

    [Fact]
    public async Task Another_process_in_the_same_state_alerts_separately()
    {
        await Sink().PublishAsync(Process(), default);
        await Sink().PublishAsync(Process(), default);

        Assert.Equal(2, _alert.Bodies.Count);
    }

    [Fact]
    public async Task Alert_key_is_stored_in_the_ledger_per_process_and_state()
    {
        var process = Process();

        await Sink().PublishAsync(process, default);

        Assert.Contains(HubSpotStatusSink.AlertKey(process), _ledger.Records.Keys);
        Assert.Contains(process.State.ToString(), HubSpotStatusSink.AlertKey(process));
        Assert.Contains(process.Id.ToString("N"), HubSpotStatusSink.AlertKey(process));
    }

    [Fact]
    public async Task Alert_and_logs_contain_no_response_body_or_personal_data()
    {
        await Sink().PublishAsync(Process(), default);

        var everything = string.Join('\n', _alert.Bodies) + "\n" + _log.All;
        Assert.DoesNotContain("Mustermann", everything);
        Assert.DoesNotContain("ungueltig", everything);
        Assert.Contains("400", string.Join('\n', _alert.Bodies));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Other_client_errors_are_permanent_too(HttpStatusCode status)
    {
        _status = status;

        await Sink().PublishAsync(Process(), default);

        Assert.Single(_alert.Bodies);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Transient_errors_still_throw_and_send_no_alert(HttpStatusCode status)
    {
        _status = status;

        await Assert.ThrowsAsync<HttpRequestException>(() => Sink().PublishAsync(Process(), default));

        Assert.Empty(_alert.Bodies);
        Assert.Empty(_ledger.Records);
    }

    [Fact]
    public async Task Success_sends_no_alert()
    {
        _rig.Handler.Map(HttpMethod.Patch, Path, _ => HubSpotHandler.Json(new { id = "101" }));

        await Sink().PublishAsync(Process(), default);

        Assert.Empty(_alert.Bodies);
    }

    [Fact]
    public async Task Without_write_enabled_nothing_is_sent_and_nothing_alerts()
    {
        _rig.Options.WriteEnabled = false;

        await Sink().PublishAsync(Process(), default);

        Assert.Empty(_rig.Handler.Requests);
        Assert.Empty(_alert.Bodies);
    }

    [Fact]
    public void Permanent_means_4xx_except_429()
    {
        Assert.True(HubSpotStatusSink.IsPermanent(HttpStatusCode.BadRequest));
        Assert.True(HubSpotStatusSink.IsPermanent(HttpStatusCode.NotFound));
        Assert.False(HubSpotStatusSink.IsPermanent(HttpStatusCode.TooManyRequests));
        Assert.False(HubSpotStatusSink.IsPermanent(HttpStatusCode.BadGateway));
        Assert.False(HubSpotStatusSink.IsPermanent(null));
    }
}
