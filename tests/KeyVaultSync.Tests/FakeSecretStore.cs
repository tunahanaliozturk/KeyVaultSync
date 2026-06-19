using KeyVaultSync;

namespace KeyVaultSync.Tests;

public sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _data;
    public List<string> Writes { get; } = new();

    public FakeSecretStore(Dictionary<string, string>? seed = null) => _data = seed ?? new();

    public Task<string?> GetValueAsync(string name, CancellationToken ct = default)
        => Task.FromResult(_data.TryGetValue(name, out var v) ? v : null);

    public Task SetValueAsync(string name, string value, CancellationToken ct = default)
    {
        _data[name] = value;
        Writes.Add(name);
        return Task.CompletedTask;
    }
}
