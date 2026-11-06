using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PvAutopilot.Core.Notifications;
using PvAutopilot.Sources.HubSpot;

namespace PvAutopilot.Tests.Sources.HubSpot;

public class HubSpotOptionsTests
{
    [Fact]
    public void Defaults_are_deal_and_no_writes()
    {
        var o = new HubSpotOptions();

        Assert.Equal("0-3", o.ObjectTypeId);
        Assert.False(o.WriteEnabled);
        Assert.Equal(AuditLevel.Info, o.AuditLevel);
        Assert.Equal("Vollmacht", o.VollmachtFileNamePattern);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_object_type_is_a_startup_error(string objectType)
    {
        var options = HubSpotRig.DefaultOptions();
        options.ObjectTypeId = objectType;

        Assert.Contains(HubSpotOptionsValidator.Errors(options), e => e.Contains("ObjectTypeId"));
    }

    [Fact]
    public void Undefined_audit_level_is_a_startup_error()
    {
        var options = HubSpotRig.DefaultOptions();
        options.AuditLevel = (AuditLevel)42;

        Assert.Contains(HubSpotOptionsValidator.Errors(options), e => e.Contains("AuditLevel"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_vollmacht_pattern_is_a_startup_error(string pattern)
    {
        var options = HubSpotRig.DefaultOptions();
        options.VollmachtFileNamePattern = pattern;

        Assert.Contains(HubSpotOptionsValidator.Errors(options), e => e.Contains("VollmachtFileNamePattern"));
    }

    [Fact]
    public void Valid_options_have_no_errors()
    {
        Assert.Empty(HubSpotOptionsValidator.Errors(HubSpotRig.DefaultOptions()));
    }

    [Theory]
    [InlineData("Verbose", AuditLevel.Verbose)]
    [InlineData("Info", AuditLevel.Info)]
    [InlineData("Error", AuditLevel.Error)]
    public void Audit_level_binds_from_configuration(string value, AuditLevel expected)
    {
        using var provider = Provider(("HubSpot:AuditLevel", value));

        Assert.Equal(expected, provider.GetRequiredService<IOptions<HubSpotOptions>>().Value.AuditLevel);
    }

    [Fact]
    public void Invalid_audit_level_in_configuration_fails_on_resolve()
    {
        using var provider = Provider(("HubSpot:AuditLevel", "Chatty"));

        Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IOptions<HubSpotOptions>>().Value);
    }

    [Fact]
    public void Empty_object_type_in_configuration_fails_on_resolve()
    {
        using var provider = Provider(("HubSpot:ObjectTypeId", ""));

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<HubSpotOptions>>().Value);
        Assert.Contains("ObjectTypeId", error.Message);
    }

    [Fact]
    public void Configuration_without_overrides_keeps_deal_and_write_off()
    {
        using var provider = Provider();

        var o = provider.GetRequiredService<IOptions<HubSpotOptions>>().Value;
        Assert.Equal("0-3", o.ObjectTypeId);
        Assert.False(o.WriteEnabled);
    }

    private static ServiceProvider Provider(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["HubSpot:TriggerRules:0:Kind"] = "Start",
            ["HubSpot:TriggerRules:0:PropertyName"] = "dealstage",
            ["HubSpot:TriggerRules:0:Values:0"] = "stage-start",
        };
        foreach (var (key, value) in settings)
            values[key] = value;

        return new ServiceCollection().AddLogging()
            .AddHubSpot(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .BuildServiceProvider();
    }
}
