namespace KeyVaultSync;

public static class PrefixNaming
{
    public static string Normalize(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return prefix.EndsWith('-') ? prefix : prefix + "-";
    }
}
