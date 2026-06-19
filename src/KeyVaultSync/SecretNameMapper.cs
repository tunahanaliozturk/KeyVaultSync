using System.Text.RegularExpressions;

namespace KeyVaultSync;

public static partial class SecretNameMapper
{
    public static string ToSecretName(string configKey)
    {
        var name = configKey.Replace(":", "--");
        if (!IsValid(name))
        {
            throw new ArgumentException(
                $"Config key '{configKey}' maps to invalid Key Vault secret name '{name}'. " +
                "Allowed characters: a-z, A-Z, 0-9 and '-' (max 127).");
        }
        return name;
    }

    public static string ToConfigKey(string secretName) => secretName.Replace("--", ":");

    public static bool IsValid(string secretName) =>
        secretName.Length is > 0 and <= 127 && ValidName().IsMatch(secretName);

    [GeneratedRegex("^[a-zA-Z0-9-]+$")]
    private static partial Regex ValidName();
}
