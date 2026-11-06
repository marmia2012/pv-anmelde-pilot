using System.Globalization;
using PvAutopilot.Core.DataCard;
using PvAutopilot.Core.Sources;
using PvAutopilot.Sources.HubSpot.Api;
using K = PvAutopilot.Sources.HubSpot.HubSpotPropertyKeys;

namespace PvAutopilot.Sources.HubSpot.Mapping;

/// <summary>
/// Übersetzt die Properties eines Deals in <see cref="CrmProjectData"/> (ohne Dokumente).
/// Regeln: Betreiber-Felder leer → Betreiber = Kunde; Betreiberadresse = Kundenadresse; Anlagenort leer → Kundenadresse;
/// Installationstermin = der spätere von Elektroinstallation und Dachmontage.
/// Problemtexte nennen nur fachliche Schlüssel, nie Werte.
/// </summary>
public static class DealMapper
{
    public sealed record Result(CrmProjectData Data, IReadOnlyList<string> MissingKeys);

    public static Result Map(HubSpotObject deal, HubSpotOptions options)
    {
        var ctx = new Context(deal, options);

        // Anlagenort: eigene Anlage-Felder, sonst die Kundenadresse (dann Pflicht).
        var abweichenderAnlagenort = ctx.HasAny(K.AnlageStrasse, K.AnlageHausnummer, K.AnlagePlz, K.AnlageOrt, K.AnlageAdresse);
        var anlagenadresse = abweichenderAnlagenort
            ? ctx.Address("Anlagenadresse", K.AnlageStrasse, K.AnlageHausnummer, K.AnlagePlz, K.AnlageOrt, K.AnlageAdresse)
            : ctx.Address("Kundenadresse (als Anlagenadresse)", K.KundeStrasse, K.KundeHausnummer, K.KundePlz, K.KundeOrt, K.KundeAdresse);

        // Betreiberadresse = immer die Kundenadresse, auch bei abweichendem Betreiber (Entscheidung Martin, 06.11.).
        // Bei abweichendem Anlagenort ist sie kein Pflichtfeld: Lücken werden nicht gemeldet (eigener Kontext, Probleme
        // verworfen), der Aggregator nimmt dann die Anlagenadresse.
        var betreiberadresse = abweichenderAnlagenort
            ? new Context(deal, options).Address("Kundenadresse", K.KundeStrasse, K.KundeHausnummer, K.KundePlz, K.KundeOrt, K.KundeAdresse)
            : anlagenadresse;

        var data = new CrmProjectData
        {
            PlanningProjectId = ctx.Get(K.ReonicProjectId),
            Anlagenbetreiber = ctx.Betreiber(),
            Betreiberadresse = betreiberadresse,
            Anlagenadresse = anlagenadresse,
            Errichter = ctx.Errichter(),
            Einspeiseart = ctx.Einspeiseart(),
            NhSicherungAmpere = ctx.Ampere(K.NhSicherungAmpere),
            BestandsanlageVorhanden = ctx.Bool(K.BestandsanlageVorhanden),
            Installationstermin = Later(ctx.Date(K.TerminElektroinstallation), ctx.Date(K.TerminDachmontage)),
            Problems = ctx.Problems,
        };
        return new Result(data, ctx.MissingKeys);
    }

    /// <summary>Anzahl nicht-leerer, konfigurierter Felder (für das Log).</summary>
    public static int CountFilled(HubSpotObject deal, HubSpotOptions options) =>
        K.DataCardKeys.Count(key => deal.Value(options.PropertyName(key)) is not null);

