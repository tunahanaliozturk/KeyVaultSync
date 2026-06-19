using Azure;
using Azure.Security.KeyVault.Secrets;

namespace KeyVaultSync;

public sealed class KeyVaultSecretStore : ISecretStore
{
    private readonly SecretClient _client;

    public KeyVaultSecretStore(SecretClient client) => _client = client;

    public async Task<string?> GetValueAsync(string name, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetSecretAsync(name, cancellationToken: ct);
            return response.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task SetValueAsync(string name, string value, CancellationToken ct = default)
        => _client.SetSecretAsync(name, value, ct);
}
