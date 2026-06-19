using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class MappingServiceTests
{
    private static readonly Dictionary<string, string> Mappings = new()
    {
        ["db-connectionstring"] = "ConnectionStrings:DefaultConnection",
    };

    [Fact]
    public async Task Includes_only_prefix_scoped_secrets()
    {
        var lister = new FakeSecretLister(
            "lm-dev-db-connectionstring",
            "lm-dev-sendgrid-apikey",
            "passgate-other-secret");

        var result = await new MappingService(lister).BuildAsync("lm-dev", Mappings);

        Assert.False(result.ContainsValue("passgate-other-secret"));
        Assert.DoesNotContain("other-secret", result.Keys);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Maps_suffix_to_config_key_or_falls_back_to_suffix()
    {
        var lister = new FakeSecretLister("lm-dev-db-connectionstring", "lm-dev-sendgrid-apikey");

        var result = await new MappingService(lister).BuildAsync("lm-dev", Mappings);

        Assert.Equal("ConnectionStrings:DefaultConnection", result["db-connectionstring"]);
        Assert.Equal("sendgrid-apikey", result["sendgrid-apikey"]); // no mapping -> suffix as-is
    }

    [Fact]
    public async Task Skips_secret_equal_to_bare_prefix()
    {
        var lister = new FakeSecretLister("lm-dev-");
        var result = await new MappingService(lister).BuildAsync("lm-dev", Mappings);
        Assert.Empty(result);
    }
}
