using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PvAutopilot.Netzbetreiber.NetzeDuisburg;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Channels;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Submission;

namespace PvAutopilot.Tests.Netzbetreiber;

public class NetzeDuisburgAdapterTests
{
    private static NetzeDuisburgAdapter Adapter(NetzeDuisburgChannelKind channel = NetzeDuisburgChannelKind.Mail) =>
        new(
            Options.Create(new NetzeDuisburgOptions { Channel = channel }), new NetzeDuisburgMailChannel(), new NetzeDuisburgPortalChannel(),
            new NetzeDuisburgFormGenerator(NullLogger<NetzeDuisburgFormGenerator>.Instance), TimeProvider.System,
            NullLogger<NetzeDuisburgAdapter>.Instance);

    [Fact]
    public void Small_complete_plant_is_valid()
    {
        var card = TestData.Card(modulAnzahl: 20);
        card = card with { Dokumente = [.. card.Dokumente, new DocumentRef(DocumentKind.Schaltplan, "test", "2", "schaltplan.pdf")] };

        Assert.Empty(Adapter().Validate(SubmissionKind.Netzanmeldung, card));
    }

    [Fact]
    public void Plant_of_25_kw_or_more_is_not_automated()
    {
        var problems = Adapter().Validate(SubmissionKind.Netzanmeldung, TestData.Card(modulAnzahl: 60));

        Assert.Contains(problems, p => p.Contains("25 kW"));
    }

    [Fact]
    public void Missing_meter_number_is_reported()
    {
        var card = TestData.Card() with { Hauptzaehlernummer = null };

        Assert.Contains(Adapter().Validate(SubmissionKind.Netzanmeldung, card), p => p.Contains("Hauptzählernummer"));
    }

    [Theory]
    [InlineData(NetzeDuisburgChannelKind.Mail, "netze-duisburg-mail")]
    [InlineData(NetzeDuisburgChannelKind.Portal, "netze-duisburg-portal")]
    public void Channel_follows_configuration(NetzeDuisburgChannelKind kind, string expected)
    {
        Assert.Equal(expected, Adapter(kind).Channel.Name);
    }
}
