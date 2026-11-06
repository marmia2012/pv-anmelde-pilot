using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Core.Processes;
using PvAutopilot.Core.Submission;
using PvAutopilot.Sources.HubSpot;
using K = PvAutopilot.Sources.HubSpot.HubSpotPropertyKeys;

namespace PvAutopilot.Tests.Sources.HubSpot;

/// <summary>Schreibende Sinks gegen simuliertes HubSpot: Schalter, Ziel, Inhalt und Idempotenz.</summary>
public class HubSpotWriteTests
{
    private const string Deal = HubSpotRig.DealId;

    private readonly HubSpotRig _rig = new();
    private readonly InMemoryCrmWriteLedger _ledger = new();
    private readonly FixedTime _time = new(TestData.Now);
    private int _counter;

    public HubSpotWriteTests()
    {
        _rig.Options.WriteEnabled = true;
        _rig.Handler.Map(HttpMethod.Post, "crm/v3/objects/tasks", _ => Created("T"));
        _rig.Handler.Map(HttpMethod.Post, "crm/v3/objects/notes", _ => Created("N"));
        _rig.Handler.Map(HttpMethod.Post, "files/v3/files", _ => Created("F"));
        _rig.Handler.Map(HttpMethod.Patch, $"crm/v3/objects/0-3/{Deal}", _ => HubSpotHandler.Json(new { id = Deal }));
    }

    private HttpResponseMessage Created(string prefix) => HubSpotHandler.Json(new { id = $"{prefix}{++_counter}" });

    private HubSpotWriteOnce Once() => new(_ledger, NullLogger<HubSpotWriteOnce>.Instance);

    private HubSpotTaskSink Tasks() => new(_rig.Api, Once(), HubSpotRig.Wrap(_rig.Options), _time, NullLogger<HubSpotTaskSink>.Instance);

    private HubSpotAuditSink Audit() => new(_rig.Api, Once(), HubSpotRig.Wrap(_rig.Options), _time, NullLogger<HubSpotAuditSink>.Instance);

    private HubSpotDocumentArchive Archive() =>
        new(_rig.Api, Once(), HubSpotRig.Wrap(_rig.Options), _time, NullLogger<HubSpotDocumentArchive>.Instance);

    private HubSpotStatusSink Status() =>
        new(_rig.Api, _ledger, new PvAutopilot.Tests.Core.CountingAlert(), HubSpotRig.Wrap(_rig.Options), NullLogger<HubSpotStatusSink>.Instance);

    private HubSpotMastrCredentialStore Mastr() => new(_rig.Api, HubSpotRig.Wrap(_rig.Options), NullLogger<HubSpotMastrCredentialStore>.Instance);

    private static AnmeldeProcess NewProcess() => new(Deal, TestData.Now);

    private static JsonElement Body(RecordedRequest request) => JsonDocument.Parse(request.Body!).RootElement;

    private static string? Prop(RecordedRequest request, string name) =>
        Body(request).GetProperty("properties").TryGetProperty(name, out var value) ? value.GetString() : null;

    private IEnumerable<RecordedRequest> Posts(string path) => _rig.Handler.Writes.Where(r => r.Method == HttpMethod.Post && r.Path == path);

    // Schalter WriteEnabled

    [Fact]
    public async Task Without_write_enabled_no_sink_sends_a_write()
    {
        _rig.Options.WriteEnabled = false;
        var process = NewProcess();
        process.Block("Timeout", TestData.Now);

        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default);
        await Audit().WriteAsync(process, new AuditEvent("blocked", "x", AuditLevel.Error), default);
        await Archive().StoreAsync(Deal, [new GeneratedDocument("a.json", "application/json", [1, 2])], default);
        await Status().PublishAsync(process, default);

