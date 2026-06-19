using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class SecretNameMapperTests
{
    [Fact]
    public void Maps_colon_to_double_dash()
    {
        Assert.Equal("ConnectionStrings--Default", SecretNameMapper.ToSecretName("ConnectionStrings:Default"));
    }

    [Fact]
    public void Round_trips_back_to_config_key()
    {
        var secret = SecretNameMapper.ToSecretName("A:B:C");
        Assert.Equal("A:B:C", SecretNameMapper.ToConfigKey(secret));
    }

    [Fact]
    public void Leaves_simple_keys_unchanged()
    {
        Assert.Equal("Port", SecretNameMapper.ToSecretName("Port"));
    }

    [Theory]
    [InlineData("Has Space")]
    [InlineData("Under_score")]
    [InlineData("Dot.Key")]
    public void Rejects_invalid_characters(string configKey)
    {
        Assert.Throws<ArgumentException>(() => SecretNameMapper.ToSecretName(configKey));
    }
}
