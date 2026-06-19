using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class KeyVaultSyncServiceTests
{
    private static KeyValuePair<string, string> Pair(string k, string v) => new(k, v);

    [Fact]
    public async Task Adds_missing_secret()
    {
        var store = new FakeSecretStore();
        var service = new KeyVaultSyncService(store);

        var result = await service.SyncAsync(new[] { Pair("New", "value") });

        Assert.Equal(1, result.Count(SyncAction.Added));
        Assert.Contains("New", store.Writes);
    }

    [Fact]
    public async Task Updates_changed_secret()
    {
        var store = new FakeSecretStore(new() { ["Key"] = "old" });
        var service = new KeyVaultSyncService(store);

        var result = await service.SyncAsync(new[] { Pair("Key", "new") });

        Assert.Equal(1, result.Count(SyncAction.Updated));
        Assert.Contains("Key", store.Writes);
    }

    [Fact]
    public async Task Skips_identical_secret_without_writing()
    {
        var store = new FakeSecretStore(new() { ["Key"] = "same" });
        var service = new KeyVaultSyncService(store);

        var result = await service.SyncAsync(new[] { Pair("Key", "same") });

        Assert.Equal(1, result.Count(SyncAction.Skipped));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Maps_colon_keys_to_double_dash_secret_names()
    {
        var store = new FakeSecretStore();
        var service = new KeyVaultSyncService(store);

        await service.SyncAsync(new[] { Pair("ConnectionStrings:Default", "x") });

        Assert.Contains("ConnectionStrings--Default", store.Writes);
    }

    [Fact]
    public async Task Records_invalid_key_as_failed_and_continues()
    {
        var store = new FakeSecretStore();
        var service = new KeyVaultSyncService(store);

        var result = await service.SyncAsync(new[] { Pair("Bad Key", "x"), Pair("Good", "y") });

        Assert.Equal(1, result.Count(SyncAction.Failed));
        Assert.Equal(1, result.Count(SyncAction.Added));
        Assert.Contains("Good", store.Writes);
    }

    [Fact]
    public async Task Propagates_non_argument_exception_from_store()
    {
        var service = new KeyVaultSyncService(new ThrowingSecretStore());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SyncAsync(new[] { new KeyValuePair<string, string>("Key", "value") }));
    }

    private sealed class ThrowingSecretStore : ISecretStore
    {
        public Task<string?> GetValueAsync(string name, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
        public Task SetValueAsync(string name, string value, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
