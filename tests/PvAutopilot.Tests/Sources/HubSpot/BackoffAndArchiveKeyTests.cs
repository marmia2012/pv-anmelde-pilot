using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Http;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Processes.Steps;
using PvAutopilot.Core.Submission;
using PvAutopilot.Sources.HubSpot;
using PvAutopilot.Sources.HubSpot.Api;
using PvAutopilot.Sources.Reonic;
using PvAutopilot.Sources.Reonic.Api;
using PvAutopilot.Tests.Core;

namespace PvAutopilot.Tests.Sources.HubSpot;

/// <summary>Anlegende POSTs werden nur bei 429 wiederholt; Lesen, PATCH und Batch-Read weiter auch bei 5xx.</summary>
public class CreatingPostBackoffTests
{
    private readonly HubSpotHandler _handler = new();
    private readonly List<TimeSpan> _waits = [];

    private ServiceProvider Build()
    {
        var settings = new Dictionary<string, string?>
        {
            ["HubSpot:AccessToken"] = "t",
            ["HubSpot:BaseUrl"] = HubSpotRig.BaseUrl,
            ["HubSpot:TriggerRules:0:Kind"] = "Start",
            ["HubSpot:TriggerRules:0:PropertyName"] = "dealstage",
            ["HubSpot:TriggerRules:0:Values:0"] = "s",
            ["Reonic:ApiKey"] = "rnc_test",
            ["Reonic:BaseUrl"] = "https://reonic.test/rest/v3/",
        };
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        services.AddHubSpot(configuration);
        services.AddReonic(configuration);
        // Reonic registriert den Handler selbst (letzte Registrierung gewinnt): hier ohne echte Wartezeit.
        services.AddTransient(_ => new ExponentialBackoffHandler((d, _) =>
        {
            _waits.Add(d);
            return Task.CompletedTask;
        }));
        services.AddHttpClient<HubSpotApiClient>().ConfigurePrimaryHttpMessageHandler(() => _handler);
        services.AddHttpClient<ReonicApiClient>().ConfigurePrimaryHttpMessageHandler(() => _handler);
        return services.BuildServiceProvider();
    }

    private static readonly Dictionary<string, string> Props = new() { ["hs_note_body"] = "x" };

    private int Count(HttpMethod method, string path) => _handler.Requests.Count(r => r.Method == method && r.Path == path);

    private static Task Create(HubSpotApiClient api, string kind) => kind switch
    {
        "tasks" => api.CreateAssociatedObjectAsync("tasks", Props, "0-3", "1", default),
        "notes" => api.CreateAssociatedObjectAsync("notes", Props, "0-3", "1", default),
        _ => api.UploadFileAsync("/pv-autopilot/1", "a.json", "application/json", [1], default),
    };

    private static string CreatePath(string kind) => kind switch
    {
        "tasks" => "crm/v3/objects/tasks",
        "notes" => "crm/v3/objects/notes",
        _ => "files/v3/files",
    };

    [Theory]
    [InlineData("tasks")]
    [InlineData("notes")]
    [InlineData("upload")]
    public async Task Creating_post_is_not_retried_on_502(string kind)
    {
        _handler.Map(HttpMethod.Post, CreatePath(kind), _ => HubSpotHandler.Status(HttpStatusCode.BadGateway));
        using var provider = Build();

        await Assert.ThrowsAsync<HttpRequestException>(() => Create(provider.GetRequiredService<HubSpotApiClient>(), kind));

        Assert.Equal(1, Count(HttpMethod.Post, CreatePath(kind)));
        Assert.Empty(_waits);
    }

    [Theory]
    [InlineData("tasks")]
    [InlineData("notes")]
    [InlineData("upload")]
    public async Task Creating_post_is_retried_on_429(string kind)
    {
        var attempts = 0;
        _handler.Map(HttpMethod.Post, CreatePath(kind), _ => ++attempts == 1
            ? HubSpotHandler.Status(HttpStatusCode.TooManyRequests)
            : HubSpotHandler.Json(new { id = "X1" }));
        using var provider = Build();

        await Create(provider.GetRequiredService<HubSpotApiClient>(), kind);

        Assert.Equal(2, Count(HttpMethod.Post, CreatePath(kind)));
        Assert.Single(_waits);
    }

    [Fact]
    public async Task Get_is_still_retried_on_5xx()
    {
        var attempts = 0;
        _handler.Map(HttpMethod.Get, "crm/v3/objects/0-3/1", _ => ++attempts == 1
            ? HubSpotHandler.Status(HttpStatusCode.ServiceUnavailable)
            : HubSpotHandler.Json(new { id = "1", properties = new { } }));
        using var provider = Build();

        var deal = await provider.GetRequiredService<HubSpotApiClient>().GetObjectAsync("0-3", "1", [], default);

        Assert.Equal("1", deal!.Id);
        Assert.Equal(2, Count(HttpMethod.Get, "crm/v3/objects/0-3/1"));
    }

