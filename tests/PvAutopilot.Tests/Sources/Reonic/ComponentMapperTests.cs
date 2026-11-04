using PvAutopilot.Core.DataCard;
using PvAutopilot.Sources.Reonic.Api;
using PvAutopilot.Sources.Reonic.Mapping;

namespace PvAutopilot.Tests.Sources.Reonic;

public class ComponentMapperTests
{
    private static PlannedComponent C(
        string type, string brand, string name, decimal quantity, ReonicComponentAttributes? attributes,
        string? datasheet = "default", string? unit = "item", string id = "c1", string version = "v1")
    {
        datasheet = datasheet == "default" ? $"https://files.example/{brand}-{name}.pdf" : datasheet;
        return new PlannedComponent(
            new ReonicLineItem(id, version, type, name, brand, quantity),
            new ReonicComponent(id, version, name, brand, type, unit, datasheet, attributes));
    }

    private static ReonicComponentAttributes Attr(
        decimal? wp = null, decimal? w = null, int? phases = null, decimal? wh = null, decimal? charging = null) =>
        new(wp, w, phases, wh, charging);

    [Fact]
    public void Typical_plant_is_mapped_with_units()
    {
        var data = ComponentMapper.Map(
        [
            C("module", "Hersteller A", "Modul 450", 20, Attr(wp: 450)),
            C("inverter", "Hersteller B", "WR 8K", 1, Attr(w: 8000, phases: 3)),
            C("batteryStorage", "Hersteller C", "Speicher 5", 2, Attr(wh: 5120)),
            C("evCharger", "Hersteller D", "Wallbox 11", 1, Attr(charging: 11000)),
        ]);

        Assert.Empty(data.Problems);
        Assert.Equal(new ModulPosition("Hersteller A", "Modul 450", 450m, 20m), Assert.Single(data.Module));
        var wr = Assert.Single(data.Wechselrichter);
        Assert.Equal(8000m, wr.LeistungW);
        Assert.Equal(1m, wr.Anzahl);
        Assert.Equal(3, wr.Phasen);
        Assert.Null(wr.ScheinleistungKva);
        Assert.Equal(new Batteriespeicher("Hersteller C", "Speicher 5", 10.24m, null), data.Speicher);
        Assert.Equal(new Wallbox("Hersteller D", "Wallbox 11", 11m), data.Wallbox);
        Assert.Equal(4, data.Datenblaetter.Count);
        Assert.All(data.Datenblaetter, d =>
        {
            Assert.Equal(DocumentKind.Datenblatt, d.Kind);
            Assert.Equal(ComponentMapper.DocumentSource, d.Source);
        });
    }

    [Fact]
    public void All_in_one_system_yields_inverter_and_storage()
    {
        var data = ComponentMapper.Map([C("allInOneEnergyStorageSystem", "Hersteller E", "AIO 10", 1, Attr(w: 10000, phases: 3, wh: 10000))]);

        Assert.Empty(data.Problems);
        Assert.Equal(10000m, Assert.Single(data.Wechselrichter).LeistungW);
        Assert.Equal(10m, data.Speicher!.KapazitaetKwh);
        Assert.Single(data.Datenblaetter);
    }

    [Fact]
    public void Microinverters_count_as_inverters()
    {
        var data = ComponentMapper.Map([C("microinverter", "Hersteller F", "Micro 800", 4, Attr(w: 800, phases: 1))]);

        var wr = Assert.Single(data.Wechselrichter);
        Assert.Equal(4m, wr.Anzahl);
        Assert.Equal(3.2m, data.Wechselrichter.Sum(w => w.LeistungW * w.Anzahl) / 1000m);
    }

    [Fact]
    public void Irrelevant_component_types_are_ignored()
    {
        var data = ComponentMapper.Map(
        [
            C("optimizer", "Hersteller G", "Optimierer", 20, null, datasheet: null),
            C("accessoryToModule", "Hersteller H", "Klemmen", 40, null, datasheet: null),
            C("heatPump", "Hersteller I", "WP", 1, null, datasheet: null),
        ]);

        Assert.Empty(data.Problems);
        Assert.Empty(data.Module);
        Assert.Empty(data.Wechselrichter);
        Assert.Null(data.Speicher);
        Assert.Null(data.Wallbox);
        Assert.Empty(data.Datenblaetter);
    }

    [Fact]
    public void Unit_of_irrelevant_components_is_not_checked()
    {
        var data = ComponentMapper.Map(
        [
            C("moduleFrameConstruction", "Hersteller J", "Schienen", 24, null, datasheet: null, unit: "meter"),
            C("installationFee", "reinsolar", "Montage", 1, null, datasheet: null, unit: "flatRate"),
        ]);

        Assert.Empty(data.Problems);
    }

    [Fact]
    public void Wallbox_quantity_is_kept_and_same_model_merged()
    {
        var data = ComponentMapper.Map(
        [
            C("evCharger", "Hersteller D", "Wallbox 11", 2, Attr(charging: 11000)),
            C("evCharger", "Hersteller D", "Wallbox 11", 1, Attr(charging: 11000), id: "c2", version: "v2"),
        ]);

        Assert.Empty(data.Problems);
        Assert.Equal(new Wallbox("Hersteller D", "Wallbox 11", 11m, 3m), data.Wallbox);
    }

    [Fact]
    public void Datasheet_file_name_is_sanitized()
    {
        var data = ComponentMapper.Map([C("batteryStorage", "Hersteller C", "HVS 7.7 / 3 Module", 1, Attr(wh: 7680))]);

        var datenblatt = Assert.Single(data.Datenblaetter);
        Assert.Equal("Datenblatt Hersteller C HVS 7.7 _ 3 Module.pdf", datenblatt.FileName);
    }

