using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class PrefixProfileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kvprofile-{Guid.NewGuid():N}.json");

    private void WriteProfile(string json) => File.WriteAllText(_path, json);

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Loads_vault_prefix_and_mappings()
    {
        WriteProfile("""
        {
          "KeyVault": {
            "VaultUri": "https://kv-passgate-dev.vault.azure.net",
            "Prefix": "lm-dev",
            "SecretMappings": {
              "db-connectionstring": "ConnectionStrings:DefaultConnection",
              "sendgrid-apikey": "SendGrid:APIKey"
            }
          }
        }
        """);

        var profile = PrefixProfile.Load(_path);

        Assert.Equal("https://kv-passgate-dev.vault.azure.net", profile.VaultUri);
        Assert.Equal("lm-dev-", profile.Prefix);
        Assert.Equal(2, profile.SecretMappings.Count);
        Assert.Equal("ConnectionStrings:DefaultConnection", profile.SecretMappings["db-connectionstring"]);
        Assert.Equal("SendGrid:APIKey", profile.SecretMappings["SENDGRID-APIKEY"]); // case-insensitive
    }

    [Fact]
    public void Throws_when_prefix_missing()
    {
        WriteProfile("""{ "KeyVault": { "VaultUri": "https://x.vault.azure.net" } }""");
        Assert.Throws<InvalidOperationException>(() => PrefixProfile.Load(_path));
    }

    [Fact]
    public void Throws_when_prefix_is_whitespace()
    {
        WriteProfile("""{ "KeyVault": { "Prefix": "   " } }""");
        Assert.Throws<InvalidOperationException>(() => PrefixProfile.Load(_path));
    }
}
