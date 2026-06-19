namespace KeyVaultSync;

public interface ISecretStore
{
    Task<string?> GetValueAsync(string name, CancellationToken ct = default);
    Task SetValueAsync(string name, string value, CancellationToken ct = default);
}
