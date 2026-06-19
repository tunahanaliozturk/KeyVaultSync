using KeyVaultSync;

namespace KeyVaultSync.Tests;

public sealed class FakeSecretLister : ISecretLister
{
    private readonly IReadOnlyList<string> _names;
    public FakeSecretLister(params string[] names) => _names = names;
    public Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default)
        => Task.FromResult(_names);
}
