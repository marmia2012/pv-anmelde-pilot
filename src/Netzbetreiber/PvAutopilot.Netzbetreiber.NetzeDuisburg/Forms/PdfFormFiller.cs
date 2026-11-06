using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;

/// <summary>
/// Füllt die AcroForm-Felder einer PDF-Vorlage. Felder werden über ihren voll qualifizierten Namen
/// (Hierarchie mit Punkt, z. B. <c>E.1: Anlagenanschrift: Vorname, Name</c>) angesprochen.
/// Ein unbekannter Name oder ein falscher Feldtyp wirft sofort – so fällt ein Vorlagenwechsel auf.
/// Die Felder bleiben editierbar (kein Flatten); <c>NeedAppearances</c> lässt den Viewer die Darstellung erzeugen.
/// </summary>
/// <remarks>
/// Werte werden direkt als <c>/V</c> bzw. <c>/AS</c> gesetzt, ohne Appearance-Streams zu rendern.
/// Dadurch braucht PDFsharp keine Schriften (im Linux-Container gibt es keine Systemschriften).
/// Liegt im Duisburg-Projekt; Umzug in ein gemeinsames Projekt, sobald ein zweiter Netzbetreiber es braucht.
/// </remarks>
internal sealed class PdfFormFiller : IDisposable
{
    private const int RadioFlag = 1 << 15;
    private const int PushButtonFlag = 1 << 16;

    private readonly PdfDocument _document;
    private readonly Dictionary<string, PdfDictionary> _fields;
    private readonly HashSet<string> _set = new(StringComparer.Ordinal);

    private PdfFormFiller(string formName, PdfDocument document)
    {
        FormName = formName;
        _document = document;
        _fields = CollectFields(document, formName);
    }

    /// <summary>Formularname für Fehlermeldungen und Log (keine Inhalte).</summary>
    public string FormName { get; }

    /// <summary>Alle ausfüllbaren Felder der Vorlage.</summary>
    public int FieldCount => _fields.Count;

    /// <summary>Felder, in die ein Wert geschrieben wurde (Text, angehakte Checkbox, gewählte Radio-Option).</summary>
    public int SetCount => _set.Count;

    /// <summary>Felder, die leer geblieben sind – bewusst oder mangels Daten.</summary>
    public int EmptyCount => FieldCount - SetCount;

    public IReadOnlyCollection<string> FieldNames => _fields.Keys;

    public static PdfFormFiller Open(string formName, Stream template)
    {
        var document = PdfReader.Open(template, PdfDocumentOpenMode.Modify);
        return new PdfFormFiller(formName, document);
    }

    /// <summary>Setzt ein Textfeld. Leerer Wert = Feld bleibt leer (der Name wird trotzdem geprüft).</summary>
    public void SetText(string name, string? value)
    {
        var field = Field(name, FieldKind.Text);
        if (string.IsNullOrWhiteSpace(value))
            return;

        field.Elements["/V"] = new PdfString(value.Trim(), PdfStringEncoding.Unicode);
        _set.Add(name);
    }

    /// <summary>Hakt eine Checkbox an (<paramref name="isChecked"/> = true). <c>false</c> lässt sie aus.</summary>
    public void SetCheckBox(string name, bool isChecked)
    {
        var field = Field(name, FieldKind.CheckBox);
        var widgets = Widgets(field);
        if (!isChecked)
        {
            field.Elements["/V"] = new PdfName("/Off");
            foreach (var widget in widgets)
                widget.Elements["/AS"] = new PdfName("/Off");
            _set.Remove(name);
            return;
        }

        var onState = widgets.SelectMany(OnStates).FirstOrDefault()
            ?? throw new InvalidOperationException($"{FormName}: Checkbox „{name}“ hat keinen Ein-Zustand.");
        field.Elements["/V"] = new PdfName(onState);
        foreach (var widget in widgets)
            widget.Elements["/AS"] = new PdfName(OnStates(widget).Contains(onState) ? onState : "/Off");
        _set.Add(name);
    }

    /// <summary>Wählt eine Radio-Option über ihren Export-Wert (z. B. <c>2</c>). Unbekannter Wert wirft.</summary>
    public void SetRadio(string name, string exportValue)
    {
        var field = Field(name, FieldKind.Radio);
        var state = "/" + exportValue;
        var widgets = Widgets(field);
        if (!widgets.Any(w => OnStates(w).Contains(state)))
            throw new InvalidOperationException($"{FormName}: Radio „{name}“ hat keine Option „{exportValue}“.");

        field.Elements["/V"] = new PdfName(state);
        foreach (var widget in widgets)
            widget.Elements["/AS"] = new PdfName(OnStates(widget).Contains(state) ? state : "/Off");
        _set.Add(name);
    }

