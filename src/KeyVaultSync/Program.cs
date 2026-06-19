using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using KeyVaultSync;

const string usage = """
KeyVaultSync — sync appsettings into Azure Key Vault.

Commands:
  sync    --vault <uri> --file <appsettings.json>
  sync    --profile <kv.json> --values <appsettings.json> [--vault <uri>]
  mapping --vault <uri> --prefix <prefix> [--profile <kv.json>] [--report <out.json>]
""";

if (args.Length == 0)
{
    Console.Error.WriteLine(usage);
    return 1;
}

string verb = args[0];
string[] rest = args[1..];

switch (verb)
{
    case "sync":
        return await RunSync(rest);
    case "mapping":
        return await RunMapping(rest);
    default:
        Console.Error.WriteLine($"Unknown command '{verb}'.\n\n{usage}");
        return 1;
}

static string? GetArg(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static bool TryFlatten(string path, out IReadOnlyList<KeyValuePair<string, string>> pairs, out string error)
{
    try
    {
        pairs = JsonFlattener.Flatten(File.ReadAllText(path));
        error = "";
        return true;
    }
    catch (System.Text.Json.JsonException ex)
    {
        pairs = [];
        error = $"Error: invalid JSON in {path}: {ex.Message}";
        return false;
    }
}

static async Task<int> RunSync(string[] args)
{
    string? vault = GetArg(args, "--vault");
    string? profilePath = GetArg(args, "--profile");
    ISecretResolver resolver;
    IReadOnlyList<KeyValuePair<string, string>> pairs;

    if (profilePath is not null)
    {
        if (!File.Exists(profilePath))
        {
            Console.Error.WriteLine($"Error: profile file not found: {profilePath}");
            return 1;
        }

        PrefixProfile profile;
        try
        {
            profile = PrefixProfile.Load(profilePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Error: invalid profile {profilePath}: {ex.Message}");
            return 1;
        }

        string valuesPath = GetArg(args, "--values") ?? "appsettings.json";
        if (!File.Exists(valuesPath))
        {
            Console.Error.WriteLine($"Error: values file not found: {valuesPath}");
            return 1;
        }

        vault ??= profile.VaultUri;
        resolver = new PrefixSecretResolver(profile.Prefix, profile.SecretMappings);
        if (!TryFlatten(valuesPath, out pairs, out var err)) { Console.Error.WriteLine(err); return 1; }
    }
    else
    {
        string filePath = GetArg(args, "--file") ?? "appsettings.json";
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: input file not found: {filePath}");
            return 1;
        }
        resolver = new FlattenSecretResolver();
        if (!TryFlatten(filePath, out pairs, out var err)) { Console.Error.WriteLine(err); return 1; }
    }

    if (string.IsNullOrWhiteSpace(vault))
    {
        Console.Error.WriteLine("Error: vault URL not provided. Pass --vault or set KeyVault:VaultUri in the profile.");
        return 1;
    }

    var resolution = resolver.Resolve(pairs);
    var service = new KeyVaultSyncService(new KeyVaultSecretStore(new SecretClient(new Uri(vault), new DefaultAzureCredential())));

    SyncResult result;
    try
    {
        result = await service.SyncAsync(resolution.Planned);
    }
    catch (AuthenticationFailedException ex)
    {
        Console.Error.WriteLine($"Error: authentication failed. Run 'az login' or configure a managed identity.\n{ex.Message}");
        return 1;
    }
    catch (RequestFailedException ex) when (ex.Status is 401 or 403)
    {
        Console.Error.WriteLine($"Error: access denied (HTTP {ex.Status}). Your identity needs the 'Key Vault Secrets Officer' role on this vault.");
        return 1;
    }
    catch (RequestFailedException ex)
    {
        Console.Error.WriteLine($"Error: Key Vault request failed (HTTP {ex.Status}): {ex.Message}");
        return 1;
    }

    foreach (var e in result.Entries)
        Console.WriteLine($"  {e.Action,-7} {e.SecretName}");
    foreach (var key in resolution.Invalid)
        Console.WriteLine($"  INVALID {key}");

    if (resolution.Unmapped.Count > 0)
    {
        Console.WriteLine("\nUnmapped (not a managed secret):");
        foreach (var k in resolution.Unmapped) Console.WriteLine($"  {k}");
    }
    if (resolution.MissingValue.Count > 0)
    {
        Console.WriteLine("\nMissingValue (mapping defined, no value):");
        foreach (var k in resolution.MissingValue) Console.WriteLine($"  {k}");
    }

    Console.WriteLine(
        $"\nDone. Added: {result.Count(SyncAction.Added)}, " +
        $"Updated: {result.Count(SyncAction.Updated)}, " +
        $"Skipped: {result.Count(SyncAction.Skipped)}, " +
        $"Invalid: {resolution.Invalid.Count}");
    Console.WriteLine($"Unmapped: {resolution.Unmapped.Count}, MissingValue: {resolution.MissingValue.Count}");

    return resolution.Invalid.Count > 0 ? 2 : 0;
}

static async Task<int> RunMapping(string[] args)
{
    string? vault = GetArg(args, "--vault");
    string? prefix = GetArg(args, "--prefix");
    string? profilePath = GetArg(args, "--profile");
    string? reportPath = GetArg(args, "--report");

    if (string.IsNullOrWhiteSpace(vault))
    {
        Console.Error.WriteLine("Error: --vault is required for mapping.");
        return 1;
    }
    if (string.IsNullOrWhiteSpace(prefix))
    {
        Console.Error.WriteLine("Error: --prefix is required for mapping.");
        return 1;
    }

    IReadOnlyDictionary<string, string> mappings = new Dictionary<string, string>();
    if (profilePath is not null)
    {
        if (!File.Exists(profilePath))
        {
            Console.Error.WriteLine($"Error: profile file not found: {profilePath}");
            return 1;
        }
        try
        {
            mappings = PrefixProfile.Load(profilePath).SecretMappings;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Error: invalid profile {profilePath}: {ex.Message}");
            return 1;
        }
    }

    var mapper = new MappingService(new KeyVaultSecretStore(new SecretClient(new Uri(vault), new DefaultAzureCredential())));

    IReadOnlyDictionary<string, string> mapping;
    try
    {
        mapping = await mapper.BuildAsync(prefix, mappings);
    }
    catch (AuthenticationFailedException ex)
    {
        Console.Error.WriteLine($"Error: authentication failed. Run 'az login' or configure a managed identity.\n{ex.Message}");
        return 1;
    }
    catch (RequestFailedException ex) when (ex.Status is 401 or 403)
    {
        Console.Error.WriteLine($"Error: access denied (HTTP {ex.Status}). Your identity needs list/get permission on this vault.");
        return 1;
    }
    catch (RequestFailedException ex)
    {
        Console.Error.WriteLine($"Error: Key Vault request failed (HTTP {ex.Status}): {ex.Message}");
        return 1;
    }

    var json = JsonSerializer.Serialize(mapping, new JsonSerializerOptions { WriteIndented = true });

    if (reportPath is not null)
    {
        await File.WriteAllTextAsync(reportPath, json);
        Console.Error.WriteLine($"Wrote {mapping.Count} entries to {reportPath}");
    }
    else
    {
        Console.WriteLine(json);
    }

    return 0;
}
