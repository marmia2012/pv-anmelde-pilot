using System.Globalization;
using System.Net;
using System.Text;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Infrastructure.Logging;
using PvAutopilot.Sources.Reonic;
using PvAutopilot.Sources.Reonic.Api;
using PvAutopilot.Tests.Sources.HubSpot;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using static PvAutopilot.Tests.Sources.Reonic.ReonicFixtures;

namespace PvAutopilot.Tests.Sources.Reonic;

/// <summary>Antwortet je Pfad relativ zur Basis-URL (bzw. je absoluter URL); unbekannte Pfade sind 404. Fiktive Daten.</summary>
internal sealed class ReonicRoutes : HttpMessageHandler
{
    private const string ApiPrefix = "/rest/v3/";

    public Dictionary<string, string> Json { get; } = [];
    public List<string> Calls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var key = path.StartsWith(ApiPrefix, StringComparison.Ordinal) ? path[ApiPrefix.Length..] : uri.ToString();
        Calls.Add(key);
        return Task.FromResult(Json.TryGetValue(key, out var json)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public class ReonicSignedOrderSourceTests
{
    private const string Sig1 = "sig-1";
    private const string Sig2 = "sig-2";
    private const string PdfUrl = "https://files.example/signed/geheim-token-4711.pdf";
    private const string Gross1 = """{"gross":1}""";

    private readonly ReonicRoutes _routes = new();
    private readonly CapturingLogger<ReonicSignedOrderSource> _log = new();

    public ReonicSignedOrderSourceTests()
    {
        _routes.Json[$"residentialProjects/{ProjectId}"] = Project();
        _routes.Json[$"residentialProjects/{ProjectId}/variants/{VariantId}"] = Variant();
        _routes.Json[$"residentialProjects/{ProjectId}/signatureRequests"] = Requests(Request(Sig1, "signed", VariantId, PdfUrl, "2026-11-09T23:30:00Z"));
        foreach (var (path, json) in Components)
            _routes.Json[path] = json;
    }

    private ReonicSignedOrderSource Source()
    {
        var http = new HttpClient(_routes) { BaseAddress = new Uri("https://api.reonic.test/rest/v3/") };
        return new ReonicSignedOrderSource(new ReonicApiClient(http), new ReonicFileDownloader(new HttpClient(_routes)), _log);
    }

    private static string Request(string id, string status, string? variantId, string? pdfUrl, string? signedAt) => $$"""
        {
          "id": "{{id}}", "status": "{{status}}",
          "signedVariantId": {{Str(variantId)}}, "signedPdfUrl": {{Str(pdfUrl)}}, "signedAt": {{Str(signedAt)}}
        }
        """;

    private static string Requests(params string[] items) => $$"""{ "data": [ {{string.Join(",", items)}} ] }""";

    private static string Str(string? value) => value is null ? "null" : $"\"{value}\"";

    private static string Item(string type, string name, string brand, decimal qty, string? price, string? article = null, string? description = null) => $$"""
        {
          "componentId": "c-x", "componentVersionId": "v-x", "componentType": "{{type}}",
          "name": "{{name}}", "brand": "{{brand}}", "quantity": {{qty.ToString(CultureInfo.InvariantCulture)}},
          "articleNumber": {{Str(article)}}, "description": {{Str(description)}}, "visibleToCustomer": true,
          "unitSalesPrice": {{price ?? "null"}},
          "unitPurchasePrice": { "net": 99999, "gross": 99999 }
        }
        """;

    private void SetVariant(string solarItems, string bundles = "[]") =>
        _routes.Json[$"residentialProjects/{ProjectId}/variants/{VariantId}"] = $$"""
            { "id": "{{VariantId}}",
              "systems": { "solar": { "lineItems": [ {{solarItems}} ] } },
              "optionalBundles": {{bundles}} }
            """;

    [Fact]
    public async Task Explicit_signature_id_is_used_even_when_another_signed_one_is_newer()
    {
        _routes.Json[$"residentialProjects/{ProjectId}/signatureRequests"] = Requests(
            Request(Sig1, "signed", VariantId, PdfUrl, "2026-11-01T10:00:00Z"),
            Request(Sig2, "signed", VariantId, PdfUrl, "2026-11-08T10:00:00Z"));

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.Equal(Sig1, order!.SignatureRequestId);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), order.SignedAt);
        Assert.True(order.IsSigned);
    }

