using PvAutopilot.Core.Logging;

namespace PvAutopilot.Core.DataCard;

/// <summary>
/// NAB-Datenkarte: alle Daten, die für Netzanmeldung, Inbetriebnahme, Zählerwechsel und MaStR
/// gebraucht werden – aggregiert aus CRM (HubSpot), Planung (Reonic) und Dokumentenablage (SharePoint).
/// Wird als JSON in der Prozess-DB gespeichert.
/// </summary>
public sealed record NabDataCard
{
    public required Person Anlagenbetreiber { get; init; }

    /// <summary>
    /// Anschrift des Anlagenbetreibers (Formulare: Anschlussnehmer/Anlagenbetreiber). Der Aggregator füllt sie immer
    /// (Fallback <see cref="Anlagenadresse"/>); <c>null</c> nur bei Datenkarten aus der Zeit vor diesem Feld.
    /// Straße und Hausnummer sind über <see cref="PostalAddress"/> als personenbezogen markiert.
    /// </summary>
    public PostalAddress? Betreiberadresse { get; init; }

    public required PostalAddress Anlagenadresse { get; init; }
    public required Anlagenerrichter Errichter { get; init; }

    public Einspeiseart Einspeiseart { get; init; } = Einspeiseart.Teileinspeisung;
    public int NhSicherungAmpere { get; init; } = 63;
    public DateOnly? Installationstermin { get; init; }

    /// <summary>Führende Quelle ist die Planung (Reonic-Checkliste), nicht das CRM.</summary>
    public Installationsart Installationsart { get; init; } = Installationsart.Neuerrichtung;

    public bool BestandsanlageVorhanden => Installationsart == Installationsart.Erweiterung;

    [PersonalData] public string? Hauptzaehlernummer { get; init; }
    public bool Kaskadenschaltung { get; init; }
    public IReadOnlyList<Zaehler> Zaehler { get; init; } = [];

    public IReadOnlyList<ModulPosition> Module { get; init; } = [];
    public IReadOnlyList<WechselrichterPosition> Wechselrichter { get; init; } = [];
    public Batteriespeicher? Speicher { get; init; }
    public Wallbox? Wallbox { get; init; }

    /// <summary>Netzbetreiberneutraler Wert; die Zuordnung zur Formularnummer liegt im Netzbetreiber-Adapter.</summary>
    public string? Messkonzept { get; init; }

    [PersonalData] public string? MesskonzeptBemerkung { get; init; }

    public IReadOnlyList<DocumentRef> Dokumente { get; init; } = [];

    public decimal GesamtleistungKwp => Module.Sum(m => m.LeistungWp * m.Anzahl) / 1000m;
    public decimal WechselrichterLeistungKw => Wechselrichter.Sum(w => w.LeistungW * w.Anzahl) / 1000m;
}

public sealed record Person(
    [property: PersonalData] string? Anrede,
    [property: PersonalData] string Vorname,
    [property: PersonalData] string Nachname,
    [property: PersonalData] string? Email,
    [property: PersonalData] string? Telefon,
    [property: PersonalData] string? Iban);

/// <summary>PLZ und Ort bleiben lesbar (für die Netzbetreiber-Ermittlung im Log), Straße wird maskiert.</summary>
public sealed record PostalAddress(
    [property: PersonalData] string Strasse,
    [property: PersonalData] string Hausnummer,
    string Plz,
    string Ort);

public sealed record Anlagenerrichter(string Firma, string Ort, string? Eintragungsnummer, string? EingetragenBeiNetzbetreiber);

/// <summary>Ein Zähler, an dem etwas passiert (Tausch, Abmeldung, Neusetzung …). Zählernummer fehlt bei Neusetzung.</summary>
public sealed record Zaehler(
    [property: PersonalData] string? Zaehlernummer,
    ZaehlerwechselTyp Wechseltyp,
    string? LageVerbrauchsstelle,
    [property: PersonalData] string? BezeichnungVerbrauchsstelle,
    string? Einbauort,
    Zaehlerbauform? Bauform)
{
    public bool Abmelden => Wechseltyp == ZaehlerwechselTyp.Ausbau;
}

public sealed record ModulPosition(string Hersteller, string Modell, decimal LeistungWp, decimal Anzahl);

public sealed record WechselrichterPosition(
    string Hersteller,
    string Modell,
    string? Bezeichnung,
    decimal LeistungW,
    decimal Anzahl,
    decimal? ScheinleistungKva,
    int? Phasen = null);

/// <summary>
/// <see cref="KapazitaetKwh"/> ist die Nennkapazität laut Katalog. Die nutzbare Kapazität (MaStR)
/// kommt später aus dem Datenblatt.
/// </summary>
public sealed record Batteriespeicher(string Hersteller, string Modell, decimal KapazitaetKwh, Speicherkopplung? Kopplung);

public sealed record Wallbox(string Hersteller, string Modell, decimal LeistungKw, decimal Anzahl = 1);

/// <summary>
/// Verweis auf ein Dokument in einer Quelle (z. B. Reonic-Datei, SharePoint-Item, HubSpot-Datei).
/// <see cref="Reference"/> kann eine signierte URL auf Kundenfotos sein, <see cref="FileName"/> den Kundennamen enthalten
/// (z. B. „Vollmacht Mustermann.pdf“); beide werden deshalb im Log maskiert.
/// </summary>
public sealed record DocumentRef(
    DocumentKind Kind,
    string Source,
    [property: PersonalData] string Reference,
    [property: PersonalData] string FileName);

public enum Einspeiseart { Teileinspeisung, Volleinspeisung }

public enum Installationsart { Neuerrichtung, Erweiterung }

public enum Zaehlerbauform { DreiPunkt, Ehz }

public enum ZaehlerwechselTyp
{
    TauschZweirichtungszaehler,
    Ausbau,
    Neusetzung,
    UmbauKaskade,
    KeinWechsel,
}

public enum Speicherkopplung { Ac, Dc }

public enum DocumentKind
{
    Schaltplan,
    FotoHauptzaehler,
    FotoZaehlerschrank,
    FotoHausanschlusskasten,
    Datenblatt,
    Einheitenzertifikat,
    NaSchutzZertifikat,
    EvuVollmacht,
    Lageplan,
    Einspeisezusage,
    Sonstiges,
}
