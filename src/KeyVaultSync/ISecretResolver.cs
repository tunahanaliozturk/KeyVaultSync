namespace KeyVaultSync;

public sealed record PlannedSecret(string DisplayKey, string SecretName, string Value);

public sealed record ResolutionResult(
    IReadOnlyList<PlannedSecret> Planned,
    IReadOnlyList<string> Invalid,
    IReadOnlyList<string> Unmapped,
    IReadOnlyList<string> MissingValue);

public interface ISecretResolver
{
    ResolutionResult Resolve(IReadOnlyList<KeyValuePair<string, string>> flattened);
}
