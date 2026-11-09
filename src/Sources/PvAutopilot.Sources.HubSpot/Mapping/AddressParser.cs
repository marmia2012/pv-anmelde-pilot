using System.Text.RegularExpressions;
using PvAutopilot.Core.DataCard;

namespace PvAutopilot.Sources.HubSpot.Mapping;

/// <summary>
/// Zerlegt deutsche Anschriften. Einzeilig: „Straße Nr, PLZ Ort“, auch ohne Komma, mit Zeilenumbruch,
/// „D-“ vor der PLZ oder einem Land am Ende („Deutschland“, „Germany“, „DE“; mit Komma/Zeilenumbruch oder nur durch
/// Leerzeichen getrennt, z. B. „47051 Duisburg Deutschland“). Hausnummern wie <c>12</c>, <c>12a</c>, <c>12 a</c>, <c>12-14</c>, <c>12/1</c>.
/// </summary>
public static partial class AddressParser
{
    private const string HouseNumber = @"\d+\s?[a-zA-Z]?(?:\s?[-/]\s?\d+\s?[a-zA-Z]?)?";

    [GeneratedRegex(@"^(?<strasse>.*?\D)\s*(?<nr>" + HouseNumber + @")\s*(?:[,;\r\n]+\s*|\s+)(?:D\s?-\s?)?(?<plz>\d{5})\s+(?<ort>[^,;\r\n]+?)\s*(?:(?:[,;\r\n]+\s*|\s+)(?:Deutschland|Germany|DE))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex SingleLine();

    [GeneratedRegex(@"^(?<strasse>.*?\D)\s*(?<nr>" + HouseNumber + @")$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex StreetWithNumber();

    [GeneratedRegex(@"^\d{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex Plz();

    /// <summary>Einzeilige Anschrift; <c>null</c>, wenn sie nicht eindeutig zerlegbar ist.</summary>
    public static PostalAddress? ParseSingleLine(string value)
    {
        try
        {
            var match = SingleLine().Match(value.Trim());
            if (!match.Success)
                return null;
            var strasse = CleanStreet(match.Groups["strasse"].Value);
            return strasse.Length == 0
                ? null
                : new PostalAddress(strasse, NormalizeNumber(match.Groups["nr"].Value), match.Groups["plz"].Value, match.Groups["ort"].Value.Trim());
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>Straße mit angehängter Hausnummer („Musterstr. 12a“) trennen; <c>null</c>, wenn keine Nummer erkennbar ist.</summary>
    public static (string Strasse, string Hausnummer)? SplitStreet(string value)
    {
        try
        {
            var match = StreetWithNumber().Match(value.Trim());
            if (!match.Success)
                return null;
            var strasse = CleanStreet(match.Groups["strasse"].Value);
            return strasse.Length == 0 ? null : (strasse, NormalizeNumber(match.Groups["nr"].Value));
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    public static bool IsValidPlz(string value) => Plz().IsMatch(value);

    private static string CleanStreet(string value) => value.Trim().TrimEnd(',').Trim();

    private static string NormalizeNumber(string value) => Regex.Replace(value.Trim(), @"\s+", " ");
}