    /// <summary>Speichert das Formular (Felder editierbar, <c>NeedAppearances</c> gesetzt).</summary>
    public byte[] Save()
    {
        var acroForm = _document.Internals.Catalog.Elements.GetDictionary("/AcroForm")
            ?? throw new InvalidOperationException($"{FormName}: Vorlage enthält kein AcroForm.");
        acroForm.Elements["/NeedAppearances"] = new PdfBoolean(true);

        using var stream = new MemoryStream();
        _document.Save(stream, false);
        return stream.ToArray();
    }

    public void Dispose() => _document.Dispose();

    private enum FieldKind { Text, CheckBox, Radio }

    private PdfDictionary Field(string name, FieldKind expected)
    {
        if (!_fields.TryGetValue(name, out var field))
            throw new InvalidOperationException($"{FormName}: Feld „{name}“ gibt es in der Vorlage nicht.");

        var actual = KindOf(field);
        if (actual != expected)
            throw new InvalidOperationException($"{FormName}: Feld „{name}“ ist {actual?.ToString() ?? "unbekannt"}, nicht {expected}.");
        return field;
    }

    private static FieldKind? KindOf(PdfDictionary field)
    {
        var type = Inherited(field, "/FT") as PdfName;
        var flags = Inherited(field, "/Ff") is PdfInteger ff ? ff.Value : 0;
        return type?.Value switch
        {
            "/Tx" => FieldKind.Text,
            "/Btn" when (flags & PushButtonFlag) != 0 => null,
            "/Btn" when (flags & RadioFlag) != 0 => FieldKind.Radio,
            "/Btn" => FieldKind.CheckBox,
            _ => null,
        };
    }

    private static PdfItem? Inherited(PdfDictionary field, string key)
    {
        for (var current = field; current is not null; current = Resolve(current.Elements["/Parent"]))
        {
            if (current.Elements[key] is { } value)
                return value is PdfReference reference ? reference.Value : value;
        }
        return null;
    }

    /// <summary>Endfelder (mit Wert) über die ganze Hierarchie, Schlüssel = voll qualifizierter Name.</summary>
    private static Dictionary<string, PdfDictionary> CollectFields(PdfDocument document, string formName)
    {
        var acroForm = document.Internals.Catalog.Elements.GetDictionary("/AcroForm")
            ?? throw new InvalidOperationException($"{formName}: Vorlage enthält kein AcroForm.");
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        IEnumerable<PdfItem> roots = acroForm.Elements.GetArray("/Fields")?.Elements ?? Enumerable.Empty<PdfItem>();
        foreach (var item in roots)
        {
            if (Resolve(item) is { } root)
                Collect(root, string.Empty, result);
        }
        return result;
    }

    private static void Collect(PdfDictionary node, string prefix, Dictionary<string, PdfDictionary> result)
    {
        var partial = node.Elements.GetString("/T");
        var name = string.IsNullOrEmpty(partial) ? prefix : prefix.Length == 0 ? partial : $"{prefix}.{partial}";
        var childFields = Kids(node).Where(k => k.Elements.ContainsKey("/T")).ToList();
        if (childFields.Count > 0)
        {
            foreach (var child in childFields)
                Collect(child, name, result);
            return;
        }
        if (name.Length > 0)
            result[name] = node;
    }

    /// <summary>Widgets eines Endfelds: entweder die Kinder ohne eigenen Namen oder das Feld selbst.</summary>
    private static List<PdfDictionary> Widgets(PdfDictionary field)
    {
        var kids = Kids(field).Where(k => !k.Elements.ContainsKey("/T")).ToList();
        return kids.Count > 0 ? kids : [field];
    }

    private static IEnumerable<string> OnStates(PdfDictionary widget) =>
        widget.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N")?.Elements.Keys.Where(k => k != "/Off") ?? [];

    private static IEnumerable<PdfDictionary> Kids(PdfDictionary node) =>
        node.Elements.GetArray("/Kids")?.Elements.Select(Resolve).OfType<PdfDictionary>() ?? [];

    private static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfReference reference => reference.Value as PdfDictionary,
        PdfDictionary dictionary => dictionary,
        _ => null,
    };
}