    [Fact]
    public async Task Patch_is_still_retried_on_5xx()
    {
        var attempts = 0;
        _handler.Map(HttpMethod.Patch, "crm/v3/objects/0-3/1", _ => ++attempts == 1
            ? HubSpotHandler.Status(HttpStatusCode.BadGateway)
            : HubSpotHandler.Json(new { id = "1" }));
        using var provider = Build();

        await provider.GetRequiredService<HubSpotApiClient>().UpdateObjectAsync("0-3", "1", new Dictionary<string, string> { ["a"] = "b" }, default);

        Assert.Equal(2, Count(HttpMethod.Patch, "crm/v3/objects/0-3/1"));
    }

    [Fact]
    public async Task Batch_read_post_is_still_retried_on_5xx()
    {
        var attempts = 0;
        _handler.Map(HttpMethod.Post, "crm/v3/objects/notes/batch/read", _ => ++attempts == 1
            ? HubSpotHandler.Status(HttpStatusCode.InternalServerError)
            : HubSpotHandler.Json(new { results = new[] { new { id = "1", properties = new { } } } }));
        using var provider = Build();

        var result = await provider.GetRequiredService<HubSpotApiClient>().BatchReadAsync("notes", ["1"], ["hs_timestamp"], default);

        Assert.Single(result);
        Assert.Equal(2, Count(HttpMethod.Post, "crm/v3/objects/notes/batch/read"));
    }

    [Fact]
    public async Task Default_association_put_is_still_retried_on_5xx()
    {
        _handler.Map(HttpMethod.Post, "crm/v3/objects/tasks", _ => HubSpotHandler.Json(new { id = "T1" }));
        var attempts = 0;
        _handler.Map(HttpMethod.Put, "crm/v4/objects/tasks/T1/associations/default/2-77/1", _ => ++attempts == 1
            ? HubSpotHandler.Status(HttpStatusCode.BadGateway)
            : HubSpotHandler.Status(HttpStatusCode.OK, "{}"));
        using var provider = Build();

        await provider.GetRequiredService<HubSpotApiClient>().CreateAssociatedObjectAsync("tasks", Props, "2-77", "1", default);

        Assert.Equal(1, Count(HttpMethod.Post, "crm/v3/objects/tasks"));
        Assert.Equal(2, Count(HttpMethod.Put, "crm/v4/objects/tasks/T1/associations/default/2-77/1"));
    }

    [Fact]
    public async Task Reonic_client_still_retries_5xx()
    {
        var attempts = 0;
        _handler.Map(HttpMethod.Get, "rest/v3/residentialProjects/p1", _ => ++attempts == 1
            ? HubSpotHandler.Status(HttpStatusCode.BadGateway)
            : HubSpotHandler.Status(HttpStatusCode.NotFound));
        using var provider = Build();

        var project = await provider.GetRequiredService<ReonicApiClient>().GetProjectAsync("p1", default);

        Assert.Null(project);
        Assert.Equal(2, Count(HttpMethod.Get, "rest/v3/residentialProjects/p1"));
    }

    [Fact]
    public async Task Handler_without_the_option_retries_posts_on_5xx()
    {
        var calls = 0;
        var handler = new ExponentialBackoffHandler((_, _) => Task.CompletedTask)
        {
            InnerHandler = new Stub(() => ++calls == 1 ? HttpStatusCode.BadGateway : HttpStatusCode.OK),
        };
        using var client = new HttpClient(handler);

        using var response = await client.PostAsync("https://x.test/a", new StringContent("{}"));

        Assert.Equal(2, calls);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class Stub(Func<HttpStatusCode> next) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(next()));
    }
}

/// <summary>Datenkarte: stabiler Ablage-Schlüssel je Vorgang und Revision, kein "reference" im JSON.</summary>
public class DataCardArchiveKeyTests
{
    private static readonly Guid ProcessId = Guid.Parse("019a4f3c-1d2e-7abc-8def-0123456789ab");

    private readonly HubSpotRig _rig = new();
    private readonly InMemoryCrmWriteLedger _ledger = new();

    public DataCardArchiveKeyTests()
    {
        _rig.Options.WriteEnabled = true;
        var n = 0;
        _rig.Handler.Map(HttpMethod.Post, "crm/v3/objects/notes", _ => HubSpotHandler.Json(new { id = $"N{++n}" }));
        _rig.Handler.Map(HttpMethod.Post, "files/v3/files", _ => HubSpotHandler.Json(new { id = $"F{++n}" }));
    }

    private HubSpotDocumentArchive Archive() => new(_rig.Api, new HubSpotWriteOnce(_ledger, NullLogger<HubSpotWriteOnce>.Instance),
        HubSpotRig.Wrap(_rig.Options), new FixedTime(TestData.Now), NullLogger<HubSpotDocumentArchive>.Instance);

