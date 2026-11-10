using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Orders;
using PvAutopilot.Sources.HubSpot;
using K = PvAutopilot.Sources.HubSpot.HubSpotPropertyKeys;

namespace PvAutopilot.Tests.Sources.HubSpot;

/// <summary>Auftragsübernahme gegen simuliertes HubSpot: Vorschau, Artikel, Properties, PDF, Idempotenz, fremde/zweite Unterschrift.</summary>
public class HubSpotOrderSinkTests
{
    private const string Deal = HubSpotRig.DealId;
    private const string Sig = "sig-1";

    private readonly HubSpotRig _rig = new();
    private readonly InMemoryCrmWriteLedger _ledger = new();
    private readonly FixedTime _time = new(TestData.Now);
    private readonly CapturingLogger<HubSpotOrderSink> _log = new();
    private readonly List<string> _associated = [];
    private int _counter;
    private bool _failPatch;
    private bool _failUpload;

    public HubSpotOrderSinkTests()
    {
        _rig.Options.WriteEnabled = true;
        _rig.Handler.Map(HttpMethod.Post, "crm/v3/objects/line_items", _ =>
        {
            var id = (++_counter).ToString();
            _associated.Add(id);
            return HubSpotHandler.Json(new { id });
        });
        _rig.Handler.Map(HttpMethod.Get, $"crm/v4/objects/0-3/{Deal}/associations/line_items", _ =>
            HubSpotHandler.Json(new { results = _associated.Select(id => new { toObjectId = id }) }));
        _rig.Handler.Map(HttpMethod.Post, "crm/v3/objects/tasks", _ => HubSpotHandler.Json(new { id = $"T{++_counter}" }));
        _rig.Handler.Map(HttpMethod.Post, "crm/v3/objects/notes", _ => HubSpotHandler.Json(new { id = $"N{++_counter}" }));
        _rig.Handler.Map(HttpMethod.Post, "files/v3/files", _ =>
            _failUpload ? HubSpotHandler.Status(HttpStatusCode.InternalServerError) : HubSpotHandler.Json(new { id = $"F{++_counter}" }));
        _rig.Handler.Map(HttpMethod.Patch, $"crm/v3/objects/0-3/{Deal}", _ =>
            _failPatch ? HubSpotHandler.Status(HttpStatusCode.InternalServerError) : HubSpotHandler.Json(new { id = Deal }));
    }

