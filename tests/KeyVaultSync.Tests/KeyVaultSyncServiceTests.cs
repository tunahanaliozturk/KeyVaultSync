using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class KeyVaultSyncServiceTests
{
    private static PlannedSecret Plan(string name, string value) => new(name, name, value);

    [Fact]
    public async Task Adds_missing_secret()
    {
        var store = new FakeSecretStore();
        var service = new KeyVaultSyncService(store);
        var result = await service.SyncAsync(new[] { Plan("New", "value") });
        Assert.Equal(1, result.Count(SyncAction.Added));
        Assert.Contains("New", store.Writes);
    }

    [Fact]
    public async Task Updates_changed_secret()
    {
        var store = new FakeSecretStore(new() { ["Key"] = "old" });
        var service = new KeyVaultSyncService(store);
        var result = await service.SyncAsync(new[] { Plan("Key", "new") });
        Assert.Equal(1, result.Count(SyncAction.Updated));
        Assert.Contains("Key", store.Writes);
    }

    [Fact]
    public async Task Skips_identical_secret_without_writing()
    {
        var store = new FakeSecretStore(new() { ["Key"] = "same" });
        var service = new KeyVaultSyncService(store);
        var result = await service.SyncAsync(new[] { Plan("Key", "same") });
        Assert.Equal(1, result.Count(SyncAction.Skipped));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Propagates_non_argument_exception_from_store()
    {
        var service = new KeyVaultSyncService(new ThrowingSecretStore());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SyncAsync(new[] { Plan("Key", "value") }));
    }

    private sealed class ThrowingSecretStore : ISecretStore
    {
        public Task<string?> GetValueAsync(string name, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
        public Task SetValueAsync(string name, string value, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
