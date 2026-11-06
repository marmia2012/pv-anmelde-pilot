using System.Net;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Sources.HubSpot.Documents;

namespace PvAutopilot.Tests.Sources.HubSpot;

public class VollmachtFinderTests
{
    private readonly HubSpotRig _rig = new();

    private Task<DocumentRef?> Find() => _rig.Finder().FindAsync(HubSpotRig.DealId, default);

    [Fact]
    public async Task Newest_matching_pdf_wins_across_notes()
    {
        _rig.AddAttachments("1001", "f-old", "2026-10-01T10:00:00Z");
        _rig.AddAttachments("1002", "f-new", "2026-11-02T10:00:00Z");
        _rig.AddFile("f-old", "Vollmacht alt", "pdf", "2026-10-01T10:00:00Z");
        _rig.AddFile("f-new", "Vollmacht neu", "pdf", "2026-11-02T10:00:00Z");

        var found = await Find();

        Assert.Equal(new DocumentRef(DocumentKind.EvuVollmacht, "hubspot", "f-new", "Vollmacht neu.pdf"), found);
    }

    [Fact]
    public async Task Newest_file_wins_within_one_note()
    {
        _rig.AddAttachments("1001", "f-a;f-b", "2026-11-02T10:00:00Z");
        _rig.AddFile("f-a", "Vollmacht A", "pdf", "2026-11-01T10:00:00Z");
        _rig.AddFile("f-b", "Vollmacht B", "pdf", "2026-11-02T09:00:00Z");

        Assert.Equal("f-b", (await Find())!.Reference);
    }

    [Fact]
    public async Task Emails_are_searched_as_well_as_notes()
    {
        _rig.AddAttachments("2001", "f-mail", "2026-11-02T10:00:00Z", type: "emails");
        _rig.AddFile("f-mail", "Vollmacht per Mail", "pdf", "2026-11-02T10:00:00Z");

        Assert.Equal("f-mail", (await Find())!.Reference);
    }

    [Fact]
    public async Task A_newer_email_beats_an_older_note()
    {
        _rig.AddAttachments("1001", "f-note", "2026-10-01T10:00:00Z");
        _rig.AddAttachments("2001", "f-mail", "2026-11-05T10:00:00Z", type: "emails");
        _rig.AddFile("f-note", "Vollmacht Notiz", "pdf", "2026-10-01T10:00:00Z");
        _rig.AddFile("f-mail", "Vollmacht Mail", "pdf", "2026-11-05T10:00:00Z");

        Assert.Equal("f-mail", (await Find())!.Reference);
    }

    [Fact]
    public async Task Non_pdf_attachments_are_ignored_even_if_newer()
    {
        _rig.AddAttachments("1001", "f-pdf", "2026-10-01T10:00:00Z");
        _rig.AddAttachments("1002", "f-jpg", "2026-11-02T10:00:00Z");
        _rig.AddFile("f-pdf", "Vollmacht", "pdf", "2026-10-01T10:00:00Z");
        _rig.AddFile("f-jpg", "Vollmacht Foto", "jpg", "2026-11-02T10:00:00Z");

        Assert.Equal("f-pdf", (await Find())!.Reference);
    }

    [Fact]
    public async Task Pdf_without_the_pattern_in_its_name_is_ignored()
    {
        _rig.AddAttachments("1001", "f-angebot", "2026-11-02T10:00:00Z");
        _rig.AddFile("f-angebot", "Angebot", "pdf", "2026-11-02T10:00:00Z");

        Assert.Null(await Find());
    }

    [Fact]
    public async Task Pattern_is_case_insensitive_and_matches_inside_the_name()
    {
        _rig.AddAttachments("1001", "f1", "2026-11-02T10:00:00Z");
        _rig.AddFile("f1", "Scan_EVU-VOLLMACHT_unterschrieben", "PDF", "2026-11-02T10:00:00Z");

        var found = await Find();

        Assert.Equal("f1", found!.Reference);
        Assert.Equal("Scan_EVU-VOLLMACHT_unterschrieben.pdf".ToLowerInvariant(), found.FileName.ToLowerInvariant());
    }

