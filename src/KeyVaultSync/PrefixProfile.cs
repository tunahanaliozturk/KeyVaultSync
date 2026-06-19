using Microsoft.Extensions.Configuration;

namespace KeyVaultSync;

public sealed class PrefixProfile
{
    public string? VaultUri { get; }
    public string Prefix { get; }
    public IReadOnlyDictionary<string, string> SecretMappings { get; }

    private PrefixProfile(string? vaultUri, string prefix, IReadOnlyDictionary<string, string> mappings)
    {
        VaultUri = vaultUri;
        Prefix = prefix;
        SecretMappings = mappings;
    }

    public static PrefixProfile Load(string path)
    {
        var full = Path.GetFullPath(path);
        var config = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(full)!)
            .AddJsonFile(Path.GetFileName(full), optional: false)
            .Build();

        var prefix = config["KeyVault:Prefix"];
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new InvalidOperationException($"Profile '{path}' is missing KeyVault:Prefix.");
        }

        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in config.GetSection("KeyVault:SecretMappings").GetChildren())
        {
            if (child.Value is not null)
            {
                mappings[child.Key] = child.Value;
            }
        }

        return new PrefixProfile(config["KeyVault:VaultUri"], PrefixNaming.Normalize(prefix), mappings);
    }
}
