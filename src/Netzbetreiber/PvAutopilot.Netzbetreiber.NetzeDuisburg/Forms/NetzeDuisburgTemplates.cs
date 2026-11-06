namespace PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;

/// <summary>Ausfüllbare Formulare von Netze Duisburg.</summary>
public enum NetzeDuisburgForm
{
    /// <summary>E.1 Antragstellung.</summary>
    E1,

    /// <summary>E.2 Datenblatt für Erzeugungsanlagen.</summary>
    E2,

    /// <summary>E.3 Datenblatt für Speicher.</summary>
    E3,

    /// <summary>E.8 Inbetriebsetzungsprotokoll.</summary>
    E8,

    /// <summary>Antrag auf Zählermontage – Netznutzung Strom.</summary>
    Zaehlerantrag,
}

/// <summary>
/// Vorlagen als eingebettete Ressourcen (<c>Forms/Templates/</c>). Messkonzept-Auswahlblatt, E.6 und E.7
/// werden nicht ausgefüllt (Auswahl steht in E.2 „Bemerkungen“ bzw. Herstellerdokumente) und sind deshalb nicht eingebettet.
/// </summary>
internal static class NetzeDuisburgTemplates
{
    private const string Prefix = "NetzeDuisburg.Templates.";

    public static string FileName(NetzeDuisburgForm form) => form switch
    {
        NetzeDuisburgForm.E1 => "E1_Antragstellung.pdf",
        NetzeDuisburgForm.E2 => "E2_Datenblatt_Erzeugungsanlage.pdf",
        NetzeDuisburgForm.E3 => "E3_Datenblatt_Speicher.pdf",
        NetzeDuisburgForm.E8 => "E8_Inbetriebsetzungsprotokoll.pdf",
        NetzeDuisburgForm.Zaehlerantrag => "Zaehlerantrag.pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
    };

    public static Stream Open(NetzeDuisburgForm form)
    {
        var name = Prefix + FileName(form);
        return typeof(NetzeDuisburgTemplates).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Vorlage {name} ist nicht eingebettet.");
    }
}