    [Fact]
    public async Task Configured_pattern_is_used()
    {
        _rig.Options.VollmachtFileNamePattern = "Netzvollmacht";
        _rig.AddAttachments("1001", "f1", "2026-11-02T10:00:00Z");
        _rig.AddAttachments("1002", "f2", "2026-11-01T10:00:00Z");
        _rig.AddFile("f1", "Vollmacht", "pdf", "2026-11-02T10:00:00Z");
        _rig.AddFile("f2", "netzvollmacht", "pdf", "2026-11-01T10:00:00Z");

        Assert.Equal("f2", (await Find())!.Reference);
    }

    [Fact]
    public async Task File_name_that_already_ends_with_the_extension_is_not_doubled()
    {
        _rig.AddAttachments("1001", "f1", "2026-11-02T10:00:00Z");
        _rig.AddFile("f1", "Vollmacht.pdf", "pdf", "2026-11-02T10:00:00Z");

        Assert.Equal("Vollmacht.pdf", (await Find())!.FileName);
    }

    [Fact]
    public async Task No_attachments_at_all_gives_null()
    {
        Assert.Null(await Find());
    }

    [Fact]
    public async Task Engagement_without_attachment_ids_is_skipped()
    {
        _rig.AddAttachments("1001", "", "2026-11-02T10:00:00Z");

        Assert.Null(await Find());
        Assert.DoesNotContain(_rig.Handler.Requests, r => r.Path.StartsWith("files/v3/files/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Vanished_file_is_skipped()
    {
        _rig.AddAttachments("1001", "f-gone;f-ok", "2026-11-02T10:00:00Z");
        _rig.AddFile("f-ok", "Vollmacht", "pdf", "2026-11-02T10:00:00Z");

        Assert.Equal("f-ok", (await Find())!.Reference);
    }

    [Fact]
    public async Task Timestamps_as_epoch_milliseconds_order_the_notes_too()
    {
        var older = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString();
        var newer = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString();
        _rig.AddAttachments("1001", "f-newer", newer);
        _rig.AddAttachments("1002", "f-older", older);
        _rig.AddFile("f-newer", "Vollmacht", "pdf", "2026-11-01T00:00:00Z");
        _rig.AddFile("f-older", "Vollmacht", "pdf", "2026-10-01T00:00:00Z");

        Assert.Equal("f-newer", (await Find())!.Reference);
    }

    [Fact]
    public async Task Server_error_while_searching_is_technical()
    {
        _rig.Handler.Get($"crm/v4/objects/0-3/{HubSpotRig.DealId}/associations/notes", HttpStatusCode.InternalServerError, "Erika Mustermann");

        var error = await Assert.ThrowsAsync<HttpRequestException>(Find);

        Assert.DoesNotContain("Mustermann", error.Message);
    }

    [Fact]
    public async Task Search_only_reads()
    {
        _rig.AddAttachments("1001", "f1", "2026-11-02T10:00:00Z");
        _rig.AddFile("f1", "Vollmacht", "pdf", "2026-11-02T10:00:00Z");

        await Find();

        Assert.Empty(_rig.Handler.Writes);
    }

    // Keine personenbezogenen Daten im Log

    [Fact]
    public async Task Log_has_counts_but_no_file_names_for_a_hit_and_for_a_miss()
    {
        _rig.AddAttachments("1001", "f1", "2026-11-02T10:00:00Z");
        _rig.AddFile("f1", "Vollmacht_Erika_Mustermann", "pdf", "2026-11-02T10:00:00Z");
        await Find();
        var hit = _rig.FinderLog.All;

        var miss = new HubSpotRig();
        miss.AddAttachments("1001", "f2", "2026-11-02T10:00:00Z");
        miss.AddFile("f2", "Rechnung_Erika_Mustermann", "pdf", "2026-11-02T10:00:00Z");
        await miss.Finder().FindAsync(HubSpotRig.DealId, default);

        foreach (var log in new[] { hit, miss.FinderLog.All })
        {
            Assert.NotEmpty(log);
            Assert.DoesNotContain("Erika", log);
            Assert.DoesNotContain("Mustermann", log);
            Assert.DoesNotContain("Vollmacht_", log);
            Assert.DoesNotContain("Rechnung", log);
        }
    }
}
