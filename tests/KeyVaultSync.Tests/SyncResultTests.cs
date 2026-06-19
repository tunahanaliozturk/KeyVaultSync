using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class SyncResultTests
{
    [Fact]
    public void Counts_entries_by_action_and_reports_failures()
    {
        var result = new SyncResult();
        result.Add(new("A", "A", SyncAction.Added));
        result.Add(new("B", "B", SyncAction.Updated));
        result.Add(new("C", "C", SyncAction.Skipped));
        result.Add(new("D:x", "D:x", SyncAction.Failed, "bad name"));

        Assert.Equal(1, result.Count(SyncAction.Added));
        Assert.Equal(1, result.Count(SyncAction.Updated));
        Assert.Equal(1, result.Count(SyncAction.Skipped));
        Assert.Equal(1, result.Count(SyncAction.Failed));
        Assert.True(result.HasFailures);
        Assert.Equal(4, result.Entries.Count);
    }
}
