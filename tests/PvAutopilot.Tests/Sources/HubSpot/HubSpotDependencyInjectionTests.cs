using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PvAutopilot.Core.Http;
using PvAutopilot.Core.Mastr;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Sources;
using PvAutopilot.Sources.HubSpot;
using PvAutopilot.Sources.HubSpot.Api;

namespace PvAutopilot.Tests.Sources.HubSpot;

public class HubSpotDependencyInjectionTests
{
    private static ServiceProvider Build(HubSpotHandler handler, out List<TimeSpan> waits, Dictionary<string, string?>? extra = null)
    {
        var recorded = new List<TimeSpan>();
        waits = recorded;
        var settings = new Dictionary<string, string?>
        {
            ["HubSpot:AccessToken"] = "test-token",
            ["HubSpot:BaseUrl"] = HubSpotRig.BaseUrl,
            ["HubSpot:TriggerRules:0:Kind"] = "Start",
            ["HubSpot:TriggerRules:0:PropertyName"] = "dealstage",
            ["HubSpot:TriggerRules:0:Values:0"] = "stage-start",
        };
        foreach (var (key, value) in extra ?? [])
            settings[key] = value;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTime(TestData.Now));
        services.AddSingleton<ICrmWriteLedger>(new InMemoryCrmWriteLedger());
        // Backoff ohne echte Wartezeit; AddHubSpot übernimmt eine vorhandene Registrierung.
        services.AddTransient(_ => new ExponentialBackoffHandler((delay, _) =>
        {
            recorded.Add(delay);
            return Task.CompletedTask;
        }));
        services.AddHubSpot(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddHttpClient<HubSpotApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Client_sends_the_bearer_token_to_the_configured_base_url()
    {
        var handler = new HubSpotHandler();
        handler.Get("crm/v3/objects/0-3/1", new { id = "1", properties = new { } });
        using var provider = Build(handler, out _);

        await provider.GetRequiredService<HubSpotApiClient>().GetObjectAsync("0-3", "1", [], default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer test-token", request.Authorization);
    }

    [Fact]
    public async Task Rate_limit_and_server_errors_are_retried_with_the_shared_backoff()
    {
        var handler = new HubSpotHandler();
        var attempts = 0;
        handler.Map(HttpMethod.Get, "crm/v3/objects/0-3/1", _ => ++attempts switch
        {
            1 => HubSpotHandler.Status(HttpStatusCode.TooManyRequests),
            2 => HubSpotHandler.Status(HttpStatusCode.BadGateway),
            _ => HubSpotHandler.Json(new { id = "1", properties = new { } }),
        });
        using var provider = Build(handler, out var waits);

        var deal = await provider.GetRequiredService<HubSpotApiClient>().GetObjectAsync("0-3", "1", [], default);

        Assert.Equal("1", deal!.Id);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, waits.Count);
        Assert.Equal(ExponentialBackoffHandler.Delays[0], waits[0]);
        Assert.Equal(ExponentialBackoffHandler.Delays[1], waits[1]);
    }

    [Fact]
    public async Task Persistent_server_error_ends_as_http_request_exception_after_the_retries()
    {
        var handler = new HubSpotHandler();
        handler.Get("crm/v3/objects/0-3/1", HttpStatusCode.ServiceUnavailable, "Erika Mustermann");
        using var provider = Build(handler, out var waits);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetRequiredService<HubSpotApiClient>().GetObjectAsync("0-3", "1", [], default));

        Assert.Equal(ExponentialBackoffHandler.Delays.Length + 1, handler.Requests.Count);
        Assert.Equal(ExponentialBackoffHandler.Delays.Length, waits.Count);
        Assert.DoesNotContain("Mustermann", error.Message);
    }

    [Fact]
    public void Adapter_services_are_registered_for_reading_and_writing()
    {
        using var provider = Build(new HubSpotHandler(), out _);
        using var scope = provider.CreateScope();
        var s = scope.ServiceProvider;

        Assert.IsType<HubSpotCrmProjectSource>(s.GetRequiredService<ICrmProjectSource>());
        Assert.IsType<HubSpotTaskSink>(s.GetRequiredService<IHumanTaskSink>());
        Assert.IsType<HubSpotAuditSink>(s.GetRequiredService<IAuditSink>());
        Assert.IsType<HubSpotDocumentArchive>(s.GetRequiredService<IDocumentArchive>());
        Assert.IsType<HubSpotStatusSink>(s.GetRequiredService<ICrmStatusSink>());
        Assert.IsType<HubSpotMastrCredentialStore>(s.GetRequiredService<IMastrCredentialStore>());
    }

    [Fact]
    public async Task Default_configuration_never_writes_even_if_every_sink_is_called()
    {
        var handler = new HubSpotHandler();
        using var provider = Build(handler, out _);
        using var scope = provider.CreateScope();
        var s = scope.ServiceProvider;
        var process = new PvAutopilot.Core.Processes.AnmeldeProcess("1", TestData.Now);

        await s.GetRequiredService<IHumanTaskSink>().CreateTaskAsync("1", "k", "t", "b", default);
        await s.GetRequiredService<IAuditSink>().WriteAsync(process, new AuditEvent("blocked", "m", AuditLevel.Error), default);
        await s.GetRequiredService<IDocumentArchive>().StoreAsync("1", [new PvAutopilot.Core.Submission.GeneratedDocument("a.json", "application/json", [1])], default);
        await s.GetRequiredService<ICrmStatusSink>().PublishAsync(process, default);
        await s.GetRequiredService<IMastrCredentialStore>().DeleteAsync("1", default);

        Assert.Empty(handler.Requests);
    }
}