    [Fact]
    public async Task Without_id_the_latest_signed_request_wins_and_other_status_is_ignored()
    {
        _routes.Json[$"residentialProjects/{ProjectId}/signatureRequests"] = Requests(
            Request("old", "signed", VariantId, PdfUrl, "2026-11-01T10:00:00Z"),
            Request("newest-but-withdrawn", "withdrawn", VariantId, PdfUrl, "2026-11-09T10:00:00Z"),
            Request("newest-awaiting", "awaitingSignature", null, null, null),
            Request("latest-signed", "signed", VariantId, PdfUrl, "2026-11-05T10:00:00Z"));

        var order = await Source().GetSignedOrderAsync(ProjectId, null, default);

        Assert.Equal("latest-signed", order!.SignatureRequestId);
        Assert.True(order.IsSigned);
    }

    [Fact]
    public async Task Blank_signature_id_counts_as_none()
    {
        var order = await Source().GetSignedOrderAsync(ProjectId, "  ", default);

        Assert.Equal(Sig1, order!.SignatureRequestId);
    }

    [Fact]
    public async Task Without_any_signed_request_the_order_is_not_signed_and_has_no_positions()
    {
        _routes.Json[$"residentialProjects/{ProjectId}/signatureRequests"] = Requests(Request(Sig1, "awaitingSignature", null, null, null));

        var order = await Source().GetSignedOrderAsync(ProjectId, null, default);

        Assert.False(order!.IsSigned);
        Assert.Null(order.SignatureRequestId);
        Assert.Empty(order.Positions);
        Assert.Single(order.Problems);
        Assert.DoesNotContain(_routes.Calls, c => c.Contains("variants"));
        // Die Deal-ID bleibt für Aufgabe/Alarm erhalten.
        Assert.Equal("123456", order.CrmProjectId);
    }

    [Fact]
    public async Task Unknown_explicit_signature_id_is_a_problem()
    {
        var order = await Source().GetSignedOrderAsync(ProjectId, "gibts-nicht", default);

        Assert.False(order!.IsSigned);
        Assert.Equal("gibts-nicht", order.SignatureRequestId);
        Assert.Single(order.Problems);
        Assert.Empty(order.Positions);
    }

    [Theory]
    [InlineData("awaitingSignature")]
    [InlineData("expired")]
    [InlineData("withdrawn")]
    [InlineData("revokedByCustomer")]
    public async Task Explicit_request_that_is_not_signed_gives_not_signed_with_problem_and_no_variant_call(string status)
    {
        _routes.Json[$"residentialProjects/{ProjectId}/signatureRequests"] = Requests(Request(Sig1, status, VariantId, PdfUrl, null));

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.False(order!.IsSigned);
        Assert.Equal(Sig1, order.SignatureRequestId);
        var problem = Assert.Single(order.Problems);
        Assert.Contains(status, problem);
        Assert.Empty(order.Positions);
        Assert.Null(order.VariantId);
        Assert.Null(order.SignedPdf);
        Assert.DoesNotContain(_routes.Calls, c => c.Contains("variants"));
    }

    [Fact]
    public async Task Missing_signed_variant_id_is_a_problem_without_positions()
    {
        _routes.Json[$"residentialProjects/{ProjectId}/signatureRequests"] = Requests(Request(Sig1, "signed", null, PdfUrl, "2026-11-09T10:00:00Z"));

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.True(order!.IsSigned);
        Assert.Null(order.VariantId);
        Assert.Empty(order.Positions);
        Assert.Contains(order.Problems, p => p.Contains("signedVariantId"));
        Assert.DoesNotContain(_routes.Calls, c => c.Contains("variants"));
    }

    [Fact]
    public async Task Variant_404_is_a_problem_without_positions()
    {
        _routes.Json.Remove($"residentialProjects/{ProjectId}/variants/{VariantId}");

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.True(order!.IsSigned);
        Assert.Null(order.VariantId);
        Assert.Empty(order.Positions);
        Assert.Contains(order.Problems, p => p.Contains("Variante nicht gefunden"));
    }

    [Fact]
    public async Task All_systems_and_only_selected_bundles_become_positions_in_order()
    {
        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.Equal(VariantId, order!.VariantId);
        Assert.Equal(
            ["Hersteller A Modul 450", "Hersteller A Modul 450", "Hersteller B WR 8K", "Hersteller G Optimierer", "Hersteller C Speicher 2.56", "Hersteller D Wallbox 11"],
            order.Positions.Select(p => p.DisplayName));
        Assert.Equal([12m, 8m, 1m, 20m, 4m, 1m], order.Positions.Select(p => p.Quantity));
        Assert.DoesNotContain(order.Positions, p => p.Name == "Speicher XL");
    }

