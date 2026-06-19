namespace KeyVaultSync;

public interface ISecretLister
{
    Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default);
}
