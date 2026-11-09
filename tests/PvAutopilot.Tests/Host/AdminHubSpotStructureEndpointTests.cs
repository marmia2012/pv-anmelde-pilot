using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.Processes;
using PvAutopilot.Host.Endpoints;
using PvAutopilot.Infrastructure.Jobs;
using PvAutopilot.Infrastructure.Persistence;
using PvAutopilot.Sources.HubSpot;
using PvAutopilot.Tests.Core;
using PvAutopilot.Tests.Sources.HubSpot;

namespace PvAutopilot.Tests.Host;

/// <summary>
/// <c>GET /admin/hubspot/structure</c> über einen echten lokalen Kestrel (127.0.0.1, freier Port); HubSpot ist simuliert.
/// </summary>
public sealed class AdminHubSpotStructureEndpointTests : IAsyncLifetime
{
    private const string AdminKey = "test-admin-key";

    private readonly HubSpotRig _rig = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        HubSpotStructureReaderTests.MapStructure(_rig.Handler);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:ApiKey"] = AdminKey });
        var h = new ProcessHarness();
        builder.Services.AddSingleton(new HubSpotStructureReader(_rig.Api, HubSpotRig.Wrap(_rig.Options), NullLogger<HubSpotStructureReader>.Instance));
        // Die übrigen Admin-Routen brauchen ihre Dienste nur zur Laufzeit; hier genügen Attrappen, damit das Routing aufgebaut werden kann.
        builder.Services.AddSingleton<IProcessStore>(h.Store);
        builder.Services.AddSingleton(h.Handler());
        builder.Services.AddSingleton<IProcessScheduler>(h.Scheduler);
        builder.Services.AddSingleton<PvAutopilot.Core.Submission.ISubmissionLedger>(h.Ledger);
        builder.Services.AddSingleton(new PvAutopilot.Core.Notifications.AuditLog([]));
        builder.Services.AddSingleton<IProcessRunLock>(new FakeRunLock());

        _app = builder.Build();
        _app.MapAdminEndpoints();
        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private Task<HttpResponseMessage> Get(string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/hubspot/structure");
        if (key is not null)
            request.Headers.Add("X-Admin-Key", key);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task Without_admin_key_the_endpoint_is_unauthorized_and_hubspot_is_not_called()
    {
        using var response = await Get(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_rig.Handler.Requests);
    }

    [Fact]
    public async Task With_wrong_admin_key_the_endpoint_is_unauthorized()
    {
        using var response = await Get("falsch");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_rig.Handler.Requests);
    }

    [Fact]
    public async Task With_admin_key_the_endpoint_returns_stages_and_properties()
    {
        using var response = await Get(AdminKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("0-3", json.GetProperty("objectTypeId").GetString());
        var stages = json.GetProperty("pipelines")[0].GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("id").GetString());
        Assert.Contains("s-vollmacht", stages);
        var property = json.GetProperty("properties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "einspeiseart");
        Assert.Equal(2, property.GetProperty("options").GetArrayLength());
        Assert.Empty(_rig.Handler.Writes);
    }

    [Fact]
    public async Task HubSpot_failure_gives_bad_gateway_without_the_response_body()
    {
        _rig.Handler.Get("crm/v3/pipelines/0-3", HttpStatusCode.InternalServerError, "Erika Mustermann");

        using var response = await Get(AdminKey);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("Mustermann", await response.Content.ReadAsStringAsync());
    }
}
