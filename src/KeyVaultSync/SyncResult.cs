namespace KeyVaultSync;

public enum SyncAction { Added, Updated, Skipped }

public sealed record SyncEntry(string DisplayKey, string SecretName, SyncAction Action);

public sealed class SyncResult
{
    private readonly List<SyncEntry> _entries = new();

    public IReadOnlyList<SyncEntry> Entries => _entries;

    public void Add(SyncEntry entry) => _entries.Add(entry);

    public int Count(SyncAction action) => _entries.Count(e => e.Action == action);
}
