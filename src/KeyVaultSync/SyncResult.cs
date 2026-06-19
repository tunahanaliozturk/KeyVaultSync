namespace KeyVaultSync;

public enum SyncAction { Added, Updated, Skipped, Failed }

public sealed record SyncEntry(string Key, string SecretName, SyncAction Action, string? Error = null);

public sealed class SyncResult
{
    private readonly List<SyncEntry> _entries = new();

    public IReadOnlyList<SyncEntry> Entries => _entries;

    public void Add(SyncEntry entry) => _entries.Add(entry);

    public int Count(SyncAction action) => _entries.Count(e => e.Action == action);

    public bool HasFailures => _entries.Any(e => e.Action == SyncAction.Failed);
}