    private static DateOnly? Later(DateOnly? a, DateOnly? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;

    private sealed class Context(HubSpotObject deal, HubSpotOptions options)
    {
        public List<string> Problems { get; } = [];
        public List<string> MissingKeys { get; } = [];

        public string? Get(string key) => deal.Value(options.PropertyName(key));

        public bool HasAny(params string[] keys) => keys.Any(k => Get(k) is not null);

        private void Missing(string problem, params string[] keys)
        {
            Problems.Add($"HubSpot: {problem}");
            MissingKeys.AddRange(keys);
        }

        public Person? Betreiber()
        {
            // Abweichender Betreiber: Kontaktdaten und IBAN bleiben die des Kunden (Ansprechpartner von reinsolar).
            // Eine eigene Betreiber-Adresse gibt es vorerst nicht, es gilt die Kundenadresse (Entscheidung Martin, 06.11.;
            // CrmProjectData.Betreiberadresse, siehe Map).
            var abweichend = HasAny(K.BetreiberVorname, K.BetreiberNachname);
            var (vornameKey, nachnameKey) = abweichend
                ? (K.BetreiberVorname, K.BetreiberNachname)
                : (K.KundeVorname, K.KundeNachname);

            var vorname = Get(vornameKey);
            var nachname = Get(nachnameKey);
            if (vorname is null || nachname is null)
            {
                var fehlend = new[] { (vorname, vornameKey), (nachname, nachnameKey) }.Where(x => x.Item1 is null).Select(x => x.Item2).ToArray();
                Missing($"Anlagenbetreiber unvollständig (fehlt: {string.Join(", ", fehlend)})", fehlend);
                return null;
            }

            return new Person(
                abweichend ? null : Get(K.KundeAnrede),
                vorname,
                nachname,
                Get(K.KundeEmail),
                Get(K.KundeTelefon),
                Get(K.KundeIban)?.Replace(" ", string.Empty, StringComparison.Ordinal));
        }

        /// <summary>Getrennte Felder haben Vorrang; sonst wird die einzeilige Adresse zerlegt.</summary>
        public PostalAddress? Address(string label, string strasseKey, string nrKey, string plzKey, string ortKey, string lineKey)
        {
            var strasse = Get(strasseKey);
            var nr = Get(nrKey);
            var plz = Get(plzKey);
            var ort = Get(ortKey);

            if (strasse is not null || nr is not null || plz is not null || ort is not null)
            {
                // Hausnummer fehlt, steht aber evtl. im Straßenfeld („Musterstr. 12a“).
                if (nr is null && strasse is not null && AddressParser.SplitStreet(strasse) is { } split)
                    (strasse, nr) = split;
                // Nummer im Straßenfeld und im Hausnummernfeld: unklar, welche gilt (z. B. „Musterstr. 12“ + „14“).
                else if (nr is not null && strasse is not null && AddressParser.SplitStreet(strasse) is not null)
                {
                    Missing($"{label}: Straßenfeld endet auf eine Hausnummer und {nrKey} ist gesetzt", strasseKey, nrKey);
                    return null;
                }

                var fehlend = new[] { (strasse, strasseKey), (nr, nrKey), (plz, plzKey), (ort, ortKey) }
                    .Where(x => x.Item1 is null).Select(x => x.Item2).ToArray();
                if (fehlend.Length > 0)
                {
                    Missing($"{label} unvollständig (fehlt: {string.Join(", ", fehlend)})", fehlend);
                    return null;
                }
                if (!AddressParser.IsValidPlz(plz!))
                {
                    Missing($"{label}: PLZ ungültig (5 Ziffern erwartet)", plzKey);
                    return null;
                }
                return new PostalAddress(strasse!, nr!, plz!, ort!);
            }

            if (Get(lineKey) is { } line)
            {
                if (AddressParser.ParseSingleLine(line) is { } parsed)
                    return parsed;
                Missing($"{label} nicht zerlegbar (erwartet „Straße Nr, PLZ Ort“ oder getrennte Felder)", lineKey);
                return null;
            }

            Missing($"{label} fehlt", strasseKey, nrKey, plzKey, ortKey, lineKey);
            return null;
        }

        public Anlagenerrichter? Errichter()
        {
            if (Get(K.ErrichterFirma) is not { } firma)
                return null;
            if (Get(K.ErrichterOrt) is not { } ort)
            {
                Missing("Errichter am Deal ohne Ort (ErrichterOrt)", K.ErrichterOrt);
                return null;
            }
            return new Anlagenerrichter(firma, ort, Get(K.ErrichterEintragungsnummer), Get(K.ErrichterNetzbetreiber));
        }

        public Einspeiseart? Einspeiseart()
        {
            if (Get(K.Einspeiseart) is not { } value)
                return null;
            foreach (var (option, art) in options.EinspeiseartValues)
            {
                if (string.Equals(option.Trim(), value, StringComparison.OrdinalIgnoreCase))
                    return art;
            }
            Missing("Einspeiseart: unbekannter Auswahlwert (HubSpot:EinspeiseartValues ergänzen)", K.Einspeiseart);
            return null;
        }

        public int? Ampere(string key)
        {
            if (Get(key) is not { } value)
                return null;
            var number = value.TrimEnd('A', 'a').Trim().Replace(',', '.');
            if (decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out var ampere)
                && ampere > 0 && ampere == decimal.Truncate(ampere) && ampere <= int.MaxValue)
                return (int)ampere;
            Missing($"{key}: keine ganze Zahl in A", key);
            return null;
        }

        public bool? Bool(string key)
        {
            switch (Get(key)?.ToLowerInvariant())
            {
                case null: return null;
                case "true" or "ja" or "yes" or "1": return true;
                case "false" or "nein" or "no" or "0": return false;
                default:
                    Missing($"{key}: kein Ja/Nein-Wert", key);
                    return null;
            }
        }

        public DateOnly? Date(string key)
        {
            if (Get(key) is not { } value)
                return null;
            if (HubSpotDate.TryParse(value, options.ResolveTimeZone(), out var date))
                return date;
            Missing($"{key}: kein gültiges Datum", key);
            return null;
        }
    }
}