        Assert.Empty(_rig.Handler.Writes);
        Assert.Empty(_rig.Handler.Requests);
        Assert.Empty(_ledger.Records);
    }

    [Fact]
    public async Task Write_enabled_is_the_only_switch_the_same_calls_write_when_on()
    {
        var process = NewProcess();

        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default);
        await Audit().WriteAsync(process, new AuditEvent("blocked", "x", AuditLevel.Error), default);
        await Archive().StoreAsync(Deal, [new GeneratedDocument("a.json", "application/json", [1, 2])], default);
        await Status().PublishAsync(process, default);

        Assert.Contains(_rig.Handler.Writes, r => r.Method == HttpMethod.Post && r.Path == "crm/v3/objects/tasks");
        Assert.Contains(_rig.Handler.Writes, r => r.Method == HttpMethod.Post && r.Path == "crm/v3/objects/notes");
        Assert.Contains(_rig.Handler.Writes, r => r.Method == HttpMethod.Post && r.Path == "files/v3/files");
        Assert.Contains(_rig.Handler.Writes, r => r.Method == HttpMethod.Patch);
    }

    // Aufgaben

    [Fact]
    public async Task Task_is_assigned_to_the_deal_owner_and_linked_to_the_deal()
    {
        _rig.Options.DefaultTaskOwnerId = "55";
        _rig.SetDeal(HubSpotRig.BaseDeal(), ownerId: "77");

        await Tasks().CreateTaskAsync(Deal, "task-1", "Vollmacht fehlt", "Bitte nachreichen", default);

        var post = Assert.Single(Posts("crm/v3/objects/tasks"));
        Assert.Equal("77", Prop(post, K.HubSpotOwnerId));
        Assert.Equal("Vollmacht fehlt", Prop(post, "hs_task_subject"));
        Assert.Equal("NOT_STARTED", Prop(post, "hs_task_status"));
        var association = Body(post).GetProperty("associations")[0];
        Assert.Equal(Deal, association.GetProperty("to").GetProperty("id").GetString());
        Assert.Equal(216, association.GetProperty("types")[0].GetProperty("associationTypeId").GetInt32());
    }

    [Fact]
    public async Task Task_falls_back_to_the_default_owner_when_the_deal_has_none()
    {
        _rig.Options.DefaultTaskOwnerId = " 55 ";

        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default);

        Assert.Equal("55", Prop(Assert.Single(Posts("crm/v3/objects/tasks")), K.HubSpotOwnerId));
    }

    [Fact]
    public async Task Task_is_unassigned_without_deal_owner_and_default()
    {
        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default);

        Assert.Null(Prop(Assert.Single(Posts("crm/v3/objects/tasks")), K.HubSpotOwnerId));
    }

    [Fact]
    public async Task Task_body_is_html_encoded()
    {
        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "a <b> & c\nzweite Zeile", default);

        Assert.Equal("a &lt;b&gt; &amp; c<br>zweite Zeile", Prop(Assert.Single(Posts("crm/v3/objects/tasks")), "hs_task_body"));
    }

    [Fact]
    public async Task Same_task_key_creates_only_one_task_even_after_a_retry_with_a_new_sink()
    {
        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default);
        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default);
        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default);

        Assert.Single(Posts("crm/v3/objects/tasks"));
        Assert.Equal("T1", _ledger.Records["task:task-1"].ExternalId);
    }

    [Fact]
    public async Task Different_task_keys_create_different_tasks()
    {
        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default);
        await Tasks().CreateTaskAsync(Deal, "task-2", "Titel", "Text", default);

        Assert.Equal(2, Posts("crm/v3/objects/tasks").Count());
    }

    [Fact]
    public async Task Failed_task_write_is_retried_and_created_exactly_once()
    {
        var fail = true;
        _rig.Handler.Map(HttpMethod.Post, "crm/v3/objects/tasks", _ => fail ? HubSpotHandler.Status(HttpStatusCode.InternalServerError, "Erika") : Created("T"));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default));
        Assert.DoesNotContain("Erika", error.Message);
        Assert.Empty(_ledger.Records);

        fail = false;
        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default);
        await Tasks().CreateTaskAsync(Deal, "task-1", "Titel", "Text", default);

        Assert.Single(_ledger.Records);
        Assert.Equal(2, Posts("crm/v3/objects/tasks").Count());
    }

    // Audit

    public static TheoryData<AuditLevel, AuditLevel, bool> AuditFilter => new()
    {
        { AuditLevel.Verbose, AuditLevel.Verbose, true },
        { AuditLevel.Verbose, AuditLevel.Info, true },
        { AuditLevel.Verbose, AuditLevel.Error, true },
        { AuditLevel.Info, AuditLevel.Verbose, false },
        { AuditLevel.Info, AuditLevel.Info, true },
        { AuditLevel.Info, AuditLevel.Error, true },
        { AuditLevel.Error, AuditLevel.Verbose, false },
        { AuditLevel.Error, AuditLevel.Info, false },
        { AuditLevel.Error, AuditLevel.Error, true },
    };

    [Theory]
    [MemberData(nameof(AuditFilter))]
    public async Task Audit_note_is_written_only_when_the_event_level_reaches_the_configured_level(
        AuditLevel configured, AuditLevel eventLevel, bool written)
    {
        _rig.Options.AuditLevel = configured;

        await Audit().WriteAsync(NewProcess(), new AuditEvent("state-changed", "A → B", eventLevel), default);

        Assert.Equal(written ? 1 : 0, Posts("crm/v3/objects/notes").Count());
    }

    [Fact]
    public async Task Default_audit_level_is_info()
    {
        var sink = Audit();

        await sink.WriteAsync(NewProcess(), new AuditEvent("t", "verbose", AuditLevel.Verbose), default);
        await sink.WriteAsync(NewProcess(), new AuditEvent("t", "info", AuditLevel.Info), default);

        Assert.Single(Posts("crm/v3/objects/notes"));
    }

    [Fact]
    public async Task Audit_note_is_linked_to_the_deal_and_names_type_and_message()
    {
        await Audit().WriteAsync(NewProcess(), new AuditEvent("state-changed", "Created → Done", AuditLevel.Info), default);

        var post = Assert.Single(Posts("crm/v3/objects/notes"));
        Assert.Contains("state-changed", Prop(post, "hs_note_body"));
        Assert.Contains("Created →", Prop(post, "hs_note_body"));
        var association = Body(post).GetProperty("associations")[0];
        Assert.Equal(Deal, association.GetProperty("to").GetProperty("id").GetString());
        Assert.Equal(214, association.GetProperty("types")[0].GetProperty("associationTypeId").GetInt32());
    }

    [Fact]
    public async Task Same_audit_event_is_noted_once_but_a_new_state_change_is_noted_again()
    {
        var process = NewProcess();
        var evt = new AuditEvent("state-changed", "Created → DataCardAggregated", AuditLevel.Info);
        process.TransitionTo(ProcessState.DataCardAggregated, TestData.Now);

        await Audit().WriteAsync(process, evt, default);
        await Audit().WriteAsync(process, evt, default);
        Assert.Single(Posts("crm/v3/objects/notes"));

        process.TransitionTo(ProcessState.NetzbetreiberResolved, TestData.Now);
        await Audit().WriteAsync(process, new AuditEvent("state-changed", "DataCardAggregated → NetzbetreiberResolved", AuditLevel.Info), default);
        Assert.Equal(2, Posts("crm/v3/objects/notes").Count());
    }

    // Ablage

    [Fact]
    public async Task Archive_uploads_privately_into_the_deal_folder_and_attaches_the_file_with_a_note()
    {
        await Archive().StoreAsync(Deal, [new GeneratedDocument("nab-datenkarte-r0.json", "application/json", [1, 2, 3])], default);

        var upload = Assert.Single(Posts("files/v3/files"));
        Assert.Contains("/pv-autopilot/101", upload.Body);
        Assert.Contains("PRIVATE", upload.Body);
        Assert.Contains("nab-datenkarte-r0.json", upload.Body);

        var note = Assert.Single(Posts("crm/v3/objects/notes"));
        Assert.Equal("F1", Prop(note, "hs_attachment_ids"));
        Assert.Equal(Deal, Body(note).GetProperty("associations")[0].GetProperty("to").GetProperty("id").GetString());
        // Erst die Datei, dann die Notiz.
        Assert.True(_rig.Handler.Requests.FindIndex(r => r.Path == "files/v3/files") < _rig.Handler.Requests.FindIndex(r => r.Path == "crm/v3/objects/notes"));
    }

    [Fact]
    public async Task Archive_attaches_several_files_to_one_note()
    {
        await Archive().StoreAsync(Deal,
            [new GeneratedDocument("a.pdf", "application/pdf", [1]), new GeneratedDocument("b.pdf", "application/pdf", [2])], default);

        Assert.Equal(2, Posts("files/v3/files").Count());
        Assert.Equal("F1;F2", Prop(Assert.Single(Posts("crm/v3/objects/notes")), "hs_attachment_ids"));
    }

    [Fact]
    public async Task Archive_with_no_documents_does_nothing()
    {
        await Archive().StoreAsync(Deal, [], default);

        Assert.Empty(_rig.Handler.Requests);
    }

    [Fact]
    public async Task Same_document_stored_again_creates_neither_file_nor_note()
    {
        var document = new GeneratedDocument("a.json", "application/json", [1, 2, 3]);

        await Archive().StoreAsync(Deal, [document], default);
        await Archive().StoreAsync(Deal, [new GeneratedDocument("a.json", "application/json", [1, 2, 3])], default);

        Assert.Single(Posts("files/v3/files"));
        Assert.Single(Posts("crm/v3/objects/notes"));
    }

    [Fact]
    public async Task Same_name_with_different_content_is_stored_as_a_new_file()
    {
        await Archive().StoreAsync(Deal, [new GeneratedDocument("a.json", "application/json", [1])], default);
        await Archive().StoreAsync(Deal, [new GeneratedDocument("a.json", "application/json", [2])], default);

        Assert.Equal(2, Posts("files/v3/files").Count());
        Assert.Equal(2, Posts("crm/v3/objects/notes").Count());
    }

    [Fact]
    public async Task Retry_after_a_failed_note_does_not_upload_the_file_again()
    {
        var failNote = true;
        _rig.Handler.Map(HttpMethod.Post, "crm/v3/objects/notes", _ => failNote ? HubSpotHandler.Status(HttpStatusCode.BadGateway) : Created("N"));
        var document = new GeneratedDocument("a.json", "application/json", [1, 2, 3]);

        await Assert.ThrowsAsync<HttpRequestException>(() => Archive().StoreAsync(Deal, [document], default));
        failNote = false;
        await Archive().StoreAsync(Deal, [document], default);
        await Archive().StoreAsync(Deal, [document], default);

        Assert.Single(Posts("files/v3/files"));
        Assert.Equal(2, Posts("crm/v3/objects/notes").Count());
    }

    [Fact]
    public async Task Failed_upload_creates_no_note_and_no_ledger_entry()
    {
        _rig.Handler.Map(HttpMethod.Post, "files/v3/files", _ => HubSpotHandler.Status(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Archive().StoreAsync(Deal, [new GeneratedDocument("a.json", "application/json", [1])], default));

        Assert.Empty(Posts("crm/v3/objects/notes"));
        Assert.Empty(_ledger.Records);
    }

    // Status

    [Fact]
    public async Task Status_patch_writes_the_known_properties_to_the_deal()
    {
        var process = NewProcess();
        process.AssignNetzbetreiber("netze-duisburg", TestData.Now);
        process.RecordMastrRegistration("SEE900000000001", "SSE900000000002", TestData.Now);

        await Status().PublishAsync(process, default);

        var patch = Assert.Single(_rig.Handler.Writes);
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.Equal($"crm/v3/objects/0-3/{Deal}", patch.Path);
        Assert.Equal("Created", Prop(patch, "status"));
        Assert.Equal("netze-duisburg", Prop(patch, "netzbetreiber"));
        Assert.Equal("", Prop(patch, "fehlertext"));
        Assert.Equal("SEE900000000001", Prop(patch, "mastr_nummer_anlage"));
        Assert.Equal("SSE900000000002", Prop(patch, "mastr_nummer_speicher"));
    }

    [Fact]
    public async Task Status_patch_skips_properties_with_an_empty_name()
    {
        _rig.Options.Properties[K.Netzbetreiber] = "";
        _rig.Options.Properties[K.MastrNummerAnlage] = "  ";
        _rig.Options.Properties.Remove(K.MastrNummerSpeicher);
        var process = NewProcess();
        process.AssignNetzbetreiber("netze-duisburg", TestData.Now);
        process.RecordMastrRegistration("SEE900000000001", "SSE900000000002", TestData.Now);

        await Status().PublishAsync(process, default);

        var properties = Body(Assert.Single(_rig.Handler.Writes)).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["fehlertext", "status"], properties);
    }

    [Fact]
    public async Task Status_patch_leaves_unknown_values_out_instead_of_clearing_them()
    {
        await Status().PublishAsync(NewProcess(), default);

        var properties = Body(Assert.Single(_rig.Handler.Writes)).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["fehlertext", "status"], properties);
    }

    [Fact]
    public async Task Error_text_is_the_block_reason_while_blocked_and_cleared_afterwards()
    {
        var process = NewProcess();
        process.Block("HttpRequestException: HTTP 503", TestData.Now);
        await Status().PublishAsync(process, default);
        process.Resume(TestData.Now);
        await Status().PublishAsync(process, default);

        var patches = _rig.Handler.Writes.ToList();
        Assert.Equal("Blocked", Prop(patches[0], "status"));
        Assert.Equal("HttpRequestException: HTTP 503", Prop(patches[0], "fehlertext"));
        Assert.Equal("Created", Prop(patches[1], "status"));
        Assert.Equal("", Prop(patches[1], "fehlertext"));
    }

    [Fact]
    public async Task Status_without_any_configured_property_sends_nothing()
    {
        _rig.Options.Properties.Clear();

        await Status().PublishAsync(NewProcess(), default);

        Assert.Empty(_rig.Handler.Requests);
    }

    [Fact]
    public async Task Status_patch_failure_is_technical_without_body()
    {
        _rig.Handler.Map(HttpMethod.Patch, $"crm/v3/objects/0-3/{Deal}", _ => HubSpotHandler.Status(HttpStatusCode.InternalServerError, "Erika Mustermann"));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Status().PublishAsync(NewProcess(), default));

        Assert.DoesNotContain("Mustermann", error.Message);
    }

    // MaStR-Login

    [Fact]
    public async Task Mastr_login_is_read_from_both_deal_properties_and_only_those_are_requested()
    {
        var deal = HubSpotRig.BaseDeal();
        deal[K.MastrBenutzername] = "kunde-login";
        deal[K.MastrPasswort] = "pw-1234";
        _rig.SetDeal(deal);

        var credentials = await Mastr().GetAsync(Deal, default);

        Assert.Equal(new PvAutopilot.Core.Mastr.MastrCredentials("kunde-login", "pw-1234"), credentials);
        var request = Assert.Single(_rig.Handler.Requests);
        Assert.Equal(["mastr_benutzername", "mastr_passwort"], request.RequestedProperties.Order());
    }

    [Theory]
    [InlineData(K.MastrBenutzername)]
    [InlineData(K.MastrPasswort)]
    public async Task Mastr_login_is_null_when_one_property_is_missing_or_empty(string missing)
    {
        var deal = HubSpotRig.BaseDeal();
        deal[K.MastrBenutzername] = "kunde-login";
        deal[K.MastrPasswort] = "pw-1234";
        deal[missing] = " ";
        _rig.SetDeal(deal);

        Assert.Null(await Mastr().GetAsync(Deal, default));
    }

    [Fact]
    public async Task Mastr_login_is_null_when_the_deal_is_unknown()
    {
        _rig.Handler.Get($"crm/v3/objects/0-3/{Deal}", HttpStatusCode.NotFound);

        Assert.Null(await Mastr().GetAsync(Deal, default));
    }

    [Fact]
    public async Task Mastr_login_is_null_without_a_request_when_the_properties_are_not_configured()
    {
        _rig.Options.Properties.Remove(K.MastrPasswort);

        Assert.Null(await Mastr().GetAsync(Deal, default));
        Assert.Empty(_rig.Handler.Requests);
    }

    [Fact]
    public async Task Mastr_delete_sends_no_request_even_with_write_enabled()
    {
        await Mastr().DeleteAsync(Deal, default);

        Assert.Empty(_rig.Handler.Requests);
    }

    [Fact]
    public async Task Mastr_logs_never_contain_the_login()
    {
        var deal = HubSpotRig.BaseDeal();
        deal[K.MastrBenutzername] = "kunde-login";
        deal[K.MastrPasswort] = "pw-1234";
        _rig.SetDeal(deal);
        var log = new CapturingLogger<HubSpotMastrCredentialStore>();
        var store = new HubSpotMastrCredentialStore(_rig.Api, HubSpotRig.Wrap(_rig.Options), log);

        await store.GetAsync(Deal, default);
        await store.DeleteAsync(Deal, default);

        Assert.NotEmpty(log.Messages);
        Assert.DoesNotContain("kunde-login", log.All);
        Assert.DoesNotContain("pw-1234", log.All);
    }
}
