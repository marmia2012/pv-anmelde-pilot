using System.Text;
using System.Text.RegularExpressions;
using PvAutopilot.Sources.Reonic.Api;

namespace PvAutopilot.Sources.Reonic.Checklist;

/// <summary>Textnormalisierung für den Vergleich von Titeln und Auswahlwerten.</summary>
public static partial class ChecklistText
{
    /// <summary>Kleinschreibung, Gedankenstriche → Bindestrich, Mehrfach-Leerzeichen zusammengefasst.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Trim().ToLowerInvariant())
            builder.Append(c is '–' or '—' or '‐' ? '-' : c);
        return Whitespace().Replace(builder.ToString(), " ");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>
/// Werte einer Reonic-Checkliste, adressiert über technische Schlüssel. Der Schlüssel steht als <c>[key: …]</c>
/// in der Beschreibung eines Felds (bei Bild-Feldern: der einzelnen Foto-Anforderung); ohne Schlüssel greift
/// der Titel-Fallback. Als irrelevant markierte Felder gelten als leer.
/// </summary>
public sealed partial class ChecklistValues
{
    private readonly Dictionary<string, string> _choices = [];
    private readonly Dictionary<string, string> _texts = [];
    private readonly Dictionary<string, IReadOnlyList<ReonicAttachment>> _attachments = [];
    private readonly HashSet<string> _found = [];
    private readonly List<string> _duplicateKeys = [];

    private ChecklistValues() { }

    /// <summary>Schlüssel, die mehrfach vorkommen. Gewertet wird jeweils das erste Vorkommen.</summary>
    public IReadOnlyList<string> DuplicateKeys => _duplicateKeys;

    public static ChecklistValues Parse(ReonicChecklist checklist, IReadOnlyDictionary<string, string> titleFallback)
    {
        var values = new ChecklistValues();
        var fields = Fields(checklist).ToList();

        // Zwei Durchgänge: Ein ausdrücklicher [key: …] hat Vorrang vor einem Titel-Treffer, auch wenn das
        // gleichnamige Feld (z. B. „Messkonzept“ in einem anderen Block) weiter vorne steht.
        foreach (var field in fields)
            if (KeyFromDescription(field.Description) is { } key)
                values.Read(key, field);

        var explicitKeys = values._found.ToHashSet();
        foreach (var field in fields)
        {
            if (KeyFromDescription(field.Description) is not null)
                continue;
            if (titleFallback.GetValueOrDefault(ChecklistText.Normalize(field.Title)) is { } key && !explicitKeys.Contains(key))
                values.Read(key, field);
        }
        return values;
    }

    /// <summary>Ein adressierbares Feld: ein Auswahl-/Freitext-Element oder eine einzelne Foto-Anforderung.</summary>
    private sealed record Field(string Type, string? Title, string? Description, bool Irrelevant, ReonicChecklistItem Item,
        ReonicChecklistSubItem? Request);

    private static IEnumerable<Field> Fields(ReonicChecklist checklist)
    {
        foreach (var item in checklist.Blocks.SelectMany(b => b.Segments).SelectMany(s => s.Items))
        {
            if (item.Type is "radio" or "freetext")
                yield return new Field(item.Type, item.Title, item.Description, item.MarkedIrrelevant, item, null);
            else if (item.Type == "image")
                foreach (var request in item.Items ?? [])
                    yield return new Field(item.Type, request.Title, request.Description,
                        item.MarkedIrrelevant || request.MarkedIrrelevant, item, request);
        }
    }

    private void Read(string key, Field field)
    {
        if (!_found.Add(key))
        {
            _duplicateKeys.Add(key);
            return;
        }
        if (field.Irrelevant)
            return;

        switch (field.Type)
        {
            case "radio":
                if (field.Item.Items?.FirstOrDefault(i => i.Checked)?.Text is { } selected)
                    _choices[key] = selected.Trim();
                break;
            case "freetext":
                if (!string.IsNullOrWhiteSpace(field.Item.Text))
                    _texts[key] = field.Item.Text.Trim();
                break;
            case "image":
                var files = (field.Request!.Attachments ?? []).Where(a => !string.IsNullOrWhiteSpace(a.Url)).ToList();
                if (files.Count > 0)
                    _attachments[key] = files;
                break;
        }
    }

    public string? Choice(string key) => _choices.GetValueOrDefault(key);

    public string? Text(string key) => _texts.GetValueOrDefault(key);

    public IReadOnlyList<ReonicAttachment> Attachments(string key) => _attachments.GetValueOrDefault(key) ?? [];

    /// <summary>Gibt es das Feld in der Checkliste überhaupt (auch leer)?</summary>
    public bool Contains(string key) => _found.Contains(key);

    private static string? KeyFromDescription(string? description)
    {
        if (string.IsNullOrEmpty(description))
            return null;
        var match = KeyPattern().Match(description);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"\[key:\s*([A-Za-z0-9_.]+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex KeyPattern();
}
