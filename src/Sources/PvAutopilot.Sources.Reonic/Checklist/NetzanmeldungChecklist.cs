using PvAutopilot.Core.DataCard;

namespace PvAutopilot.Sources.Reonic.Checklist;

/// <summary>
/// Interne Checkliste „Netzanmeldung“ mit angewendeten Verbesserungen (docs/konzept/reonic-checkliste.md, 4.1/4.3/4.7):
/// Komponenten und Datenblätter kommen aus der Hauptvariante + Katalog, die Hauptzählernummer aus dem Projektfeld.
/// In der Checkliste bleiben nur die Vor-Ort-Angaben (Zeilen #2–33 und #67 des Konzepts).
/// </summary>
public static class NetzanmeldungChecklist
{
    public const int MaxZaehler = 4;

    public static class Keys
    {
        public const string FotoHauptzaehler = "na.foto.hauptzaehler";
        public const string FotoZaehlerschrank = "na.foto.zaehlerschrank";
        public const string FotoHak = "na.foto.hak";
        public const string Kaskade = "na.kaskade";
        public const string ZaehlerAnzahl = "na.zaehler.anzahl";
        public const string AnlageArt = "na.anlage.art";
        public const string Messkonzept = "na.messkonzept";
        public const string MesskonzeptBemerkung = "na.messkonzept.bemerkung";
        public const string Schaltplan = "na.schaltplan";

        public static string ZaehlerTyp(int n) => $"na.zaehler{n}.typ";
        public static string ZaehlerNummer(int n) => $"na.zaehler{n}.nummer";
        public static string ZaehlerVerbrauchsstelle(int n) => $"na.zaehler{n}.verbrauchsstelle";
        public static string ZaehlerVerbrauchsstelleText(int n) => $"na.zaehler{n}.verbrauchsstelle.text";
        public static string ZaehlerEinbauort(int n) => $"na.zaehler{n}.einbauort";
        public static string ZaehlerBefestigung(int n) => $"na.zaehler{n}.befestigung";
    }

    /// <summary>
    /// Fallback, wenn ein Feld keinen <c>[key: …]</c> in der Beschreibung hat: Feldtitel (wie im Konzept) → Schlüssel.
    /// Verglichen wird normalisiert (siehe <see cref="ChecklistText.Normalize"/>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> TitleFallback = BuildTitleFallback();

    public static readonly IReadOnlyDictionary<string, ZaehlerwechselTyp> ZaehlerTypen = Options(
        ("Tausch gegen Zweirichtungszähler", ZaehlerwechselTyp.TauschZweirichtungszaehler),
        ("Ausbau – Zähler abmelden", ZaehlerwechselTyp.Ausbau),
        ("Neusetzung zusätzlicher Zähler", ZaehlerwechselTyp.Neusetzung),
        ("Umbau auf Kaskade (Erzeugungszähler)", ZaehlerwechselTyp.UmbauKaskade),
        ("Kein Wechsel (nur Bestand)", ZaehlerwechselTyp.KeinWechsel));

    /// <summary><c>null</c> = „Unbekannt“.</summary>
    public static readonly IReadOnlyDictionary<string, Zaehlerbauform?> Befestigungen = Options<Zaehlerbauform?>(
        ("3-Punkt", Zaehlerbauform.DreiPunkt),
        ("eHZ", Zaehlerbauform.Ehz),
        ("Unbekannt", null));

    public static readonly IReadOnlyDictionary<string, Installationsart> AnlagenArten = Options(
        ("Neuerrichtung", Installationsart.Neuerrichtung),
        ("Erweiterung einer bestehenden Anlage", Installationsart.Erweiterung));

    public static readonly IReadOnlyDictionary<string, bool> JaNein = Options(("Ja", true), ("Nein", false));

    public const string MesskonzeptSonstiges = "Sonstiges (siehe Bemerkung)";

    /// <summary>Netzbetreiberneutrale Werte; die Zuordnung zur Formularnummer macht der Netzbetreiber-Adapter.</summary>
    public static readonly IReadOnlyList<string> Messkonzepte =
    [
        "Überschusseinspeisung – Zweirichtungszähler",
        "Volleinspeisung – Erzeugungszähler",
        "Kaskade – Zweirichtungs- + Erzeugungszähler",
        "Überschuss + separater Zähler steuerbare Verbrauchseinrichtung (§ 14a)",
        MesskonzeptSonstiges,
    ];

    private static Dictionary<string, string> BuildTitleFallback()
    {
        var titles = new Dictionary<string, string>
        {
            ["Foto Hauptstromzähler (Nummer lesbar)"] = Keys.FotoHauptzaehler,
            ["Foto Zählerschrank (geöffnet, komplett)"] = Keys.FotoZaehlerschrank,
            ["Foto Hausanschlusskasten (HAK) mit Sicherungen"] = Keys.FotoHak,
            ["Kaskadenschaltung"] = Keys.Kaskade,
            ["Anzahl Zähler mit Wechsel, Abmeldung oder Neusetzung"] = Keys.ZaehlerAnzahl,
            ["Neuerrichtung oder Erweiterung"] = Keys.AnlageArt,
            ["Messkonzept"] = Keys.Messkonzept,
            ["Bemerkung Messkonzept"] = Keys.MesskonzeptBemerkung,
            ["Übersichtsschaltplan"] = Keys.Schaltplan,
        };
        for (var n = 1; n <= MaxZaehler; n++)
        {
            titles[$"Zähler {n}: Typ Zählerwechsel"] = Keys.ZaehlerTyp(n);
            titles[$"Zähler {n}: Zählernummer"] = Keys.ZaehlerNummer(n);
            titles[$"Zähler {n}: Lage der Verbrauchsstelle"] = Keys.ZaehlerVerbrauchsstelle(n);
            titles[$"Zähler {n}: Bezeichnung Verbrauchsstelle"] = Keys.ZaehlerVerbrauchsstelleText(n);
            titles[$"Zähler {n}: Einbauort"] = Keys.ZaehlerEinbauort(n);
            titles[$"Zähler {n}: Befestigung"] = Keys.ZaehlerBefestigung(n);
        }
        return titles.ToDictionary(t => ChecklistText.Normalize(t.Key), t => t.Value);
    }

    private static Dictionary<string, T> Options<T>(params (string Text, T Value)[] options) =>
        options.ToDictionary(o => ChecklistText.Normalize(o.Text), o => o.Value);
}