    private HubSpotOrderSink Sink()
    {
        var options = HubSpotRig.Wrap(_rig.Options);
        var once = new HubSpotWriteOnce(_ledger, NullLogger<HubSpotWriteOnce>.Instance);
        return new HubSpotOrderSink(_rig.Api, once, _ledger,
            new HubSpotTaskSink(_rig.Api, once, options, _time, NullLogger<HubSpotTaskSink>.Instance),
            new HubSpotDocumentArchive(_rig.Api, once, options, _time, NullLogger<HubSpotDocumentArchive>.Instance),
            options, _log);
    }

    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46];

    private static SignedOrder Order(string signature = Sig, bool withPdfRef = true) => new()
    {
        PlanningProjectId = "reonic-1",
        SignatureRequestId = signature,
        IsSigned = true,
        CrmProjectId = Deal,
        SignedAt = new DateTimeOffset(2026, 11, 9, 23, 30, 0, TimeSpan.Zero),
        VariantId = "v1",
        Positions =
        [
            new OrderPosition("module", "Hersteller A", "Modul 450", "ART-1", "Langtext Modul", 20m, 119.5m, 19m),
            new OrderPosition("inverter", "Hersteller B", "WR 8K", null, null, 1m, 1500m, 19m),
            new OrderPosition("service", "", "Montage", null, null, 1m, null, null),
        ],
        Summary = new OrderSummary(
            [new ModulPosition("Hersteller A", "Modul 450", 450m, 20m)],
            [new WechselrichterPosition("Hersteller B", "WR 8K", null, 8000m, 1m, null)],
            new Batteriespeicher("Hersteller C", "Speicher 2.56", 10.24m, null),
            null),
        SignedPdf = withPdfRef ? new DocumentRef(DocumentKind.Auftrag, "reonic-signature", "https://files.example/geheim.pdf", "Mustermann Erika Auftrag.pdf") : null,
    };

    private static JsonElement Body(RecordedRequest request) => JsonDocument.Parse(request.Body!).RootElement;

    private static Dictionary<string, string> Props(RecordedRequest request) =>
        Body(request).GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);

    private IEnumerable<RecordedRequest> Posts(string path) => _rig.Handler.Writes.Where(r => r.Method == HttpMethod.Post && r.Path == path);

    private IEnumerable<RecordedRequest> Patches => _rig.Handler.Writes.Where(r => r.Method == HttpMethod.Patch);

    // Vorschau (WriteEnabled=false)

    [Fact]
    public async Task Without_write_enabled_only_gets_are_sent_and_the_preview_has_counts_and_names()
    {
        _rig.Options.WriteEnabled = false;
        _associated.AddRange(["900", "901"]);

        var result = await Sink().PublishAsync(Order(), Pdf, default);

        Assert.All(_rig.Handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.Empty(_rig.Handler.Writes);
        Assert.Empty(_ledger.Records);
        Assert.False(result.Written);
        Assert.Equal(OrderSkipReasons.WriteDisabled, result.SkipReason);
        Assert.Equal(3, result.LineItemCount);
        Assert.True(result.PdfStored);
        Assert.Equal(2, result.ForeignLineItems);
        Assert.Equal(
            ["auftrag_kwp", "auftrag_module", "auftrag_speicher", "auftrag_speicher_kwh", "auftrag_speicher_vorhanden", "auftrag_unterschriftsdatum",
             "auftrag_wallbox_vorhanden", "auftrag_wechselrichter", "auftrag_wr_leistung_kw"],
            result.PropertyNames);
    }

    [Fact]
    public async Task Preview_without_pdf_says_pdf_would_not_be_stored()
    {
        _rig.Options.WriteEnabled = false;

        var result = await Sink().PublishAsync(Order(withPdfRef: false), null, default);

        Assert.False(result.PdfStored);
        Assert.Equal(0, result.ForeignLineItems);
    }

    // Artikel

    [Fact]
    public async Task Each_position_becomes_a_line_item_associated_with_the_deal_with_type_20()
    {
        var result = await Sink().PublishAsync(Order(), null, default);

        var posts = Posts("crm/v3/objects/line_items").ToList();
        Assert.Equal(3, posts.Count);

        var first = Props(posts[0]);
        Assert.Equal("Hersteller A Modul 450", first["name"]);
        Assert.Equal("20", first["quantity"]);
        Assert.Equal("119.5", first["price"]);
        Assert.Equal("ART-1", first["hs_sku"]);
        Assert.Equal("Langtext Modul", first["description"]);
        Assert.Equal("0", first["hs_position_on_quote"]);

        var association = Body(posts[0]).GetProperty("associations")[0];
        Assert.Equal(Deal, association.GetProperty("to").GetProperty("id").GetString());
        var type = association.GetProperty("types")[0];
        Assert.Equal(20, type.GetProperty("associationTypeId").GetInt32());
        Assert.Equal("HUBSPOT_DEFINED", type.GetProperty("associationCategory").GetString());

        var second = Props(posts[1]);
        Assert.Equal("Hersteller B WR 8K", second["name"]);
        Assert.Equal("1500", second["price"]);
        Assert.DoesNotContain("hs_sku", second.Keys);
        Assert.DoesNotContain("description", second.Keys);

        // Position ohne Preis und ohne Hersteller: Name ohne führendes Leerzeichen, kein Preis geschrieben.
        var third = Props(posts[2]);
        Assert.Equal("Montage", third["name"]);
        Assert.DoesNotContain("price", third.Keys);

        Assert.True(result.Written);
        Assert.Equal(3, result.LineItemCount);
    }

    [Fact]
    public void Line_item_numbers_stay_invariant_under_german_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var props = HubSpotOrderSink.LineItemProperties(new OrderPosition("x", "B", "N", null, null, 2.5m, 1234.56m, null), 4);

            Assert.Equal("2.5", props["quantity"]);
            Assert.Equal("1234.56", props["price"]);
            Assert.Equal("4", props["hs_position_on_quote"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Line_items_are_recorded_per_signature_and_position_in_the_ledger()
    {
        await Sink().PublishAsync(Order(), null, default);

        Assert.Equal("1", _ledger.Records[$"lineitem:{Deal}:{Sig}:0"].ExternalId);
        Assert.Equal("2", _ledger.Records[$"lineitem:{Deal}:{Sig}:1"].ExternalId);
        Assert.Equal("3", _ledger.Records[$"lineitem:{Deal}:{Sig}:2"].ExternalId);
    }

    [Fact]
    public async Task Second_run_with_the_same_signature_creates_no_line_items_twice()
    {
        await Sink().PublishAsync(Order(), Pdf, default);
        var again = await Sink().PublishAsync(Order(), Pdf, default);

        Assert.Equal(3, Posts("crm/v3/objects/line_items").Count());
        Assert.Single(Posts("files/v3/files"));
        Assert.Single(Posts("crm/v3/objects/notes"));
        Assert.Empty(Posts("crm/v3/objects/tasks"));
        Assert.True(again.Written);
        Assert.Equal(0, again.ForeignLineItems);
        Assert.Equal(3, again.LineItemCount);
    }

    [Fact]
    public async Task Line_items_and_tasks_always_target_the_deal_regardless_of_the_configured_object_type()
    {
        _rig.Options.ObjectTypeId = "2-99";
        _associated.Add("900");

        await Sink().PublishAsync(Order(), Pdf, default);

        Assert.DoesNotContain(_rig.Handler.Requests, r => r.Path.Contains("2-99"));
        Assert.Single(Patches);
        foreach (var path in new[] { "crm/v3/objects/tasks", "crm/v3/objects/notes", "crm/v3/objects/line_items" })
            Assert.All(Posts(path), r => Assert.Equal(Deal, Body(r).GetProperty("associations")[0].GetProperty("to").GetProperty("id").GetString()));
        Assert.NotEmpty(Posts("crm/v3/objects/tasks"));
    }

    // Properties

    [Fact]
    public async Task Patch_contains_exactly_the_configured_auftrag_properties()
    {
        var result = await Sink().PublishAsync(Order(), null, default);

        var patch = Assert.Single(Patches);
        Assert.Equal(new Dictionary<string, string>
        {
            ["auftrag_unterschriftsdatum"] = "2026-11-10",
            ["auftrag_kwp"] = "9",
            ["auftrag_module"] = "20 × Hersteller A Modul 450 (450 Wp)",
            ["auftrag_wechselrichter"] = "1 × Hersteller B WR 8K (8 kW)",
            ["auftrag_wr_leistung_kw"] = "8",
            ["auftrag_speicher_vorhanden"] = "true",
            ["auftrag_speicher"] = "Hersteller C Speicher 2.56 (10.24 kWh)",
            ["auftrag_speicher_kwh"] = "10.24",
            ["auftrag_wallbox_vorhanden"] = "false",
        }, Props(patch));
        Assert.Equal(Props(patch).Keys.Order(StringComparer.Ordinal), result.PropertyNames);
    }

    [Fact]
    public async Task Only_configured_property_names_are_patched()
    {
        var keep = _rig.Options.Properties[K.AuftragKwp];
        foreach (var key in _rig.Options.Properties.Keys.Where(k => k.StartsWith("Auftrag", StringComparison.Ordinal) && k != K.AuftragKwp).ToList())
            _rig.Options.Properties.Remove(key);

        await Sink().PublishAsync(Order(), null, default);

        Assert.Equal(new Dictionary<string, string> { [keep] = "9" }, Props(Assert.Single(Patches)));
    }

    [Fact]
    public async Task Without_configured_properties_there_is_no_patch_but_line_items_are_created()
    {
        foreach (var key in _rig.Options.Properties.Keys.Where(k => k.StartsWith("Auftrag", StringComparison.Ordinal)).ToList())
            _rig.Options.Properties.Remove(key);

        var result = await Sink().PublishAsync(Order(), null, default);

        Assert.Empty(Patches);
        Assert.Equal(3, Posts("crm/v3/objects/line_items").Count());
        Assert.Empty(result.PropertyNames);
        Assert.True(result.Written);
    }

    // PDF

    [Fact]
    public async Task Pdf_is_uploaded_with_stable_archive_key_and_a_file_name_without_customer_name()
    {
        var result = await Sink().PublishAsync(Order(), Pdf, default);

        var upload = Assert.Single(Posts("files/v3/files"));
        Assert.Contains("Auftrag unterschrieben 2026-11-10.pdf", upload.Body);
        Assert.DoesNotContain("Mustermann", upload.Body);
        Assert.Contains("/pv-autopilot/101", upload.Body);
        Assert.Contains("PRIVATE", upload.Body);
        Assert.True(_ledger.Records.ContainsKey($"file:{Deal}:order-pdf:{Sig}"));
        Assert.True(result.PdfStored);

        var note = Assert.Single(Posts("crm/v3/objects/notes"));
        Assert.Equal(_ledger.Records[$"file:{Deal}:order-pdf:{Sig}"].ExternalId, Props(note)["hs_attachment_ids"]);
        var association = Body(note).GetProperty("associations")[0];
        Assert.Equal(Deal, association.GetProperty("to").GetProperty("id").GetString());
        Assert.Equal(214, association.GetProperty("types")[0].GetProperty("associationTypeId").GetInt32());
    }

    [Fact]
    public async Task Different_pdf_content_for_the_same_signature_is_not_uploaded_again()
    {
        await Sink().PublishAsync(Order(), Pdf, default);
        await Sink().PublishAsync(Order(), [1, 2, 3, 4, 5], default);

        Assert.Single(Posts("files/v3/files"));
    }

    [Fact]
    public async Task Without_pdf_nothing_is_uploaded_and_pdf_stored_is_false()
    {
        var result = await Sink().PublishAsync(Order(withPdfRef: false), null, default);

        Assert.Empty(Posts("files/v3/files"));
        Assert.Empty(Posts("crm/v3/objects/notes"));
        Assert.False(result.PdfStored);
        Assert.True(result.Written);
    }

    [Fact]
    public void Pdf_file_name_uses_the_signing_day_in_the_instance_time_zone()
    {
        var options = HubSpotRig.DefaultOptions();

        Assert.Equal("Auftrag unterschrieben 2026-11-10.pdf", HubSpotOrderSink.PdfFileName(Order(), options));
        Assert.Equal("Auftrag unterschrieben.pdf", HubSpotOrderSink.PdfFileName(Order() with { SignedAt = null }, options));
    }

    // Deal nicht gefunden

    [Fact]
    public async Task Missing_deal_gives_deal_not_found_without_writes()
    {
        _rig.Handler.Routes.Remove($"GET crm/v3/objects/0-3/{Deal}");

        var result = await Sink().PublishAsync(Order(), Pdf, default);

        Assert.True(result.DealNotFound);
        Assert.False(result.Written);
        Assert.Empty(_rig.Handler.Writes);
        Assert.Empty(_ledger.Records);
    }

    [Fact]
    public async Task Missing_deal_is_detected_in_preview_mode_too()
    {
        _rig.Options.WriteEnabled = false;
        _rig.Handler.Routes.Remove($"GET crm/v3/objects/0-3/{Deal}");

        var result = await Sink().PublishAsync(Order(), Pdf, default);

        Assert.Equal(OrderSkipReasons.DealNotFound, result.SkipReason);
    }

    // Zweite Unterschrift

    [Fact]
    public async Task Other_signature_for_a_transferred_deal_writes_nothing_but_one_task()
    {
        await Sink().PublishAsync(Order(), Pdf, default);
        var writesBefore = _rig.Handler.Writes.Count();

        var second = await Sink().PublishAsync(Order("sig-2"), Pdf, default);
        await Sink().PublishAsync(Order("sig-2"), Pdf, default);

        var newWrites = _rig.Handler.Writes.Skip(writesBefore).ToList();
        var task = Assert.Single(newWrites);
        Assert.Equal("crm/v3/objects/tasks", task.Path);
        Assert.Equal(HubSpotOrderSink.SecondSignatureTaskTitle, Props(task)["hs_task_subject"]);
        Assert.True(_ledger.Records.ContainsKey("task:order-second-signature:sig-2"));
        Assert.False(second.Written);
        Assert.Equal(OrderSkipReasons.SecondSignature, second.SkipReason);
        Assert.Equal(3, second.LineItemCount);
        // Der Abschluss der ersten Unterschrift bleibt unverändert.
        Assert.Equal(Sig, _ledger.Records[$"order:{Deal}"].ExternalId);
        Assert.DoesNotContain(_ledger.Records.Keys, k => k.Contains("sig-2:"));
    }

    [Fact]
    public async Task Other_signature_in_preview_mode_sends_no_write_either()
    {
        await Sink().PublishAsync(Order(), Pdf, default);
        _rig.Options.WriteEnabled = false;
        var requestsBefore = _rig.Handler.Requests.Count;

        var result = await Sink().PublishAsync(Order("sig-2"), Pdf, default);

        Assert.Equal(OrderSkipReasons.SecondSignature, result.SkipReason);
        Assert.All(_rig.Handler.Requests.Skip(requestsBefore), r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    // Fremde Artikel

    [Fact]
    public async Task Foreign_line_items_create_one_task_and_the_own_items_are_created_anyway()
    {
        _associated.AddRange(["900", "901"]);

        var result = await Sink().PublishAsync(Order(), null, default);
        await Sink().PublishAsync(Order(), null, default);

        var task = Assert.Single(Posts("crm/v3/objects/tasks"));
        Assert.Equal(HubSpotOrderSink.ForeignItemsTaskTitle, Props(task)["hs_task_subject"]);
        Assert.True(_ledger.Records.ContainsKey($"task:order-foreign-items:{Sig}"));
        Assert.Equal(3, Posts("crm/v3/objects/line_items").Count());
        Assert.Equal(2, result.ForeignLineItems);
        Assert.True(result.Written);
    }

    [Fact]
    public async Task Own_items_from_an_earlier_run_do_not_count_as_foreign()
    {
        await Sink().PublishAsync(Order(), null, default);
        _associated.Add("900");

        var result = await Sink().PublishAsync(Order(), null, default);

        Assert.Equal(1, result.ForeignLineItems);
    }

    [Fact]
    public async Task No_associated_items_means_no_foreign_task()
    {
        var result = await Sink().PublishAsync(Order(), null, default);

        Assert.Empty(Posts("crm/v3/objects/tasks"));
        Assert.Equal(0, result.ForeignLineItems);
    }

    // Reihenfolge und Wiederaufnahme

    [Fact]
    public async Task Ledger_entry_for_the_order_is_only_set_at_the_end_so_a_failing_patch_leaves_it_unset()
    {
        _failPatch = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => Sink().PublishAsync(Order(), Pdf, default));

        Assert.DoesNotContain($"order:{Deal}", _ledger.Records.Keys);
        Assert.Equal(3, _ledger.Records.Keys.Count(k => k.StartsWith("lineitem:", StringComparison.Ordinal)));
        Assert.Empty(Posts("files/v3/files"));
    }

    [Fact]
    public async Task Ledger_entry_for_the_order_is_unset_when_the_pdf_upload_fails()
    {
        _failUpload = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => Sink().PublishAsync(Order(), Pdf, default));

        Assert.DoesNotContain($"order:{Deal}", _ledger.Records.Keys);
    }

    [Fact]
    public async Task Retry_after_a_failed_patch_completes_without_duplicate_line_items()
    {
        _failPatch = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => Sink().PublishAsync(Order(), Pdf, default));
        _failPatch = false;

        var result = await Sink().PublishAsync(Order(), Pdf, default);

        Assert.True(result.Written);
        Assert.Equal(3, Posts("crm/v3/objects/line_items").Count());
        Assert.Single(Posts("files/v3/files"));
        Assert.Equal(Sig, _ledger.Records[$"order:{Deal}"].ExternalId);
        Assert.Equal(0, result.ForeignLineItems);
        Assert.Empty(Posts("crm/v3/objects/tasks"));
    }

    [Fact]
    public async Task Ledger_entry_for_the_order_holds_the_signature_id()
    {
        await Sink().PublishAsync(Order(), null, default);

        Assert.Equal(Sig, _ledger.Records[$"order:{Deal}"].ExternalId);
    }

    // Eingaben

    [Theory]
    [InlineData(null, Sig)]
    [InlineData("", Sig)]
    [InlineData(Deal, null)]
    [InlineData(Deal, " ")]
    public async Task Order_without_deal_or_signature_is_rejected(string? deal, string? signature)
    {
        var order = Order() with { CrmProjectId = deal, SignatureRequestId = signature };

        await Assert.ThrowsAsync<ArgumentException>(() => Sink().PublishAsync(order, null, default));

        Assert.Empty(_rig.Handler.Requests);
    }

    // Logging

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Logs_contain_no_names_prices_descriptions_urls_or_file_names(bool writeEnabled)
    {
        _rig.Options.WriteEnabled = writeEnabled;
        _associated.Add("900");
        var order = Order() with
        {
            Positions = [new OrderPosition("module", "Geheimmarke", "Geheimname", "ART-GEHEIM", "Geheimtext", 7m, 7777.77m, 19m)],
        };

        await Sink().PublishAsync(order, Pdf, default);
        await Sink().PublishAsync(Order("sig-2") with { Positions = order.Positions }, Pdf, default);

        Assert.NotEmpty(_log.Messages);
        foreach (var secret in new[] { "Geheimmarke", "Geheimname", "ART-GEHEIM", "Geheimtext", "7777", "geheim.pdf", "files.example", "Mustermann", "Auftrag unterschrieben", "119.5", "Langtext" })
            Assert.DoesNotContain(secret, _log.All);
        Assert.Contains(Deal, _log.All);
        Assert.Contains(Sig, _log.All);
    }
}
