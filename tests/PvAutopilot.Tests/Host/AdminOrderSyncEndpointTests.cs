using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Orders;
using PvAutopilot.Core.Processes;
using PvAutopilot.Host.Endpoints;
using PvAutopilot.Infrastructure.Jobs;
using PvAutopilot.Tests.Core;
using PvAutopilot.Tests.Sources.HubSpot;

namespace PvAutopilot.Tests.Host;

/// <summary><c>POST /admin/reonic/projects/{projectId}/order-sync</c> über einen echten lokalen Kestrel; Quelle und Ziel sind Fakes.</summary>
public sealed class AdminOrderSyncEndpointTests : IAsyncLifetime
{
    private const string AdminKey = "test-admin-key";
    private const string Project = "reonic-proj-1";

    private readonly FakeOrderSource _source = new();
    private readonly FakeOrderSink _sink = new();
    private RecordingTasks _tasks => _sink.Tasks;
    private readonly OrderAlertRecorder _alert = new();
    private readonly RecordingProjectLock _lock = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:ApiKey"] = AdminKey });
        var h = new ProcessHarness();
        builder.Services.AddSingleton(new OrderTransferService(_source, _sink, _alert, new InMemoryCrmWriteLedger(),
            NullLogger<OrderTransferService>.Instance));
        builder.Services.AddSingleton<IProcessRunLock>(_lock);
        var rig = new HubSpotRig();
        builder.Services.AddSingleton(new PvAutopilot.Sources.HubSpot.HubSpotStructureReader(rig.Api, HubSpotRig.Wrap(rig.Options),
            NullLogger<PvAutopilot.Sources.HubSpot.HubSpotStructureReader>.Instance));
        // Die übrigen Admin-Routen brauchen ihre Dienste nur zur Laufzeit; Attrappen, damit das Routing aufgebaut werden kann.
        builder.Services.AddSingleton<IProcessStore>(h.Store);
        builder.Services.AddSingleton(h.Handler());
        builder.Services.AddSingleton<IProcessScheduler>(h.Scheduler);
        builder.Services.AddSingleton<PvAutopilot.Core.Submission.ISubmissionLedger>(h.Ledger);
        builder.Services.AddSingleton(new PvAutopilot.Core.Notifications.AuditLog([]));

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

    private Task<HttpResponseMessage> Post(string project, string query = "", string? key = AdminKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/admin/reonic/projects/{project}/order-sync{query}");
        if (key is not null)
            request.Headers.Add("X-Admin-Key", key);
        return _client.SendAsync(request);
    }

    private static SignedOrder Order(IReadOnlyList<string>? problems = null) => new()
    {
        PlanningProjectId = Project,
        SignatureRequestId = "sig-1",
        IsSigned = true,
        CrmProjectId = "4711",
        VariantId = "v1",
        Positions = [new OrderPosition("module", "Geheimmarke", "Geheimname", "ART-GEHEIM", "Geheimtext", 3m, 7777.77m, 19m)],
        SignedPdf = new DocumentRef(DocumentKind.Auftrag, "reonic-signature", "https://files.example/geheim-token.pdf", "Mustermann Auftrag.pdf"),
        Problems = problems ?? [],
    };

    [Fact]
    public async Task Without_or_with_wrong_admin_key_the_endpoint_is_unauthorized_and_nothing_runs()
    {
        _source.Order = Order();

        using var none = await Post(Project, key: null);
        using var wrong = await Post(Project, key: "falsch");

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Empty(_source.Reads);
        Assert.Equal(0, _lock.Acquired);
    }

    [Fact]
    public async Task Result_holds_ids_counts_and_property_names_but_no_personal_data()
    {
        _source.Order = Order(["Reonic-Katalog: Komponente „X“ nicht gefunden"]);
        _source.Pdf = [1, 2, 3];
        _sink.Result = new OrderPublishResult
        {
            SkipReason = OrderSkipReasons.WriteDisabled, LineItemCount = 1, PropertyNames = ["auftrag_kwp"], PdfStored = true, ForeignLineItems = 2,
        };

        using var response = await Post(Project);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { "Geheimmarke", "Geheimname", "ART-GEHEIM", "Geheimtext", "7777", "geheim-token", "files.example", "Mustermann" })
            Assert.DoesNotContain(secret, text);

