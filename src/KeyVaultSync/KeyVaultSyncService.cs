namespace KeyVaultSync;

public sealed class KeyVaultSyncService
{
    private readonly ISecretStore _store;

    public KeyVaultSyncService(ISecretStore store) => _store = store;

    public async Task<SyncResult> SyncAsync(
        IEnumerable<KeyValuePair<string, string>> pairs,
        CancellationToken ct = default)
    {
        var result = new SyncResult();

        foreach (var (key, value) in pairs)
        {
            string secretName;
            try
            {
                secretName = SecretNameMapper.ToSecretName(key);
            }
            catch (ArgumentException ex)
            {
                result.Add(new(key, key, SyncAction.Failed, ex.Message));
                continue;
            }

            var existing = await _store.GetValueAsync(secretName, ct);
            if (existing is null)
            {
                await _store.SetValueAsync(secretName, value, ct);
                result.Add(new(key, secretName, SyncAction.Added));
            }
            else if (existing != value)
            {
                await _store.SetValueAsync(secretName, value, ct);
                result.Add(new(key, secretName, SyncAction.Updated));
            }
            else
            {
                result.Add(new(key, secretName, SyncAction.Skipped));
            }
        }

        return result;
    }
}
