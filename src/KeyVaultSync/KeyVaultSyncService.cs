namespace KeyVaultSync;

public sealed class KeyVaultSyncService
{
    private readonly ISecretStore _store;

    public KeyVaultSyncService(ISecretStore store) => _store = store;

    public async Task<SyncResult> SyncAsync(
        IReadOnlyList<PlannedSecret> planned,
        CancellationToken ct = default)
    {
        var result = new SyncResult();

        foreach (var p in planned)
        {
            var existing = await _store.GetValueAsync(p.SecretName, ct);
            if (existing is null)
            {
                await _store.SetValueAsync(p.SecretName, p.Value, ct);
                result.Add(new(p.DisplayKey, p.SecretName, SyncAction.Added));
            }
            else if (existing != p.Value)
            {
                await _store.SetValueAsync(p.SecretName, p.Value, ct);
                result.Add(new(p.DisplayKey, p.SecretName, SyncAction.Updated));
            }
            else
            {
                result.Add(new(p.DisplayKey, p.SecretName, SyncAction.Skipped));
            }
        }

        return result;
    }
}
