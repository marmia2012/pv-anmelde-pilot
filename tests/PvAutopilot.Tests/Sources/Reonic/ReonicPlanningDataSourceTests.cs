using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Sources.Reonic;
using PvAutopilot.Sources.Reonic.Api;
using static PvAutopilot.Tests.Sources.Reonic.ReonicFixtures;

namespace PvAutopilot.Tests.Sources.Reonic;

public class ReonicPlanningDataSourceTests
{
    private const string BaseUrl = "https://api.reonic.test/rest/v3/";

    private readonly RoutingHandler _handler = new();

    public ReonicPlanningDataSourceTests()
    {
        _handler.Routes[$"residentialProjects/{ProjectId}"] = Ok(Project());
        _handler.Routes[$"residentialProjects/{ProjectId}/variants/{VariantId}"] = Ok(Variant());
        _handler.Routes[$"checklists/{ProjectId}"] = Ok(Checklist());
        foreach (var (path, json) in Components)
            _handler.Routes[path] = Ok(json);
    }

    private ReonicPlanningDataSource Source(string stage = "offer") =>
        new(new ReonicApiClient(new HttpClient(_handler) { BaseAddress = new Uri(BaseUrl) }),
            Options.Create(new ReonicOptions { ChecklistStage = stage }),
            NullLogger<ReonicPlanningDataSource>.Instance);

    [Fact]
    public async Task Happy_path_maps_project_variant_catalog_and_checklist()
    {
        var data = await Source().GetPlanningDataAsync(ProjectId, default);

        Assert.Empty(data.Problems);
        Assert.Equal("1ESY0000000099", data.Hauptzaehlernummer);
        Assert.Equal(Installationsart.Neuerrichtung, data.Installationsart);
        Assert.False(data.Kaskadenschaltung);
        Assert.Equal("Überschusseinspeisung – Zweirichtungszähler", data.Messkonzept);

        Assert.Equal(new ModulPosition("Hersteller A", "Modul 450", 450m, 20m), Assert.Single(data.Module));
        var wr = Assert.Single(data.Wechselrichter);
        Assert.Equal((8000m, 1m, 3), (wr.LeistungW, wr.Anzahl, wr.Phasen));
        Assert.Equal(new Batteriespeicher("Hersteller C", "Speicher 2.56", 10.24m, null), data.Speicher);
        Assert.Equal(new Wallbox("Hersteller D", "Wallbox 11", 11m), data.Wallbox);

        var zaehler = Assert.Single(data.Zaehler);
        Assert.Equal(ZaehlerwechselTyp.TauschZweirichtungszaehler, zaehler.Wechseltyp);
        Assert.Equal(Zaehlerbauform.DreiPunkt, zaehler.Bauform);

        Assert.Equal(4, data.Dokumente.Count(d => d.Kind == DocumentKind.Datenblatt));
        Assert.Contains(data.Dokumente, d => d.Kind == DocumentKind.Schaltplan && d.Reference == "https://files.example/schaltplan.pdf");
        Assert.Contains(data.Dokumente, d => d.Kind == DocumentKind.FotoHauptzaehler);
        Assert.Contains(data.Dokumente, d => d.Kind == DocumentKind.FotoZaehlerschrank);
        Assert.Contains(data.Dokumente, d => d.Kind == DocumentKind.FotoHausanschlusskasten);
    }

    [Fact]
    public async Task Each_component_version_is_loaded_once_and_unselected_bundles_are_skipped()
    {
        await Source().GetPlanningDataAsync(ProjectId, default);

        Assert.Equal(1, _handler.Calls.Count(c => c == "components/comp-module/versions/ver-module"));
        Assert.DoesNotContain(_handler.Calls, c => c.Contains("comp-battery-2"));
    }

    [Fact]
    public async Task Unselected_wallbox_bundle_is_not_part_of_the_plant()
    {
        _handler.Routes[$"residentialProjects/{ProjectId}/variants/{VariantId}"] = Ok(Variant(bundleSelected: false));

        var data = await Source().GetPlanningDataAsync(ProjectId, default);

        Assert.Null(data.Wallbox);
        Assert.Empty(data.Problems);
    }

