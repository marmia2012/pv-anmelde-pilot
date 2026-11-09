using PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;
using F = PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms.NetzeDuisburgFormFields;

namespace PvAutopilot.Tests.Netzbetreiber;

public class PdfFormFillerTests
{
    private static PdfFormFiller Open(NetzeDuisburgForm form)
    {
        using var template = NetzeDuisburgTemplates.Open(form);
        return PdfFormFiller.Open(form.ToString(), template);
    }

    [Fact]
    public void Unknown_field_name_throws()
    {
        using var filler = Open(NetzeDuisburgForm.E1);

        Assert.Throws<InvalidOperationException>(() => filler.SetText("gibt es nicht", "x"));
        Assert.Throws<InvalidOperationException>(() => filler.SetCheckBox("gibt es nicht", true));
        Assert.Throws<InvalidOperationException>(() => filler.SetRadio("gibt es nicht", "1"));
    }

    [Fact]
    public void Wrong_field_type_throws()
    {
        using var filler = Open(NetzeDuisburgForm.E1);

        Assert.Throws<InvalidOperationException>(() => filler.SetText(F.E1.AnlagenartNeuerrichtung, "x"));
        Assert.Throws<InvalidOperationException>(() => filler.SetCheckBox(F.E1.AnlageName, true));
    }

    [Fact]
    public void Unknown_radio_option_throws()
    {
        using var filler = Open(NetzeDuisburgForm.Zaehlerantrag);

        Assert.Throws<InvalidOperationException>(() => filler.SetRadio(F.Zaehlerantrag.Auftrag, "99"));
    }

    [Fact]
    public void Text_checkbox_and_radio_survive_reading_the_saved_pdf()
    {
        byte[] bytes;
        using (var filler = Open(NetzeDuisburgForm.E1))
        {
            filler.SetText(F.E1.AnlageName, "Jürgen Müller-Lüdenscheidt");
            filler.SetCheckBox(F.E1.AnlagenartNeuerrichtung, true);
            bytes = filler.Save();
        }
        byte[] zaehlerantrag;
        using (var filler = Open(NetzeDuisburgForm.Zaehlerantrag))
        {
            filler.SetRadio(F.Zaehlerantrag.Auftrag, F.Zaehlerantrag.AuftragOption.Zaehlerwechsel);
            zaehlerantrag = filler.Save();
        }

        var e1 = PdfReadback.Of(bytes);
        Assert.Equal("Jürgen Müller-Lüdenscheidt", e1.Text(F.E1.AnlageName));
        Assert.True(e1.IsChecked(F.E1.AnlagenartNeuerrichtung));
        Assert.False(e1.IsChecked(F.E1.AnlagenartErweiterung));
        Assert.Null(e1.Text(F.E1.AnlageStrasse));
        Assert.Equal("4", PdfReadback.Of(zaehlerantrag).Radio(F.Zaehlerantrag.Auftrag));
    }

    [Fact]
    public void Unchecking_a_checkbox_leaves_it_off()
    {
        using var filler = Open(NetzeDuisburgForm.E1);
        filler.SetCheckBox(F.E1.AnlagenartNeuerrichtung, true);
        filler.SetCheckBox(F.E1.AnlagenartNeuerrichtung, false);

        Assert.False(PdfReadback.Of(filler.Save()).IsChecked(F.E1.AnlagenartNeuerrichtung));
        Assert.Equal(0, filler.SetCount);
    }

    [Fact]
    public void Empty_text_leaves_field_empty_and_uncounted()
    {
        using var filler = Open(NetzeDuisburgForm.E1);
        filler.SetText(F.E1.AnlageName, null);
        filler.SetText(F.E1.AnlageStrasse, "  ");

        Assert.Equal(0, filler.SetCount);
        Assert.Equal(filler.FieldCount, filler.EmptyCount);
    }

    [Fact]
    public void Saved_pdf_sets_NeedAppearances_and_keeps_fields_editable()
    {
        using var filler = Open(NetzeDuisburgForm.E1);
        filler.SetText(F.E1.AnlageName, "Max Mustermann");
        filler.SetCheckBox(F.E1.AnlagenartNeuerrichtung, true);

        var readback = PdfReadback.Of(filler.Save());

        Assert.True(readback.NeedAppearances);
        Assert.Empty(readback.ReadOnlyFields());
        Assert.Contains(F.E1.AnlageName, readback.Names); // nicht geflattet
    }

    [Fact]
    public void Counts_set_and_empty_fields()
    {
        using var filler = Open(NetzeDuisburgForm.E1);
        filler.SetText(F.E1.AnlageName, "x");
        filler.SetCheckBox(F.E1.AnlagenartNeuerrichtung, true);

        Assert.Equal(2, filler.SetCount);
        Assert.Equal(filler.FieldCount - 2, filler.EmptyCount);
    }
}