    [Fact]
    public void Same_components_are_merged()
    {
        var data = ComponentMapper.Map(
        [
            C("module", "Hersteller A", "Modul 450", 12, Attr(wp: 450)),
            C("module", "Hersteller A", "Modul 450", 8, Attr(wp: 450)),
            C("inverter", "Hersteller B", "WR 5K", 1, Attr(w: 5000)),
            C("inverter", "Hersteller B", "WR 5K", 1, Attr(w: 5000)),
        ]);

        Assert.Equal(20m, Assert.Single(data.Module).Anzahl);
        Assert.Equal(2m, Assert.Single(data.Wechselrichter).Anzahl);
        Assert.Equal(2, data.Datenblaetter.Count); // je Komponente nur einmal
    }

    [Fact]
    public void Different_modules_stay_separate()
    {
        var data = ComponentMapper.Map(
        [
            C("module", "Hersteller A", "Modul 450", 12, Attr(wp: 450), id: "m1"),
            C("module", "Hersteller A", "Modul 410", 8, Attr(wp: 410), id: "m2"),
        ]);

        Assert.Equal(2, data.Module.Count);
    }

    [Fact]
    public void Modular_storage_multiplies_capacity_and_merges_positions()
    {
        var data = ComponentMapper.Map(
        [
            C("batteryStorage", "Hersteller C", "HV-Modul 2.56", 3, Attr(wh: 2560)),
            C("batteryStorage", "Hersteller C", "HV-Modul 2.56", 1, Attr(wh: 2560)),
        ]);

        Assert.Empty(data.Problems);
        Assert.Equal(10.24m, data.Speicher!.KapazitaetKwh);
    }

    [Fact]
    public void Missing_technical_values_are_reported()
    {
        var data = ComponentMapper.Map(
        [
            C("module", "Hersteller A", "Modul ohne Wp", 10, Attr()),
            C("inverter", "Hersteller B", "WR ohne Leistung", 1, Attr(w: 0)),
            C("batteryStorage", "Hersteller C", "Speicher ohne Kapazität", 1, null),
            C("evCharger", "Hersteller D", "Wallbox ohne Leistung", 1, Attr()),
        ]);

        Assert.Equal(4, data.Problems.Count);
        Assert.Contains(data.Problems, p => p.Contains("ohne Leistung (Wp)"));
        Assert.Contains(data.Problems, p => p.Contains("ohne AC-Nennleistung"));
        Assert.Contains(data.Problems, p => p.Contains("ohne Kapazität"));
        Assert.Contains(data.Problems, p => p.Contains("ohne Ladeleistung"));
        Assert.Empty(data.Module);
        Assert.Empty(data.Wechselrichter);
        Assert.Null(data.Speicher);
        Assert.Null(data.Wallbox);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Missing_datasheet_is_reported(string? datasheet)
    {
        var data = ComponentMapper.Map([C("inverter", "Hersteller B", "WR 8K", 1, Attr(w: 8000), datasheet: datasheet)]);

        Assert.Contains(data.Problems, p => p.Contains("ohne Datenblatt"));
        Assert.Empty(data.Datenblaetter);
        Assert.Single(data.Wechselrichter);
    }

    [Fact]
    public void Quantity_unit_other_than_item_is_reported()
    {
        var data = ComponentMapper.Map([C("module", "Hersteller A", "Modul 450", 9, Attr(wp: 450), unit: "kWp")]);

        Assert.Contains(data.Problems, p => p.Contains("Mengeneinheit") && p.Contains("kWp"));
    }

    [Fact]
    public void Null_quantity_unit_is_accepted()
    {
        var data = ComponentMapper.Map([C("module", "Hersteller A", "Modul 450", 9, Attr(wp: 450), unit: null)]);

        Assert.Empty(data.Problems);
    }

    [Fact]
    public void Several_storages_or_wallboxes_are_reported()
    {
        var data = ComponentMapper.Map(
        [
            C("batteryStorage", "Hersteller C", "Speicher A", 1, Attr(wh: 5000), id: "s1"),
            C("batteryStorage", "Hersteller C", "Speicher B", 1, Attr(wh: 7000), id: "s2"),
            C("evCharger", "Hersteller D", "Wallbox A", 1, Attr(charging: 11000), id: "w1"),
            C("evCharger", "Hersteller D", "Wallbox B", 1, Attr(charging: 22000), id: "w2"),
        ]);

        Assert.Contains(data.Problems, p => p.Contains("mehrere verschiedene Speicher"));
        Assert.Contains(data.Problems, p => p.Contains("mehrere verschiedene Wallboxen"));
        Assert.Equal("Speicher A", data.Speicher!.Modell);
        Assert.Equal("Wallbox A", data.Wallbox!.Modell);
    }

    [Fact]
    public void Shared_datasheet_url_is_listed_once()
    {
        var data = ComponentMapper.Map(
        [
            C("inverter", "Hersteller B", "WR 5K", 1, Attr(w: 5000), datasheet: "https://files.example/serie.pdf", id: "a"),
            C("inverter", "Hersteller B", "WR 8K", 1, Attr(w: 8000), datasheet: "https://files.example/serie.pdf", id: "b"),
        ]);

        Assert.Equal("https://files.example/serie.pdf", Assert.Single(data.Datenblaetter).Reference);
        Assert.Equal(2, data.Wechselrichter.Count);
    }

    [Fact]
    public void Empty_variant_yields_empty_data()
    {
        var data = ComponentMapper.Map([]);

        Assert.Empty(data.Problems);
        Assert.Empty(data.Module);
    }
}
