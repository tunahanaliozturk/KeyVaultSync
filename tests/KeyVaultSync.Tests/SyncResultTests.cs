using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class SyncResultTests
{
    [Fact]
    public void Counts_entries_by_action()
    {
        var result = new SyncResult();
        result.Add(new("A", "A", SyncAction.Added));
        result.Add(new("B", "B", SyncAction.Updated));
        result.Add(new("C", "C", SyncAction.Skipped));
        result.Add(new("D", "D", SyncAction.Added));

        Assert.Equal(2, result.Count(SyncAction.Added));
        Assert.Equal(1, result.Count(SyncAction.Updated));
        Assert.Equal(1, result.Count(SyncAction.Skipped));
        Assert.Equal(4, result.Entries.Count);
    }
}
