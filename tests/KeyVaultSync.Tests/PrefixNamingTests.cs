using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class PrefixNamingTests
{
    [Fact]
    public void Appends_trailing_dash_when_missing()
    {
        Assert.Equal("lm-dev-", PrefixNaming.Normalize("lm-dev"));
    }

    [Fact]
    public void Leaves_existing_trailing_dash()
    {
        Assert.Equal("lm-dev-", PrefixNaming.Normalize("lm-dev-"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_prefix(string prefix)
    {
        Assert.Throws<ArgumentException>(() => PrefixNaming.Normalize(prefix));
    }
}
