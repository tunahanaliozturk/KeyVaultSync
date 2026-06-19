namespace KeyVaultSync;

public sealed class FlattenSecretResolver : ISecretResolver
{
    public ResolutionResult Resolve(IReadOnlyList<KeyValuePair<string, string>> flattened)
    {
        var planned = new List<PlannedSecret>();
        var invalid = new List<string>();

        foreach (var (key, value) in flattened)
        {
            try
            {
                var name = SecretNameMapper.ToSecretName(key);
                planned.Add(new(key, name, value));
            }
            catch (ArgumentException)
            {
                invalid.Add(key);
            }
        }

        return new(planned, invalid, [], []);
    }
}
