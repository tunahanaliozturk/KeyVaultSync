using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class PrefixSecretResolverTests
{
    private static readonly Dictionary<string, string> Mappings = new()
    {
        ["partnercenter-clientsecret"] = "PartnerCenter:ClientSecret",
        ["sendgrid-apikey"] = "SendGrid:APIKey",
    };

    private static IReadOnlyList<KeyValuePair<string, string>> Pairs(params (string Key, string Value)[] kv)
        => kv.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList();

    [Fact]
    public void Maps_config_key_to_prefixed_secret_name()
    {
        var r = new PrefixSecretResolver("lm-dev", Mappings)
            .Resolve(Pairs(("PartnerCenter:ClientSecret", "topsecret")));

        Assert.Single(r.Planned);
        Assert.Equal("PartnerCenter:ClientSecret", r.Planned[0].DisplayKey);
        Assert.Equal("lm-dev-partnercenter-clientsecret", r.Planned[0].SecretName);
        Assert.Equal("topsecret", r.Planned[0].Value);
    }

    [Fact]
    public void Matches_config_key_case_insensitively()
    {
        var r = new PrefixSecretResolver("lm-dev", Mappings)
            .Resolve(Pairs(("partnercenter:clientsecret", "v")));
        Assert.Single(r.Planned);
        Assert.Equal("lm-dev-partnercenter-clientsecret", r.Planned[0].SecretName);
    }

    [Fact]
    public void Records_unmapped_keys()
    {
        var r = new PrefixSecretResolver("lm-dev", Mappings)
            .Resolve(Pairs(("PartnerCenter:ClientSecret", "v"), ("PiiEncryption:Key", "abc")));
        Assert.Contains("PiiEncryption:Key", r.Unmapped);
        Assert.DoesNotContain("PartnerCenter:ClientSecret", r.Unmapped);
    }

    [Fact]
    public void Blank_value_for_mapped_key_becomes_missing_value()
    {
        var r = new PrefixSecretResolver("lm-dev", Mappings)
            .Resolve(Pairs(("PartnerCenter:ClientSecret", "   ")));
        Assert.Empty(r.Planned);
        Assert.Contains("partnercenter-clientsecret", r.MissingValue);
    }

    [Fact]
    public void Mapping_with_no_key_in_values_becomes_missing_value()
    {
        var r = new PrefixSecretResolver("lm-dev", Mappings)
            .Resolve(Pairs(("PartnerCenter:ClientSecret", "v")));
        // sendgrid-apikey was never provided
        Assert.Contains("sendgrid-apikey", r.MissingValue);
        Assert.DoesNotContain("partnercenter-clientsecret", r.MissingValue);
    }

    [Fact]
    public void Invalid_resulting_name_goes_to_invalid()
    {
        var mappings = new Dictionary<string, string> { ["bad_suffix"] = "Some:Key" };
        var r = new PrefixSecretResolver("lm-dev", mappings)
            .Resolve(new[] { new KeyValuePair<string, string>("Some:Key", "value") });

        Assert.Contains("Some:Key", r.Invalid);
        Assert.Empty(r.Planned);
        Assert.DoesNotContain("bad_suffix", r.MissingValue);
    }
}
