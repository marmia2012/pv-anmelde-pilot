using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PvAutopilot.Sources.HubSpot;

namespace PvAutopilot.Tests.Sources.HubSpot;

public class HubSpotStructureReaderTests
{
    private readonly HubSpotRig _rig = new();

    private HubSpotStructureReader Reader() =>
        new(_rig.Api, HubSpotRig.Wrap(_rig.Options), NullLogger<HubSpotStructureReader>.Instance);

    internal static void MapStructure(HubSpotHandler handler, string objectType = "0-3")
    {
        handler.Get($"crm/v3/pipelines/{objectType}", new
        {
            results = new object[]
            {
                new
                {
                    id = "p2", label = "Zweite", displayOrder = 2, stages = new object[]
                    {
                        new { id = "p2-s1", label = "Neu", displayOrder = 0 },
                    },
                },
                new
                {
                    id = "p1", label = "Anmeldung", displayOrder = 1, stages = new object[]
                    {
                        new { id = "s-vollmacht", label = "Vollmacht zurück", displayOrder = 2 },
                        new { id = "s-neu", label = "Neu", displayOrder = 1 },
                    },
                },
            },
        });
        handler.Get($"crm/v3/properties/{objectType}", new
        {
            results = new object[]
            {
                new
                {
                    name = "einspeiseart", label = "Einspeiseart", type = "enumeration", fieldType = "select", groupName = "pv", options = new object[]
                    {
                        new { value = "voll", label = "Volleinspeisung" },
                        new { value = "teil", label = "Teileinspeisung" },
                    },
                },
                new { name = "amount", label = "Betrag", type = "number", fieldType = "number", groupName = "dealinformation", options = Array.Empty<object>() },
            },
        });
    }

    [Fact]
    public async Task Structure_lists_pipelines_with_ordered_stages_and_properties_with_options()
    {
        HubSpotStructureReaderTests.MapStructure(_rig.Handler);

        var structure = await Reader().ReadAsync(default);

        Assert.Equal("0-3", structure.ObjectTypeId);
        Assert.Equal(["p1", "p2"], structure.Pipelines.Select(p => p.Id));
        var pipeline = structure.Pipelines[0];
        Assert.Equal("Anmeldung", pipeline.Label);
        Assert.Equal([("s-neu", "Neu"), ("s-vollmacht", "Vollmacht zurück")], pipeline.Stages.Select(s => (s.Id, s.Label)));

        var einspeiseart = Assert.Single(structure.Properties, p => p.Name == "einspeiseart");
        Assert.Equal(("Einspeiseart", "enumeration", "select", "pv"), (einspeiseart.Label, einspeiseart.Type, einspeiseart.FieldType, einspeiseart.GroupName));
        Assert.Equal([("voll", "Volleinspeisung"), ("teil", "Teileinspeisung")], einspeiseart.Options.Select(o => (o.Value, o.Label)));
        Assert.Empty(Assert.Single(structure.Properties, p => p.Name == "amount").Options);
    }

    [Fact]
    public async Task Structure_is_read_only_and_reads_no_records()
    {
        HubSpotStructureReaderTests.MapStructure(_rig.Handler);

        await Reader().ReadAsync(default);

        Assert.Empty(_rig.Handler.Writes);
        Assert.All(_rig.Handler.Requests, r => Assert.StartsWith("crm/v3/", r.Path));
        Assert.DoesNotContain(_rig.Handler.Requests, r => r.Path.Contains("/objects/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Structure_uses_the_configured_object_type()
    {
        _rig.Options.ObjectTypeId = "2-77";
        HubSpotStructureReaderTests.MapStructure(_rig.Handler, "2-77");

        var structure = await Reader().ReadAsync(default);

        Assert.Equal("2-77", structure.ObjectTypeId);
        Assert.Equal(2, structure.Pipelines.Count);
    }

    [Fact]
    public async Task Wrong_object_type_is_a_technical_error_not_an_empty_result()
    {
        // 404 ist hier ein Konfigurationsfehler (falsche ObjectTypeId).
        _rig.Handler.Get("crm/v3/pipelines/0-3", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<HttpRequestException>(() => Reader().ReadAsync(default));
    }
}