    [Fact]
    public async Task Unselected_bundle_is_missing()
    {
        _routes.Json[$"residentialProjects/{ProjectId}/variants/{VariantId}"] = Variant(bundleSelected: false);

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.Equal(5, order!.Positions.Count);
        Assert.DoesNotContain(order.Positions, p => p.Name == "Wallbox 11");
        Assert.Null(order.Summary.Wallbox);
    }

    [Fact]
    public async Task Positions_from_all_five_systems_are_read()
    {
        var s = Item("mounting", "S", "B1", 1, Gross1);
        var b = Item("mounting", "B", "B2", 1, Gross1);
        var e = Item("mounting", "E", "B3", 1, Gross1);
        var h = Item("mounting", "H", "B4", 1, Gross1);
        var a = Item("mounting", "A", "B5", 1, Gross1);
        _routes.Json[$"residentialProjects/{ProjectId}/variants/{VariantId}"] = $$"""
            { "id": "{{VariantId}}", "systems": {
                "solar": { "lineItems": [ {{s}} ] },
                "batteryStorage": { "lineItems": [ {{b}} ] },
                "evCharger": { "lineItems": [ {{e}} ] },
                "heating": { "lineItems": [ {{h}} ] },
                "additional": { "lineItems": [ {{a}} ] } } }
            """;

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.Equal(["B1 S", "B2 B", "B3 E", "B4 H", "B5 A"], order!.Positions.Select(p => p.DisplayName));
    }

    [Fact]
    public async Task Unit_price_is_gross_not_net_and_other_fields_are_taken_over()
    {
        SetVariant(Item("mounting", "Gestell", "Hersteller Z", 3, """{"net": 100, "gross": 119, "vat": 19, "vatRate": 19}""",
            article: " SKU-77 ", description: " Langtext "));

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        var position = Assert.Single(order!.Positions);
        Assert.Equal(119m, position.UnitPriceGross);
        Assert.Equal(19m, position.VatRate);
        Assert.Equal("SKU-77", position.ArticleNumber);
        Assert.Equal("Langtext", position.Description);
        Assert.Equal(3m, position.Quantity);
        Assert.Equal("mounting", position.ComponentType);
        Assert.Empty(order.Problems);
    }