    [Theory]
    [InlineData("offer")]
    [InlineData("installation")]
    public async Task Checklist_is_requested_for_configured_stage(string stage)
    {
        await Source(stage).GetPlanningDataAsync(ProjectId, default);

        Assert.Contains(_handler.Requests, r => r.AbsolutePath.EndsWith($"/checklists/{ProjectId}") && r.Query == $"?stage={stage}");
    }

    [Fact]
    public async Task Unknown_project_is_a_problem_not_an_exception()
    {
        _handler.Routes.Remove($"residentialProjects/{ProjectId}");

        var data = await Source().GetPlanningDataAsync(ProjectId, default);

        Assert.Contains(data.Problems, p => p.Contains("Projekt nicht gefunden"));
        Assert.Single(_handler.Calls);
    }

    [Fact]
    public async Task Missing_primary_variant_is_reported_and_checklist_still_read()
    {
        _handler.Routes[$"residentialProjects/{ProjectId}"] = Ok(Project(primaryVariantId: null));

        var data = await Source().GetPlanningDataAsync(ProjectId, default);

        Assert.Contains(data.Problems, p => p.Contains("keine Hauptvariante"));
        Assert.Empty(data.Module);
        Assert.Single(data.Zaehler);
    }

    [Fact]
    public async Task Missing_variant_is_reported()
    {
        _handler.Routes.Remove($"residentialProjects/{ProjectId}/variants/{VariantId}");

        var data = await Source().GetPlanningDataAsync(ProjectId, default);

        Assert.Contains(data.Problems, p => p.Contains("Hauptvariante nicht gefunden"));
    }

    [Fact]
    public async Task Missing_component_version_is_reported()
    {
        _handler.Routes.Remove("components/comp-inverter/versions/ver-inverter");

        var data = await Source().GetPlanningDataAsync(ProjectId, default);

        Assert.Contains(data.Problems, p => p.Contains("WR 8K") && p.Contains("nicht gefunden"));
        Assert.Empty(data.Wechselrichter);
    }

    [Fact]
    public async Task Missing_checklist_is_reported_with_stage()
    {
        _handler.Routes.Remove($"checklists/{ProjectId}");

        var data = await Source().GetPlanningDataAsync(ProjectId, default);

        Assert.Contains(data.Problems, p => p.Contains("keine Checkliste") && p.Contains("offer"));
        Assert.Empty(data.Zaehler);
        Assert.Null(data.Installationsart);
        Assert.Single(data.Module); // Komponenten trotzdem gelesen
    }

    [Fact]
    public async Task Missing_meter_number_stays_null()
    {
        _handler.Routes[$"residentialProjects/{ProjectId}"] = Ok(Project(meterNumber: "   "));

        var data = await Source().GetPlanningDataAsync(ProjectId, default);

        Assert.Null(data.Hauptzaehlernummer);
    }

    [Fact]
    public async Task Checklist_gaps_are_passed_through()
    {
        _handler.Routes[$"checklists/{ProjectId}"] = Ok(Checklist(zaehlerAnzahl: "2"));

        var data = await Source().GetPlanningDataAsync(ProjectId, default);

        Assert.Contains(data.Problems, p => p.Contains("Zähler 2:"));
    }

    [Fact]
    public async Task Server_error_throws_without_body_content()
    {
        _handler.Routes[$"residentialProjects/{ProjectId}"] = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{ "error": "Kunde Erika Mustermann, Musterstraße 1" }""", Encoding.UTF8, "application/json"),
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Source().GetPlanningDataAsync(ProjectId, default));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains("500", ex.Message);
        Assert.DoesNotContain("Mustermann", ex.Message);
        Assert.DoesNotContain("Musterstraße", ex.Message);
    }

    [Fact]
    public async Task Ids_are_url_escaped()
    {
        _handler.Routes.Clear();

        await Source().GetPlanningDataAsync("a/b c", default);

        Assert.Equal("/rest/v3/residentialProjects/a%2Fb%20c", Assert.Single(_handler.Requests).AbsolutePath);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Ok(string json) =>
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Antwortet je Pfad (relativ zur Basis-URL, ohne Query); unbekannte Pfade → 404.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> Routes { get; } = [];
        public List<string> Calls { get; } = [];
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            var path = Uri.UnescapeDataString(uri.AbsolutePath)["/rest/v3/".Length..];
            Calls.Add(path);
            return Task.FromResult(Routes.TryGetValue(path, out var respond)
                ? respond(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
