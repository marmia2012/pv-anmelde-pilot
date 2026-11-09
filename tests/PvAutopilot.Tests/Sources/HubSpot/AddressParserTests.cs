using PvAutopilot.Core.DataCard;
using PvAutopilot.Sources.HubSpot.Mapping;

namespace PvAutopilot.Tests.Sources.HubSpot;

public class AddressParserTests
{
    [Theory]
    [InlineData("Musterstraße 12, 47051 Duisburg", "Musterstraße", "12", "47051", "Duisburg")]
    [InlineData("Musterstraße 12a, 47051 Duisburg", "Musterstraße", "12a", "47051", "Duisburg")]
    [InlineData("Musterstraße 12 a, 47051 Duisburg", "Musterstraße", "12 a", "47051", "Duisburg")]
    [InlineData("Musterstraße 12a 47051 Duisburg", "Musterstraße", "12a", "47051", "Duisburg")]
    [InlineData("Musterstraße 12 47051 Duisburg", "Musterstraße", "12", "47051", "Duisburg")]
    [InlineData("Hauptstr. 12-14, D-47051 Duisburg", "Hauptstr.", "12-14", "47051", "Duisburg")]
    [InlineData("Am Park 5/1, 47051 Duisburg, Deutschland", "Am Park", "5/1", "47051", "Duisburg")]
    [InlineData("Musterstraße 12\n47051 Duisburg", "Musterstraße", "12", "47051", "Duisburg")]
    [InlineData("  Musterstraße 12 ,  47051   Duisburg  ", "Musterstraße", "12", "47051", "Duisburg")]
    [InlineData("Straße des 17. Juni 5, 47051 Duisburg", "Straße des 17. Juni", "5", "47051", "Duisburg")]
    public void Single_line_address_is_split(string line, string strasse, string nr, string plz, string ort)
    {
        Assert.Equal(new PostalAddress(strasse, nr, plz, ort), AddressParser.ParseSingleLine(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Duisburg")]
    [InlineData("Musterstraße, Duisburg")]
    [InlineData("Musterstraße 12")]
    [InlineData("47051 Duisburg")]
    [InlineData("12, 47051 Duisburg")]
    [InlineData("Musterstraße 12, 4705 Duisburg")]
    public void Unsplittable_single_line_gives_null(string line)
    {
        Assert.Null(AddressParser.ParseSingleLine(line));
    }

    [Theory]
    [InlineData("Musterstraße 12a", "Musterstraße", "12a")]
    [InlineData("Musterstraße 12", "Musterstraße", "12")]
    [InlineData("Am Park 5/1", "Am Park", "5/1")]
    public void Street_with_number_is_split(string value, string strasse, string nr)
    {
        Assert.Equal((strasse, nr), AddressParser.SplitStreet(value));
    }

    [Theory]
    [InlineData("Musterstraße")]
    [InlineData("")]
    public void Street_without_number_gives_null(string value)
    {
        Assert.Null(AddressParser.SplitStreet(value));
    }

    [Theory]
    [InlineData("47051", true)]
    [InlineData("4705", false)]
    [InlineData("470511", false)]
    [InlineData("4705a", false)]
    public void Plz_must_be_five_digits(string value, bool valid)
    {
        Assert.Equal(valid, AddressParser.IsValidPlz(value));
    }
}
