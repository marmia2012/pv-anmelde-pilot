using Microsoft.Extensions.Options;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Sources;

namespace PvAutopilot.Tests.Core;

public class NabDataCardAggregatorTests
{
    private CrmProjectData _crm = new()
    {
        PlanningProjectId = "reonic-1",
        Anlagenbetreiber = TestData.Card().Anlagenbetreiber,
        Anlagenadresse = TestData.Card().Anlagenadresse,
        Errichter = TestData.Card().Errichter,
    };

    private PlanningData _plan = new()
    {
        Installationsart = Installationsart.Neuerrichtung,
        Hauptzaehlernummer = "1ESY0000000001",
        Module = [new ModulPosition("Hersteller A", "Modul 450", 450, 20)],
        Wechselrichter = [new WechselrichterPosition("Hersteller B", "WR 8K", null, 8000, 1, null, 3)],
        Messkonzept = "Überschusseinspeisung – Zweirichtungszähler",
        MesskonzeptBemerkung = "Hinweis",
    };

    private Task<DataCardAggregation> Aggregate() =>
        new NabDataCardAggregator(new Crm(_crm), new Planning(_plan), new NoDocuments(), Options.Create(new ErrichterOptions())).AggregateAsync("crm-1", default);

    [Fact]
    public async Task Complete_sources_give_complete_card()
    {
        var result = await Aggregate();

        Assert.True(result.IsComplete);
        Assert.Equal("Hinweis", result.Card!.MesskonzeptBemerkung);
        Assert.False(result.Card.BestandsanlageVorhanden);
    }

    [Fact]
    public async Task Planning_problems_are_passed_through()
    {
        _plan = _plan with { Problems = ["Checkliste: Kaskadenschaltung fehlt", "Reonic: keine Hauptvariante markiert"] };

        var result = await Aggregate();

        Assert.False(result.IsComplete);
        Assert.Contains("Checkliste: Kaskadenschaltung fehlt", result.MissingFields);
        Assert.Contains("Reonic: keine Hauptvariante markiert", result.MissingFields);
        Assert.NotNull(result.Card);
    }

    [Theory]
    [InlineData(true, Installationsart.Neuerrichtung, true)]
    [InlineData(false, Installationsart.Erweiterung, true)]
    [InlineData(true, Installationsart.Erweiterung, false)]
    [InlineData(false, Installationsart.Neuerrichtung, false)]
    public async Task Contradiction_between_hubspot_and_reonic_is_reported(bool bestandHubSpot, Installationsart reonic, bool expected)
    {
        _crm = _crm with { BestandsanlageVorhanden = bestandHubSpot };
        _plan = _plan with { Installationsart = reonic };

        var result = await Aggregate();

        Assert.Equal(expected, result.MissingFields.Any(m => m.StartsWith("Widerspruch")));
        Assert.Equal(reonic, result.Card!.Installationsart);
    }

    [Fact]
    public async Task No_contradiction_check_without_hubspot_value()
    {
        _crm = _crm with { BestandsanlageVorhanden = null };
        _plan = _plan with { Installationsart = Installationsart.Erweiterung };

        var result = await Aggregate();

        Assert.True(result.IsComplete);
        Assert.True(result.Card!.BestandsanlageVorhanden);
    }

    [Fact]
    public async Task Missing_installationsart_defaults_to_neuerrichtung()
    {
        _crm = _crm with { BestandsanlageVorhanden = true };
        _plan = _plan with { Installationsart = null };

        var result = await Aggregate();

        Assert.Equal(Installationsart.Neuerrichtung, result.Card!.Installationsart);
        Assert.DoesNotContain(result.MissingFields, m => m.StartsWith("Widerspruch"));
    }

    [Fact]
    public void Zaehler_abmelden_only_for_ausbau()
    {
        foreach (var typ in Enum.GetValues<ZaehlerwechselTyp>())
            Assert.Equal(typ == ZaehlerwechselTyp.Ausbau, new Zaehler("1", typ, null, null, null, null).Abmelden);
    }

    [Fact]
    public void Bestandsanlage_follows_installationsart()
    {
        Assert.True((TestData.Card() with { Installationsart = Installationsart.Erweiterung }).BestandsanlageVorhanden);
        Assert.False(TestData.Card().BestandsanlageVorhanden);
    }

    private sealed class Crm(CrmProjectData data) : ICrmProjectSource
    {
        public Task<CrmProjectData> GetProjectAsync(string crmProjectId, CancellationToken ct) => Task.FromResult(data);
    }

    private sealed class Planning(PlanningData data) : IPlanningDataSource
    {
        public Task<PlanningData> GetPlanningDataAsync(string planningProjectId, CancellationToken ct) => Task.FromResult(data);
    }

    private sealed class NoDocuments : IProductDocumentSource
    {
        public Task<IReadOnlyList<DocumentRef>> GetProductDocumentsAsync(string hersteller, string modell, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentRef>>([]);
    }
}