    private int Posts(string path) => _rig.Handler.Writes.Count(r => r.Path == path);

    private static NabDataCard CardWithReference(string reference) => TestData.Card() with
    {
        Dokumente = [new DocumentRef(DocumentKind.EvuVollmacht, "hubspot", reference, "vollmacht.pdf")],
    };

    [Fact]
    public void Archive_key_names_process_and_revision()
    {
        Assert.Equal($"datacard:{ProcessId:N}:r0", AggregateDataCardStep.DataCardDocument(ProcessId, 0, TestData.Card()).ArchiveKey);
        Assert.Equal($"datacard:{ProcessId:N}:r3", AggregateDataCardStep.DataCardDocument(ProcessId, 3, TestData.Card()).ArchiveKey);
    }

    [Fact]
    public async Task Second_store_with_changed_content_creates_no_second_file_and_no_second_note()
    {
        await Archive().StoreAsync("101", [AggregateDataCardStep.DataCardDocument(ProcessId, 0, TestData.Card(modulAnzahl: 20))], default);
        await Archive().StoreAsync("101", [AggregateDataCardStep.DataCardDocument(ProcessId, 0, TestData.Card(modulAnzahl: 21))], default);

        Assert.Equal(1, Posts("files/v3/files"));
        Assert.Equal(1, Posts("crm/v3/objects/notes"));
    }

    [Fact]
    public async Task A_new_revision_or_a_new_process_is_stored_again()
    {
        var card = TestData.Card();
        await Archive().StoreAsync("101", [AggregateDataCardStep.DataCardDocument(ProcessId, 0, card)], default);
        await Archive().StoreAsync("101", [AggregateDataCardStep.DataCardDocument(ProcessId, 1, card)], default);
        await Archive().StoreAsync("101", [AggregateDataCardStep.DataCardDocument(Guid.NewGuid(), 0, card)], default);

        Assert.Equal(3, Posts("files/v3/files"));
    }

    [Fact]
    public void File_key_uses_the_archive_key_and_no_content_hash()
    {
        var a = AggregateDataCardStep.DataCardDocument(ProcessId, 0, TestData.Card(modulAnzahl: 20));
        var b = AggregateDataCardStep.DataCardDocument(ProcessId, 0, TestData.Card(modulAnzahl: 21));

        Assert.NotEqual(a.Content, b.Content);
        Assert.Equal(HubSpotDocumentArchive.FileKey("101", a), HubSpotDocumentArchive.FileKey("101", b));
        Assert.Equal($"file:101:datacard:{ProcessId:N}:r0", HubSpotDocumentArchive.FileKey("101", a));
    }

    [Fact]
    public async Task Without_archive_key_name_and_content_hash_decide()
    {
        await Archive().StoreAsync("101", [new GeneratedDocument("a.json", "application/json", [1])], default);
        await Archive().StoreAsync("101", [new GeneratedDocument("a.json", "application/json", [1])], default);
        Assert.Equal(1, Posts("files/v3/files"));

        await Archive().StoreAsync("101", [new GeneratedDocument("a.json", "application/json", [2])], default);
        Assert.Equal(2, Posts("files/v3/files"));
    }

    [Fact]
    public void Stored_data_card_json_has_no_reference_but_keeps_the_rest_of_the_document()
    {
        var json = Encoding.UTF8.GetString(AggregateDataCardStep.DataCardDocument(ProcessId, 0, CardWithReference("signed-url-geheim")).Content);

        Assert.DoesNotContain("reference", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed-url-geheim", json);
        Assert.Contains("EvuVollmacht", json);
        Assert.Contains("vollmacht.pdf", json);
        Assert.Contains("hubspot", json);
    }

    [Fact]
    public async Task Step_archives_json_without_reference_under_the_process_key()
    {
        var archive = new CapturingArchive();
        var step = new AggregateDataCardStep(
            new NabDataCardAggregator(new FakeCrmSource(), new FakePlanningSource(), new NoProductDocuments(),
                Microsoft.Extensions.Options.Options.Create(new ErrichterOptions())),
            archive, new FixedTime(TestData.Now));
        var process = new AnmeldeProcess("crm-1", TestData.Now);

        await step.ExecuteAsync(process, default);

        var document = Assert.Single(archive.Documents);
        Assert.Equal($"datacard:{process.Id:N}:r0", document.ArchiveKey);
        Assert.DoesNotContain("reference", Encoding.UTF8.GetString(document.Content), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CapturingArchive : IDocumentArchive
    {
        public List<GeneratedDocument> Documents { get; } = [];

        public Task StoreAsync(string crmProjectId, IReadOnlyList<GeneratedDocument> documents, CancellationToken ct)
        {
            Documents.AddRange(documents);
            return Task.CompletedTask;
        }
    }
}
