using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using KeyVaultSync;
using Microsoft.Extensions.Configuration;

string? vault = GetArg(args, "--vault");
string file = GetArg(args, "--file") ?? "appsettings.json";

if (string.IsNullOrWhiteSpace(vault))
{
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .Build();
    vault = config["KeyVault:Url"];
}

if (string.IsNullOrWhiteSpace(vault))
{
    Console.Error.WriteLine(
        "Error: vault URL not provided. Pass --vault https://<name>.vault.azure.net " +
        "or set KeyVault:Url in appsettings.json.");
    return 1;
}

if (!File.Exists(file))
{
    Console.Error.WriteLine($"Error: input file not found: {file}");
    return 1;
}

IReadOnlyList<KeyValuePair<string, string>> pairs;
try
{
    var json = await File.ReadAllTextAsync(file);
    pairs = JsonFlattener.Flatten(json);
}
catch (System.Text.Json.JsonException ex)
{
    Console.Error.WriteLine($"Error: invalid JSON in {file}: {ex.Message}");
    return 1;
}

var client = new SecretClient(new Uri(vault), new DefaultAzureCredential());
var service = new KeyVaultSyncService(new KeyVaultSecretStore(client));

SyncResult result;
try
{
    result = await service.SyncAsync(pairs);
}
catch (AuthenticationFailedException ex)
{
    Console.Error.WriteLine(
        $"Error: authentication failed. Run 'az login' or configure a managed identity.\n{ex.Message}");
    return 1;
}
catch (RequestFailedException ex) when (ex.Status is 401 or 403)
{
    Console.Error.WriteLine(
        $"Error: access denied (HTTP {ex.Status}). Your identity needs the " +
        "'Key Vault Secrets Officer' role on this vault.");
    return 1;
}
catch (RequestFailedException ex)
{
    Console.Error.WriteLine($"Error: Key Vault request failed (HTTP {ex.Status}): {ex.Message}");
    return 1;
}

foreach (var e in result.Entries)
{
    var line = e.Action == SyncAction.Failed
        ? $"  FAILED  {e.Key} -> {e.Error}"
        : $"  {e.Action,-7} {e.Key}";
    Console.WriteLine(line);
}

Console.WriteLine(
    $"\nDone. Added: {result.Count(SyncAction.Added)}, " +
    $"Updated: {result.Count(SyncAction.Updated)}, " +
    $"Skipped: {result.Count(SyncAction.Skipped)}, " +
    $"Failed: {result.Count(SyncAction.Failed)}");

return result.HasFailures ? 2 : 0;

static string? GetArg(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}
