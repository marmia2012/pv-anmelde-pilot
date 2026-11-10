using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PvAutopilot.Sources.Reonic;
using PvAutopilot.Sources.Reonic.Api;
using PvAutopilot.Sources.Reonic.Mapping;
using static PvAutopilot.Tests.Sources.Reonic.ReonicFixtures;

namespace PvAutopilot.Tests.Sources.Reonic;

public class ComponentProblemDetailsTests
{
    private static PlannedComponent C(string type, string brand, string name, decimal qty, ReonicComponentAttributes? attributes,
        string? datasheet = "https://files.example/d.pdf", string unit = "item") =>
        new(new ReonicLineItem($"c-{name}", "v1", type, name, brand, qty),
            new ReonicComponent($"c-{name}", "v1", name, brand, type, unit, datasheet, attributes));

    private static ReonicComponentAttributes Attr(decimal? wp = null, decimal? w = null, decimal? wh = null, decimal? charging = null) =>
        new(wp, w, null, wh, charging);

    [Fact]
    public void Every_category_is_assigned_and_texts_and_order_equal_the_plain_problems()
    {
        var data = ComponentMapper.Map(
        [
            C("module", "A", "Modul", 10, Attr(), datasheet: null),               // CatalogValue, MissingDatasheet
            C("batteryStorage", "B", "Speicher 1", 1, Attr(wh: 5000), unit: "kg"), // QuantityUnit
            C("batteryStorage", "B", "Speicher 2", 1, Attr(wh: 6000)),
            C("evCharger", "D", "Wallbox 11", 1, Attr(charging: 11000)),
            C("evCharger", "D", "Wallbox 22", 1, Attr(charging: 22000)),
        ]);

        Assert.Equal(
        [
            (ComponentProblemKind.CatalogValue, "Reonic-Katalog: module „A Modul“ ohne Leistung (Wp)"),
            (ComponentProblemKind.MissingDatasheet, "Reonic-Katalog: module „A Modul“ ohne Datenblatt"),
            (ComponentProblemKind.QuantityUnit, "Reonic-Planung: batteryStorage „B Speicher 1“ hat Mengeneinheit „kg“ statt Stück"),
            (ComponentProblemKind.NotYetSupportedForNetzanmeldung, "Reonic-Planung: mehrere verschiedene Speicher – wird noch nicht automatisch angemeldet"),
            (ComponentProblemKind.NotYetSupportedForNetzanmeldung, "Reonic-Planung: mehrere verschiedene Wallboxen – wird noch nicht automatisch angemeldet"),
        ], data.ProblemDetails.Select(p => (p.Kind, p.Text)));
        Assert.Equal(data.Problems, data.ProblemDetails.Select(p => p.Text));
    }

    [Theory]
    [InlineData("module", "ohne Leistung (Wp)")]
    [InlineData("inverter", "ohne AC-Nennleistung")]
    [InlineData("microinverter", "ohne AC-Nennleistung")]
    [InlineData("batteryStorage", "ohne Kapazität")]
    [InlineData("evCharger", "ohne Ladeleistung")]
    public void Missing_technical_catalog_values_are_catalog_value_problems(string type, string text)
    {
        var data = ComponentMapper.Map([C(type, "A", "X", 1, Attr())]);

        var problem = Assert.Single(data.ProblemDetails);
        Assert.Equal(ComponentProblemKind.CatalogValue, problem.Kind);
        Assert.Contains(text, problem.Text);
        Assert.Equal([problem.Text], data.Problems);
    }

    [Fact]
    public void All_in_one_system_without_values_reports_two_catalog_value_problems()
    {
        var data = ComponentMapper.Map([C("allInOneEnergyStorageSystem", "A", "X", 1, Attr())]);

        Assert.Equal(2, data.ProblemDetails.Count);
        Assert.All(data.ProblemDetails, p => Assert.Equal(ComponentProblemKind.CatalogValue, p.Kind));
    }

    [Fact]
    public void Plant_without_problems_has_no_details() =>
        Assert.Empty(ComponentMapper.Map([C("module", "A", "Modul", 10, Attr(wp: 450))]).ProblemDetails);

    // Verbraucher

    private ReonicRoutes SetUpRoutes(string variantJson, Dictionary<string, string> components)
    {
        var routes = new ReonicRoutes();
        routes.Json[$"residentialProjects/{ProjectId}"] = Project();
        routes.Json[$"residentialProjects/{ProjectId}/variants/{VariantId}"] = variantJson;
        routes.Json[$"residentialProjects/{ProjectId}/signatureRequests"] =
            $$"""{ "data": [ { "id": "sig-1", "status": "signed", "signedVariantId": "{{VariantId}}", "signedPdfUrl": "https://files.example/a.pdf", "signedAt": "2026-11-09T10:00:00Z" } ] }""";
        routes.Json[$"checklists/{ProjectId}"] = Checklist();
        foreach (var (path, json) in components)
            routes.Json[path] = json;
        return routes;
    }

