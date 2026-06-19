using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class FlattenSecretResolverTests
{
    private static IReadOnlyList<KeyValuePair<string, string>> Pairs(params (string Key, string Value)[] kv)
        => kv.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList();

    [Fact]
    public void Maps_colon_keys_to_double_dash()
    {
        var r = new FlattenSecretResolver().Resolve(Pairs(("ConnectionStrings:Default", "x")));
        Assert.Single(r.Planned);
        Assert.Equal("ConnectionStrings:Default", r.Planned[0].DisplayKey);
        Assert.Equal("ConnectionStrings--Default", r.Planned[0].SecretName);
        Assert.Equal("x", r.Planned[0].Value);
        Assert.Empty(r.Invalid);
    }

    [Fact]
    public void Records_invalid_key_as_invalid_and_continues()
    {
        var r = new FlattenSecretResolver().Resolve(Pairs(("Bad Key", "x"), ("Good", "y")));
        Assert.Contains("Bad Key", r.Invalid);
        Assert.Single(r.Planned);
        Assert.Equal("Good", r.Planned[0].SecretName);
    }

    [Fact]
    public void Unmapped_and_missing_are_always_empty()
    {
        var r = new FlattenSecretResolver().Resolve(Pairs(("A", "1")));
        Assert.Empty(r.Unmapped);
        Assert.Empty(r.MissingValue);
    }
}
