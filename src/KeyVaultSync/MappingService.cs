namespace KeyVaultSync;

public sealed class MappingService
{
    private readonly ISecretLister _lister;

    public MappingService(ISecretLister lister) => _lister = lister;

    public async Task<IReadOnlyDictionary<string, string>> BuildAsync(
        string prefix,
        IReadOnlyDictionary<string, string> mappings,
        CancellationToken ct = default)
    {
        var normalized = PrefixNaming.Normalize(prefix);
        var names = await _lister.ListNamesAsync(ct);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            if (!name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = name[normalized.Length..];
            if (string.IsNullOrEmpty(suffix))
            {
                continue;
            }

            result[suffix] = mappings.TryGetValue(suffix, out var configKey) ? configKey : suffix;
        }

        return result;
    }
}