    private static string Item(string id, string type, string name, string brand, int qty) => $$"""
        { "componentId": "{{id}}", "componentVersionId": "v", "componentType": "{{type}}", "name": "{{name}}", "brand": "{{brand}}",
          "quantity": {{qty}}, "unitSalesPrice": { "net": 1, "gross": 1 } }
        """;

    private static string Comp(string id, string type, string name, string brand, string attributes, string? datasheet, string unit = "item") =>
        Component(id, "v", type, name, brand, attributes)
            .Replace("\"quantityUnit\": \"item\"", $"\"quantityUnit\": \"{unit}\"")
            .Replace($"\"datasheetUrl\": \"https://files.example/{id}.pdf\"", $"\"datasheetUrl\": {(datasheet is null ? "null" : $"\"{datasheet}\"")}");

    /// <summary>Modul ohne Datenblatt, drei Speicher (einer ohne Kapazität), Wallbox mit Mengeneinheit „kg“.</summary>
    private ReonicRoutes ProblemRoutes()
    {
        var variant = $$"""
            { "id": "{{VariantId}}", "systems": {
                "solar": { "lineItems": [ {{Item("m", "module", "Modul", "A", 10)}} ] },
                "batteryStorage": { "lineItems": [ {{Item("s1", "batteryStorage", "S1", "B", 1)}}, {{Item("s2", "batteryStorage", "S2", "B", 1)}}, {{Item("s3", "batteryStorage", "S3", "B", 1)}} ] },
                "evCharger": { "lineItems": [ {{Item("w", "evCharger", "W", "D", 1)}} ] } },
              "optionalBundles": [] }
            """;
        return SetUpRoutes(variant, new Dictionary<string, string>
        {
            ["components/m/versions/v"] = Comp("m", "module", "Modul", "A", """{ "wattPeak": 450 }""", datasheet: null),
            ["components/s1/versions/v"] = Comp("s1", "batteryStorage", "S1", "B", """{ "capacity": 5000 }""", "https://files.example/s1.pdf"),
            ["components/s2/versions/v"] = Comp("s2", "batteryStorage", "S2", "B", """{ }""", "https://files.example/s2.pdf"),
            ["components/s3/versions/v"] = Comp("s3", "batteryStorage", "S3", "B", """{ "capacity": 7000 }""", "https://files.example/s3.pdf"),
            ["components/w/versions/v"] = Comp("w", "evCharger", "W", "D", """{ "maxChargingPower": 11000 }""", "https://files.example/w.pdf", unit: "kg"),
        });
    }

    [Fact]
    public async Task Signed_order_drops_datasheet_and_netzanmeldung_limits_but_keeps_catalog_value_and_unit_problems()
    {
        var routes = ProblemRoutes();
        var http = new HttpClient(routes) { BaseAddress = new Uri("https://api.reonic.test/rest/v3/") };
        var source = new ReonicSignedOrderSource(new ReonicApiClient(http), new ReonicFileDownloader(new HttpClient(routes)),
            NullLogger<ReonicSignedOrderSource>.Instance);

        var order = await source.GetSignedOrderAsync(ProjectId, "sig-1", default);

        Assert.Equal(
        [
            "Reonic-Katalog: batteryStorage „B S2“ ohne Kapazität",
            "Reonic-Planung: evCharger „D W“ hat Mengeneinheit „kg“ statt Stück",
        ], order!.Problems);
        Assert.DoesNotContain(order.Problems, p => p.Contains("Datenblatt") || p.Contains("mehrere verschiedene"));
    }

    [Fact]
    public async Task Planning_data_source_still_reports_every_problem()
    {
        var routes = ProblemRoutes();
        var http = new HttpClient(routes) { BaseAddress = new Uri("https://api.reonic.test/rest/v3/") };
        var source = new ReonicPlanningDataSource(new ReonicApiClient(http), Options.Create(new ReonicOptions()),
            NullLogger<ReonicPlanningDataSource>.Instance);

        var data = await source.GetPlanningDataAsync(ProjectId, default);

        Assert.Contains(data.Problems, p => p == "Reonic-Katalog: module „A Modul“ ohne Datenblatt");
        Assert.Contains(data.Problems, p => p == "Reonic-Katalog: batteryStorage „B S2“ ohne Kapazität");
        Assert.Contains(data.Problems, p => p == "Reonic-Planung: evCharger „D W“ hat Mengeneinheit „kg“ statt Stück");
        Assert.Contains(data.Problems, p => p.Contains("mehrere verschiedene Speicher"));
    }
}
