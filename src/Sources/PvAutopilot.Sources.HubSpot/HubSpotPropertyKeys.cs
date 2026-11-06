namespace PvAutopilot.Sources.HubSpot;

/// <summary>
/// Fachliche Schlüssel in <see cref="HubSpotOptions.Properties"/>. Der interne HubSpot-Name steht nur in der Konfiguration.
/// Adressen: getrennte Felder (Straße, Hausnummer, PLZ, Ort) haben Vorrang vor der einzeiligen Adresse.
/// </summary>
public static class HubSpotPropertyKeys
{
    // Kunde (Vertragspartner)
    public const string KundeAnrede = nameof(KundeAnrede);
    public const string KundeVorname = nameof(KundeVorname);
    public const string KundeNachname = nameof(KundeNachname);
    public const string KundeEmail = nameof(KundeEmail);
    public const string KundeTelefon = nameof(KundeTelefon);
    public const string KundeIban = nameof(KundeIban);
    public const string KundeStrasse = nameof(KundeStrasse);
    public const string KundeHausnummer = nameof(KundeHausnummer);
    public const string KundePlz = nameof(KundePlz);
    public const string KundeOrt = nameof(KundeOrt);
    public const string KundeAdresse = nameof(KundeAdresse);

    // Anlagenbetreiber, nur wenn abweichend vom Kunden
    public const string BetreiberVorname = nameof(BetreiberVorname);
    public const string BetreiberNachname = nameof(BetreiberNachname);

    // Anlagenort, nur wenn abweichend von der Kundenadresse
    public const string AnlageStrasse = nameof(AnlageStrasse);
    public const string AnlageHausnummer = nameof(AnlageHausnummer);
    public const string AnlagePlz = nameof(AnlagePlz);
    public const string AnlageOrt = nameof(AnlageOrt);
    public const string AnlageAdresse = nameof(AnlageAdresse);

    // Anlage und Netzanschluss
    public const string ReonicProjectId = nameof(ReonicProjectId);
    public const string Einspeiseart = nameof(Einspeiseart);
    public const string NhSicherungAmpere = nameof(NhSicherungAmpere);
    public const string BestandsanlageVorhanden = nameof(BestandsanlageVorhanden);
    public const string TerminElektroinstallation = nameof(TerminElektroinstallation);
    public const string TerminDachmontage = nameof(TerminDachmontage);

    // MaStR-Login des Kunden (lesen, HubSpotMastrCredentialStore)
    public const string MastrBenutzername = nameof(MastrBenutzername);
    public const string MastrPasswort = nameof(MastrPasswort);

    // Status-Spiegel am Deal (schreiben, HubSpotStatusSink)
    public const string Status = nameof(Status);
    public const string Netzbetreiber = nameof(Netzbetreiber);
    public const string Fehlertext = nameof(Fehlertext);
    public const string MastrNummerAnlage = nameof(MastrNummerAnlage);
    public const string MastrNummerSpeicher = nameof(MastrNummerSpeicher);

    /// <summary>Von HubSpot vorgegebene Property (kein Kundenfeld): Inhaber des Deals, Ziel für Aufgaben.</summary>
    public const string HubSpotOwnerId = "hubspot_owner_id";

    // Errichter-Override je Deal (sonst Sektion Errichter der Instanz)
    public const string ErrichterFirma = nameof(ErrichterFirma);
    public const string ErrichterOrt = nameof(ErrichterOrt);
    public const string ErrichterEintragungsnummer = nameof(ErrichterEintragungsnummer);
    public const string ErrichterNetzbetreiber = nameof(ErrichterNetzbetreiber);

    /// <summary>Alle Schlüssel, die beim Lesen des Deals für die NAB-Datenkarte abgefragt werden.</summary>
    public static readonly IReadOnlyList<string> DataCardKeys =
    [
        KundeAnrede, KundeVorname, KundeNachname, KundeEmail, KundeTelefon, KundeIban,
        KundeStrasse, KundeHausnummer, KundePlz, KundeOrt, KundeAdresse,
        BetreiberVorname, BetreiberNachname,
        AnlageStrasse, AnlageHausnummer, AnlagePlz, AnlageOrt, AnlageAdresse,
        ReonicProjectId, Einspeiseart, NhSicherungAmpere, BestandsanlageVorhanden,
        TerminElektroinstallation, TerminDachmontage,
        ErrichterFirma, ErrichterOrt, ErrichterEintragungsnummer, ErrichterNetzbetreiber,
    ];
}