    [Fact]
    public async Task Position_without_price_is_a_problem_naming_the_product_but_keeps_the_position()
    {
        SetVariant($"{Item("mounting", "Gestell", "Hersteller Z", 1, null)},{Item("mounting", "Kabel", "Hersteller Y", 2, """{"net": 5, "gross": null}""")}");

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.Equal(2, order!.Positions.Count);
        Assert.All(order.Positions, p => Assert.Null(p.UnitPriceGross));
        Assert.Equal(2, order.Problems.Count);
        Assert.Contains(order.Problems, p => p.Contains("Hersteller Z Gestell") && p.Contains("ohne Bruttopreis"));
        Assert.Contains(order.Problems, p => p.Contains("Hersteller Y Kabel") && p.Contains("ohne Bruttopreis"));
    }

    [Fact]
    public async Task Blank_article_number_and_description_become_null()
    {
        SetVariant(Item("mounting", "Gestell", "Hersteller Z", 1, Gross1, article: "  ", description: ""));

        var position = Assert.Single((await Source().GetSignedOrderAsync(ProjectId, Sig1, default))!.Positions);

        Assert.Null(position.ArticleNumber);
        Assert.Null(position.Description);
    }

    [Fact]
    public async Task Deal_id_comes_from_the_project_integration_trimmed()
    {
        _routes.Json[$"residentialProjects/{ProjectId}"] = Project().Replace("\"123456\"", "\" 987 \"");

        Assert.Equal("987", (await Source().GetSignedOrderAsync(ProjectId, Sig1, default))!.CrmProjectId);
    }

    [Fact]
    public async Task Project_without_deal_link_gives_no_crm_project_id()
    {
        var json = Project().Replace("\"integrations\": { \"hubspotDealId\": \"123456\" }", "\"integrations\": {}");
        Assert.DoesNotContain("123456", json);
        _routes.Json[$"residentialProjects/{ProjectId}"] = json;

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.Null(order!.CrmProjectId);
        Assert.True(order.IsSigned);
    }

    [Fact]
    public async Task Signed_pdf_is_an_auftrag_document_with_the_signed_url()
    {
        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        var pdf = order!.SignedPdf!;
        Assert.Equal(DocumentKind.Auftrag, pdf.Kind);
        Assert.Equal(PdfUrl, pdf.Reference);
        Assert.Equal(ReonicSignedOrderSource.SignedPdfFileName, pdf.FileName);
    }

    [Fact]
    public async Task Missing_signed_pdf_url_is_a_problem_but_positions_are_still_read()
    {
        _routes.Json[$"residentialProjects/{ProjectId}/signatureRequests"] = Requests(Request(Sig1, "signed", VariantId, null, "2026-11-09T10:00:00Z"));

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.Null(order!.SignedPdf);
        Assert.Contains(order.Problems, p => p.Contains("signedPdfUrl"));
        Assert.Equal(6, order.Positions.Count);
    }

    [Fact]
    public async Task Catalog_problems_end_up_in_problems()
    {
        _routes.Json.Remove("components/comp-wallbox/versions/ver-wallbox");

        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.Contains(order!.Problems, p => p.Contains("Reonic-Katalog") && p.Contains("Hersteller D Wallbox 11"));
        // Die Position selbst bleibt als Artikel erhalten.
        Assert.Contains(order.Positions, p => p.Name == "Wallbox 11");
    }

    [Fact]
    public async Task Catalog_values_flow_into_the_summary()
    {
        var order = await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.Empty(order!.Problems);
        Assert.Equal(new ModulPosition("Hersteller A", "Modul 450", 450m, 20m), Assert.Single(order.Summary.Module));
        Assert.Equal(9m, order.Summary.LeistungKwp);
        Assert.Equal(8m, order.Summary.WechselrichterLeistungKw);
        Assert.Equal(10.24m, order.Summary.Speicher!.KapazitaetKwh);
        Assert.Equal(11m, order.Summary.Wallbox!.LeistungKw);
    }

    [Fact]
    public async Task Unknown_project_gives_null_without_further_calls()
    {
        var order = await Source().GetSignedOrderAsync("gibts-nicht", null, default);

        Assert.Null(order);
        Assert.Equal(["residentialProjects/gibts-nicht"], _routes.Calls);
    }

    [Fact]
    public async Task Server_error_is_not_swallowed()
    {
        var failing = new HttpClient(new ConstantHandler(HttpStatusCode.InternalServerError)) { BaseAddress = new Uri("https://api.reonic.test/rest/v3/") };
        var source = new ReonicSignedOrderSource(new ReonicApiClient(failing), new ReonicFileDownloader(failing), _log);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetSignedOrderAsync(ProjectId, null, default));
    }

    [Fact]
    public async Task Logs_contain_no_names_prices_descriptions_urls_or_file_names()
    {
        SetVariant(Item("mounting", "Geheimname-4711", "Geheimmarke", 1, """{"net": 8888.11, "gross": 9999.22}""",
            article: "ART-GEHEIM", description: "Geheimtext"));
        _routes.Json[PdfUrl] = "%PDF";

        var source = Source();
        var order = await source.GetSignedOrderAsync(ProjectId, Sig1, default);
        var pdf = await source.DownloadAsync(order!.SignedPdf!, default);

        Assert.NotNull(pdf);
        Assert.NotEmpty(_log.Messages);
        foreach (var secret in new[] { "Geheimname", "Geheimmarke", "8888", "9999", "Geheimtext", "ART-GEHEIM", "geheim-token", "files.example", "Auftrag unterschrieben" })
            Assert.DoesNotContain(secret, _log.All);
        Assert.Contains(Sig1, _log.All);
    }

    [Fact]
    public async Task Failed_download_log_names_no_url()
    {
        var source = Source();
        var order = await source.GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.Null(await source.DownloadAsync(order!.SignedPdf!, default));

        Assert.DoesNotContain("geheim-token", _log.All);
        Assert.DoesNotContain("files.example", _log.All);
    }

    [Fact]
    public async Task Missing_price_log_names_no_product()
    {
        SetVariant(Item("mounting", "Geheimname-4711", "Geheimmarke", 1, null));

        await Source().GetSignedOrderAsync(ProjectId, Sig1, default);

        Assert.DoesNotContain("Geheimname", _log.All);
        Assert.DoesNotContain("Geheimmarke", _log.All);
    }

    [Fact]
    public void Signed_pdf_url_is_masked_when_the_signature_request_is_destructured_for_logging()
    {
        var events = new List<LogEvent>();
        using var logger = new LoggerConfiguration().Destructure.With<PersonalDataDestructuringPolicy>()
            .WriteTo.Sink(new ListSink(events)).CreateLogger();

        logger.Information("Signatur {@Request}", new ReonicSignatureRequest(Sig1, "signed", VariantId, PdfUrl, null));

        var rendered = events.Single().Properties["Request"].ToString();
        Assert.DoesNotContain("geheim-token", rendered);
        Assert.DoesNotContain("files.example", rendered);
        Assert.Contains(Sig1, rendered);
    }

    private sealed class ListSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }

    private sealed class ConstantHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
