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

    [Fact]
    public void ToConfigKey_leaves_single_hyphen_unchanged()
    {
        Assert.Equal("my-secret", SecretNameMapper.ToConfigKey("my-secret"));
    }

    [Fact]
    public void IsValid_accepts_name_at_max_length_127()
    {
        Assert.True(SecretNameMapper.IsValid(new string('a', 127)));
    }

    [Fact]
    public void IsValid_rejects_name_over_max_length()
    {
        Assert.False(SecretNameMapper.IsValid(new string('a', 128)));
    }
}