        var json = JsonDocument.Parse(text).RootElement;
        Assert.Equal(
            new[]
            {
                "crmProjectId", "foreignLineItems", "lineItemCount", "pdfStored", "planningProjectId", "problems",
                "propertiesRejected", "propertyNames", "signatureRequestId", "skipReason", "written",
            },
            json.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(Project, json.GetProperty("planningProjectId").GetString());
        Assert.Equal("sig-1", json.GetProperty("signatureRequestId").GetString());
        Assert.Equal("4711", json.GetProperty("crmProjectId").GetString());
        Assert.False(json.GetProperty("written").GetBoolean());
        Assert.Equal(OrderSkipReasons.WriteDisabled, json.GetProperty("skipReason").GetString());
        Assert.Equal(1, json.GetProperty("lineItemCount").GetInt32());
        Assert.Equal(2, json.GetProperty("foreignLineItems").GetInt32());
        Assert.True(json.GetProperty("pdfStored").GetBoolean());
        Assert.Equal("auftrag_kwp", Assert.Single(json.GetProperty("propertyNames").EnumerateArray()).GetString());
        Assert.Contains("nicht gefunden", Assert.Single(json.GetProperty("problems").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Signature_query_parameter_is_passed_trimmed_and_blank_means_none()
    {
        _source.Order = Order();

        using (await Post(Project, "?signatureRequestId=%20sig-9%20")) { }
        using (await Post(Project, "?signatureRequestId=%20")) { }
        using (await Post(Project)) { }

        Assert.Equal([(Project, (string?)"sig-9"), (Project, null), (Project, null)], _source.Reads);
    }

    [Fact]
    public async Task Project_id_is_url_decoded()
    {
        _source.Order = Order();

        using var response = await Post("a%20b");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("a b", Assert.Single(_source.Reads).Project);
    }

    [Fact]
    public async Task Busy_lock_gives_conflict_and_the_transfer_does_not_run()
    {
        _lock.Busy = true;
        _source.Order = Order();

        using var response = await Post(Project);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_source.Reads);
        Assert.Empty(_sink.Published);
        Assert.Equal(OrderTransferJob.LockId(Project), Assert.Single(_lock.Ids));
        Assert.Equal(OrderTransferJob.LockWait, Assert.Single(_lock.Waits));
    }

    [Fact]
    public async Task Successful_run_takes_the_job_lock_and_releases_it()
    {
        _source.Order = Order();

        using var response = await Post(Project);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderTransferJob.LockId(Project), Assert.Single(_lock.Ids));
        Assert.Equal((1, 1), (_lock.Acquired, _lock.Released));
        Assert.Single(_sink.Published);
    }

    [Fact]
    public async Task Technical_failure_gives_bad_gateway_with_path_and_status_only_and_releases_the_lock()
    {
        _source.ThrowOnRead = new HttpRequestException("Reonic residentialProjects/x: HTTP 503");

        using var response = await Post(Project);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("HTTP 503", text);
        Assert.Equal((1, 1), (_lock.Acquired, _lock.Released));
        Assert.Empty(_alert.Sent);
    }

    [Fact]
    public async Task Unknown_project_is_reported_in_the_result_not_as_an_error()
    {
        _source.Order = null;

        using var response = await Post(Project);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(OrderSkipReasons.ProjectNotFound, json.GetProperty("skipReason").GetString());
        Assert.False(json.GetProperty("written").GetBoolean());
    }

    private sealed class RecordingProjectLock : IProcessRunLock
    {
        public bool Busy { get; set; }
        public int Acquired { get; private set; }
        public int Released { get; private set; }
        public List<Guid> Ids { get; } = [];
        public List<TimeSpan> Waits { get; } = [];

        public Task<IAsyncDisposable?> TryAcquireAsync(Guid processId, TimeSpan wait, CancellationToken ct)
        {
            Ids.Add(processId);
            Waits.Add(wait);
            if (Busy)
                return Task.FromResult<IAsyncDisposable?>(null);
            Acquired++;
            return Task.FromResult<IAsyncDisposable?>(new Held(this));
        }

        private sealed class Held(RecordingProjectLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Released++;
                return ValueTask.CompletedTask;
            }
        }
    }

    [Theory]
    [InlineData("%20")]
    [InlineData("%20%20%20")]
    public async Task Blank_project_id_is_a_bad_request_without_lock_or_transfer(string project)
    {
        _source.Order = Order();

        using var response = await Post(project);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_source.Reads);
        Assert.Equal(0, _lock.Acquired);
        Assert.Empty(_lock.Ids);
    }

    [Fact]
    public async Task Project_id_with_surrounding_spaces_is_trimmed_for_transfer_and_lock()
    {
        _source.Order = Order();

        using var response = await Post($"%20{Project}%20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Project, Assert.Single(_source.Reads).Project);
        Assert.Equal(OrderTransferJob.LockId(Project), Assert.Single(_lock.Ids));
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(Project, json.GetProperty("planningProjectId").GetString());
    }

    [Fact]
    public async Task Given_signature_that_is_not_visible_gives_conflict_with_ids_only_and_releases_the_lock()
    {
        _source.Order = Order() with { IsSigned = false, Problems = ["Reonic: Geheimproblem"] };

        using var response = await Post(Project, "?signatureRequestId=sig-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(Project, text);
        Assert.Contains("sig-1", text);
        Assert.DoesNotContain("Geheim", text);
        Assert.Equal((1, 1), (_lock.Acquired, _lock.Released));
        Assert.Empty(_sink.Published);
        Assert.Empty(_alert.Sent);
        Assert.Empty(_sink.Tasks.Calls);
    }

    [Fact]
    public async Task Not_signed_without_signature_id_stays_ok_with_a_business_result()
    {
        _source.Order = Order() with { IsSigned = false, SignatureRequestId = null };

        using var response = await Post(Project);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(OrderSkipReasons.NotSigned, json.GetProperty("skipReason").GetString());
    }
}
