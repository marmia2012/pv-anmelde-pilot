using System.Reflection;
using PvAutopilot.Netzbetreiber.NetzeDuisburg.Forms;

namespace PvAutopilot.Tests.Netzbetreiber;

public class NetzeDuisburgTemplateTests
{
    public static TheoryData<NetzeDuisburgForm> Forms() => new(Enum.GetValues<NetzeDuisburgForm>());

    [Theory]
    [MemberData(nameof(Forms))]
    public void Every_embedded_template_loads_and_has_fields(NetzeDuisburgForm form)
    {
        using var template = NetzeDuisburgTemplates.Open(form);
        using var filler = PdfFormFiller.Open(form.ToString(), template);

        Assert.True(filler.FieldCount > 0);
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void Every_field_constant_exists_in_its_template(NetzeDuisburgForm form)
    {
        var holder = typeof(NetzeDuisburgFormFields).GetNestedType(form.ToString())
            ?? throw new Xunit.Sdk.XunitException($"Keine Konstantenklasse für {form}");
        var names = Constants(holder).ToList();
        Assert.NotEmpty(names);

        using var template = NetzeDuisburgTemplates.Open(form);
        using var filler = PdfFormFiller.Open(form.ToString(), template);
        var missing = names.Where(n => !filler.FieldNames.Contains(n)).ToList();

        Assert.True(missing.Count == 0, $"Nicht in der Vorlage {form}: {string.Join(" | ", missing)}");
    }

    [Fact]
    public void File_names_are_distinct_pdfs()
    {
        var names = Enum.GetValues<NetzeDuisburgForm>().Select(NetzeDuisburgTemplates.FileName).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, n => Assert.EndsWith(".pdf", n));
    }

    /// <summary>Alle Feldnamen-Konstanten (string const bzw. static readonly Liste), ohne die Export-Werte der Optionsklassen.</summary>
    private static IEnumerable<string> Constants(Type holder)
    {
        foreach (var f in holder.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.FieldType == typeof(string))
                yield return (string)f.GetValue(null)!;
            else if (f.FieldType == typeof(IReadOnlyList<string>))
                foreach (var s in (IReadOnlyList<string>)f.GetValue(null)!)
                    yield return s;
        }
    }
}
