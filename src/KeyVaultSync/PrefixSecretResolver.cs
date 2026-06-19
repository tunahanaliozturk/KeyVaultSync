namespace KeyVaultSync;

public sealed class PrefixSecretResolver : ISecretResolver
{
    private readonly string _prefix;
    private readonly IReadOnlyDictionary<string, string> _suffixToConfigKey;
    private readonly Dictionary<string, string> _configKeyToSuffix;

    public PrefixSecretResolver(string prefix, IReadOnlyDictionary<string, string> suffixToConfigKey)
    {
        _prefix = PrefixNaming.Normalize(prefix);
        _suffixToConfigKey = suffixToConfigKey;
        _configKeyToSuffix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (suffix, configKey) in suffixToConfigKey)
        {
            _configKeyToSuffix[configKey] = suffix;
        }
    }

    public ResolutionResult Resolve(IReadOnlyList<KeyValuePair<string, string>> flattened)
    {
        var planned = new List<PlannedSecret>();
        var invalid = new List<string>();
        var unmapped = new List<string>();
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in flattened)
        {
            if (!_configKeyToSuffix.TryGetValue(key, out var suffix))
            {
                unmapped.Add(key);
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue; // swept into MissingValue below
            }

            var name = _prefix + suffix;
            if (SecretNameMapper.IsValid(name))
            {
                planned.Add(new(key, name, value));
            }
            else
            {
                invalid.Add(key);
            }
            handled.Add(suffix);
        }

        var missingValue = _suffixToConfigKey.Keys
            .Where(s => !handled.Contains(s))
            .ToList();

        return new(planned, invalid, unmapped, missingValue);
    }
}
