# KeyVaultSync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A .NET console tool that reads an `appsettings.json` file and upserts every key-value pair into an Azure Key Vault (add if missing, update if changed, skip if identical).

**Architecture:** Pure helpers (`JsonFlattener`, `SecretNameMapper`) transform the JSON into Key Vault secret names. A `KeyVaultSyncService` performs read-compare-write through an `ISecretStore` interface, so the upsert logic is unit-testable against a fake. The real `KeyVaultSecretStore` wraps `SecretClient`. `Program` wires args, auth, and reporting.

**Tech Stack:** C# / .NET 10, `Azure.Security.KeyVault.Secrets`, `Azure.Identity`, `Microsoft.Extensions.Configuration.Json`, xUnit.

## Global Constraints

- Target framework: `net10.0`. Nullable reference types: `enable`. Implicit usings: `enable`.
- Repo root is the `KeyVaultSync` repository (already `git init`'d). All paths below are relative to that root.
- Key Vault secret names allow only `a-zA-Z0-9-`, max length 127. Config `:` separators map to `--`.
- Auth uses `DefaultAzureCredential` only — no credentials stored in the tool.
- Exit codes: all-success `0`; fatal input/auth error `1`; partial failure (some keys had invalid names) `2`.
- Every code step shows the full file content or the exact region to change. TDD: test first, watch it fail, implement, watch it pass, commit.

---

### Task 1: Solution scaffold + SyncResult

**Files:**
- Create: `KeyVaultSync.sln`
- Create: `src/KeyVaultSync/KeyVaultSync.csproj`
- Create: `src/KeyVaultSync/SyncResult.cs`
- Create: `tests/KeyVaultSync.Tests/KeyVaultSync.Tests.csproj`
- Create: `tests/KeyVaultSync.Tests/SyncResultTests.cs`
- Create: `.gitignore`

**Interfaces:**
- Produces: `enum SyncAction { Added, Updated, Skipped, Failed }`; `record SyncEntry(string Key, string SecretName, SyncAction Action, string? Error = null)`; `class SyncResult` with `void Add(SyncEntry)`, `IReadOnlyList<SyncEntry> Entries`, `int Count(SyncAction)`, `bool HasFailures`.

- [ ] **Step 1: Scaffold projects**

Run:
```bash
dotnet new console -n KeyVaultSync -o src/KeyVaultSync -f net10.0
dotnet new xunit -n KeyVaultSync.Tests -o tests/KeyVaultSync.Tests -f net10.0
dotnet new sln -n KeyVaultSync
dotnet sln add src/KeyVaultSync/KeyVaultSync.csproj tests/KeyVaultSync.Tests/KeyVaultSync.Tests.csproj
dotnet add tests/KeyVaultSync.Tests/KeyVaultSync.Tests.csproj reference src/KeyVaultSync/KeyVaultSync.csproj
dotnet add src/KeyVaultSync/KeyVaultSync.csproj package Azure.Security.KeyVault.Secrets
dotnet add src/KeyVaultSync/KeyVaultSync.csproj package Azure.Identity
dotnet add src/KeyVaultSync/KeyVaultSync.csproj package Microsoft.Extensions.Configuration.Json
```

- [ ] **Step 2: Create `.gitignore`**

Create `.gitignore`:
```gitignore
bin/
obj/
*.user
.vs/
```

- [ ] **Step 3: Ensure `KeyVaultSync.csproj` has nullable + implicit usings enabled**

Open `src/KeyVaultSync/KeyVaultSync.csproj` and confirm the `<PropertyGroup>` contains (the `dotnet new` template already adds these for net10.0 — add any that are missing):
```xml
<TargetFramework>net10.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
```

- [ ] **Step 4: Write the failing test**

Create `tests/KeyVaultSync.Tests/SyncResultTests.cs`:
```csharp
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
```

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL — `SyncResult` / `SyncAction` / `SyncEntry` do not exist (build error).

- [ ] **Step 6: Implement `SyncResult.cs`**

Create `src/KeyVaultSync/SyncResult.cs`:
```csharp
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
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS (1 test).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: scaffold solution and SyncResult"
```

---

### Task 2: JsonFlattener

**Files:**
- Create: `src/KeyVaultSync/JsonFlattener.cs`
- Create: `tests/KeyVaultSync.Tests/JsonFlattenerTests.cs`

**Interfaces:**
- Produces: `static class JsonFlattener` with `static IReadOnlyList<KeyValuePair<string,string>> Flatten(string json)`. Nested objects join keys with `:`. Arrays expand to `:0`, `:1`. Null leaves are skipped. String leaves emit their unquoted value; numbers and booleans emit their JSON text (`true`/`false`/`42`).

- [ ] **Step 1: Write the failing test**

Create `tests/KeyVaultSync.Tests/JsonFlattenerTests.cs`:
```csharp
using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class JsonFlattenerTests
{
    [Fact]
    public void Flattens_nested_objects_with_colon()
    {
        var json = """{ "ConnectionStrings": { "Default": "Server=." } }""";
        var pairs = JsonFlattener.Flatten(json);
        Assert.Contains(new KeyValuePair<string, string>("ConnectionStrings:Default", "Server=."), pairs);
    }

    [Fact]
    public void Expands_arrays_with_index()
    {
        var json = """{ "Hosts": ["a", "b"] }""";
        var pairs = JsonFlattener.Flatten(json);
        Assert.Contains(new KeyValuePair<string, string>("Hosts:0", "a"), pairs);
        Assert.Contains(new KeyValuePair<string, string>("Hosts:1", "b"), pairs);
    }

    [Fact]
    public void Emits_numbers_and_bools_as_json_text()
    {
        var json = """{ "Port": 8080, "Enabled": true }""";
        var pairs = JsonFlattener.Flatten(json);
        Assert.Contains(new KeyValuePair<string, string>("Port", "8080"), pairs);
        Assert.Contains(new KeyValuePair<string, string>("Enabled", "true"), pairs);
    }

    [Fact]
    public void Skips_null_values()
    {
        var json = """{ "A": null, "B": "x" }""";
        var pairs = JsonFlattener.Flatten(json);
        Assert.DoesNotContain(pairs, p => p.Key == "A");
        Assert.Single(pairs);
    }

    [Fact]
    public void Empty_object_yields_no_pairs()
    {
        Assert.Empty(JsonFlattener.Flatten("{}"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL — `JsonFlattener` does not exist.

- [ ] **Step 3: Implement `JsonFlattener.cs`**

Create `src/KeyVaultSync/JsonFlattener.cs`:
```csharp
using System.Text.Json;

namespace KeyVaultSync;

public static class JsonFlattener
{
    public static IReadOnlyList<KeyValuePair<string, string>> Flatten(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<KeyValuePair<string, string>>();
        Walk(doc.RootElement, "", result);
        return result;
    }

    private static void Walk(JsonElement element, string prefix, List<KeyValuePair<string, string>> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = prefix.Length == 0 ? prop.Name : $"{prefix}:{prop.Name}";
                    Walk(prop.Value, key, result);
                }
                break;

            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, $"{prefix}:{i}", result);
                    i++;
                }
                break;

            case JsonValueKind.String:
                result.Add(new(prefix, element.GetString() ?? ""));
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                result.Add(new(prefix, element.GetRawText()));
                break;

            case JsonValueKind.Null:
            default:
                break;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS (all JsonFlattener tests + earlier tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add JsonFlattener"
```

---

### Task 3: SecretNameMapper

**Files:**
- Create: `src/KeyVaultSync/SecretNameMapper.cs`
- Create: `tests/KeyVaultSync.Tests/SecretNameMapperTests.cs`

**Interfaces:**
- Produces: `static class SecretNameMapper` with `static string ToSecretName(string configKey)` (`:` -> `--`, throws `ArgumentException` if the result is not a valid Key Vault name), `static string ToConfigKey(string secretName)` (`--` -> `:`), `static bool IsValid(string secretName)`.

- [ ] **Step 1: Write the failing test**

Create `tests/KeyVaultSync.Tests/SecretNameMapperTests.cs`:
```csharp
using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class SecretNameMapperTests
{
    [Fact]
    public void Maps_colon_to_double_dash()
    {
        Assert.Equal("ConnectionStrings--Default", SecretNameMapper.ToSecretName("ConnectionStrings:Default"));
    }

    [Fact]
    public void Round_trips_back_to_config_key()
    {
        var secret = SecretNameMapper.ToSecretName("A:B:C");
        Assert.Equal("A:B:C", SecretNameMapper.ToConfigKey(secret));
    }

    [Fact]
    public void Leaves_simple_keys_unchanged()
    {
        Assert.Equal("Port", SecretNameMapper.ToSecretName("Port"));
    }

    [Theory]
    [InlineData("Has Space")]
    [InlineData("Under_score")]
    [InlineData("Dot.Key")]
    public void Rejects_invalid_characters(string configKey)
    {
        Assert.Throws<ArgumentException>(() => SecretNameMapper.ToSecretName(configKey));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL — `SecretNameMapper` does not exist.

- [ ] **Step 3: Implement `SecretNameMapper.cs`**

Create `src/KeyVaultSync/SecretNameMapper.cs`:
```csharp
using System.Text.RegularExpressions;

namespace KeyVaultSync;

public static partial class SecretNameMapper
{
    public static string ToSecretName(string configKey)
    {
        var name = configKey.Replace(":", "--");
        if (!IsValid(name))
        {
            throw new ArgumentException(
                $"Config key '{configKey}' maps to invalid Key Vault secret name '{name}'. " +
                "Allowed characters: a-z, A-Z, 0-9 and '-' (max 127).");
        }
        return name;
    }

    public static string ToConfigKey(string secretName) => secretName.Replace("--", ":");

    public static bool IsValid(string secretName) =>
        secretName.Length is > 0 and <= 127 && ValidName().IsMatch(secretName);

    [GeneratedRegex("^[a-zA-Z0-9-]+$")]
    private static partial Regex ValidName();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add SecretNameMapper"
```

---

### Task 4: ISecretStore + KeyVaultSyncService

**Files:**
- Create: `src/KeyVaultSync/ISecretStore.cs`
- Create: `src/KeyVaultSync/KeyVaultSyncService.cs`
- Create: `tests/KeyVaultSync.Tests/FakeSecretStore.cs`
- Create: `tests/KeyVaultSync.Tests/KeyVaultSyncServiceTests.cs`

**Interfaces:**
- Produces: `interface ISecretStore { Task<string?> GetValueAsync(string name, CancellationToken ct = default); Task SetValueAsync(string name, string value, CancellationToken ct = default); }`.
- Produces: `class KeyVaultSyncService(ISecretStore store)` with `Task<SyncResult> SyncAsync(IEnumerable<KeyValuePair<string,string>> pairs, CancellationToken ct = default)`.
- Consumes: `SecretNameMapper.ToSecretName`, `SyncResult`, `SyncEntry`, `SyncAction` from Tasks 1 and 3.
- Behavior contract: invalid secret names are caught and recorded as `Failed` (processing continues). Any other exception from `ISecretStore` (e.g. Azure 401/403) is **not** caught here — it propagates to the caller. A `Skipped` entry must not trigger a write.

- [ ] **Step 1: Write the failing test**

Create `tests/KeyVaultSync.Tests/FakeSecretStore.cs`:
```csharp
using KeyVaultSync;

namespace KeyVaultSync.Tests;

public sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _data;
    public List<string> Writes { get; } = new();

    public FakeSecretStore(Dictionary<string, string>? seed = null) => _data = seed ?? new();

    public Task<string?> GetValueAsync(string name, CancellationToken ct = default)
        => Task.FromResult(_data.TryGetValue(name, out var v) ? v : null);

    public Task SetValueAsync(string name, string value, CancellationToken ct = default)
    {
        _data[name] = value;
        Writes.Add(name);
        return Task.CompletedTask;
    }
}
```

Create `tests/KeyVaultSync.Tests/KeyVaultSyncServiceTests.cs`:
```csharp
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
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL — `ISecretStore` / `KeyVaultSyncService` do not exist.

- [ ] **Step 3: Implement `ISecretStore.cs`**

Create `src/KeyVaultSync/ISecretStore.cs`:
```csharp
namespace KeyVaultSync;

public interface ISecretStore
{
    Task<string?> GetValueAsync(string name, CancellationToken ct = default);
    Task SetValueAsync(string name, string value, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement `KeyVaultSyncService.cs`**

Create `src/KeyVaultSync/KeyVaultSyncService.cs`:
```csharp
namespace KeyVaultSync;

public sealed class KeyVaultSyncService
{
    private readonly ISecretStore _store;

    public KeyVaultSyncService(ISecretStore store) => _store = store;

    public async Task<SyncResult> SyncAsync(
        IEnumerable<KeyValuePair<string, string>> pairs,
        CancellationToken ct = default)
    {
        var result = new SyncResult();

        foreach (var (key, value) in pairs)
        {
            string secretName;
            try
            {
                secretName = SecretNameMapper.ToSecretName(key);
            }
            catch (ArgumentException ex)
            {
                result.Add(new(key, key, SyncAction.Failed, ex.Message));
                continue;
            }

            var existing = await _store.GetValueAsync(secretName, ct);
            if (existing is null)
            {
                await _store.SetValueAsync(secretName, value, ct);
                result.Add(new(key, secretName, SyncAction.Added));
            }
            else if (existing != value)
            {
                await _store.SetValueAsync(secretName, value, ct);
                result.Add(new(key, secretName, SyncAction.Updated));
            }
            else
            {
                result.Add(new(key, secretName, SyncAction.Skipped));
            }
        }

        return result;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS (all service tests + earlier tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add ISecretStore and KeyVaultSyncService"
```

---

### Task 5: KeyVaultSecretStore (real Azure wrapper)

**Files:**
- Create: `src/KeyVaultSync/KeyVaultSecretStore.cs`

**Interfaces:**
- Consumes: `ISecretStore` (Task 4), `SecretClient` from `Azure.Security.KeyVault.Secrets`.
- Produces: `class KeyVaultSecretStore(SecretClient client) : ISecretStore`. `GetValueAsync` returns `null` on HTTP 404 (secret not found) and lets all other `RequestFailedException`s propagate.

> This wrapper makes live network calls and is verified manually in Task 6, not by a unit test. The 404-to-null translation is the one piece of logic; keep it minimal.

- [ ] **Step 1: Implement `KeyVaultSecretStore.cs`**

Create `src/KeyVaultSync/KeyVaultSecretStore.cs`:
```csharp
using Azure;
using Azure.Security.KeyVault.Secrets;

namespace KeyVaultSync;

public sealed class KeyVaultSecretStore : ISecretStore
{
    private readonly SecretClient _client;

    public KeyVaultSecretStore(SecretClient client) => _client = client;

    public async Task<string?> GetValueAsync(string name, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetSecretAsync(name, cancellationToken: ct);
            return response.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task SetValueAsync(string name, string value, CancellationToken ct = default)
        => _client.SetSecretAsync(name, value, ct);
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: add KeyVaultSecretStore wrapping SecretClient"
```

---

### Task 6: Program (CLI), config, README, end-to-end run

**Files:**
- Modify: `src/KeyVaultSync/Program.cs` (replace template content entirely)
- Create: `src/KeyVaultSync/appsettings.json`
- Modify: `src/KeyVaultSync/KeyVaultSync.csproj` (copy appsettings.json to output)
- Create: `README.md`

**Interfaces:**
- Consumes: `JsonFlattener.Flatten`, `KeyVaultSyncService`, `KeyVaultSecretStore`, `SyncResult`, `SyncAction` from earlier tasks; `DefaultAzureCredential`, `SecretClient`.
- CLI: `KeyVaultSync --vault <url> --file <path>`. `--vault` falls back to `KeyVault:Url` in the tool's own `appsettings.json`. `--file` defaults to `appsettings.json` in the current directory.

- [ ] **Step 1: Create `src/KeyVaultSync/appsettings.json`**

```json
{
  "KeyVault": {
    "Url": ""
  }
}
```

- [ ] **Step 2: Make `appsettings.json` copy to output**

In `src/KeyVaultSync/KeyVaultSync.csproj`, add this `ItemGroup` inside the `<Project>` element:
```xml
<ItemGroup>
  <None Update="appsettings.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 3: Replace `src/KeyVaultSync/Program.cs`**

Replace the entire file content with:
```csharp
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
```

- [ ] **Step 4: Build and run the unit suite**

Run: `dotnet test`
Expected: PASS (all tests).

- [ ] **Step 5: Verify CLI argument errors without Azure**

Run (missing vault):
```bash
dotnet run --project src/KeyVaultSync -- --file ./does-not-exist.json
```
Expected: prints the vault-URL error and exits with code 1 (because the empty config URL is blank). Confirm with `echo $?` (bash) / `$LASTEXITCODE` (PowerShell) → `1`.

- [ ] **Step 6: Create `README.md`**

```markdown
# KeyVaultSync

Console tool that syncs every key-value pair from an `appsettings.json` file into
an Azure Key Vault: adds missing secrets, updates changed ones, skips identical ones.

Nested keys are flattened (`ConnectionStrings:Default`) and `:` is converted to `--`
for Key Vault (`ConnectionStrings--Default`), which .NET configuration reads back
automatically.

## Usage

```bash
KeyVaultSync --vault https://myvault.vault.azure.net --file ./appsettings.json
```

- `--vault` — Key Vault URL. If omitted, read from `KeyVault:Url` in the tool's own `appsettings.json`.
- `--file` — input JSON file. Defaults to `appsettings.json` in the current directory.

## Authentication

Uses `DefaultAzureCredential`:

- Local: run `az login`.
- CI/CD: use a managed identity or `AZURE_*` environment variables.

The identity needs the **Key Vault Secrets Officer** role on the target vault.

## Exit codes

- `0` — all keys processed successfully.
- `1` — fatal error (bad input file, missing vault URL, auth/authorization failure).
- `2` — partial failure (some keys had names invalid for Key Vault).
```

- [ ] **Step 7: Live verification against a real vault (manual)**

Prerequisites: `az login`, and the signed-in identity has **Key Vault Secrets Officer** on a test vault.

Create `sample.json`:
```json
{ "ConnectionStrings": { "Default": "Server=." }, "Feature": { "Enabled": true } }
```

Run twice:
```bash
dotnet run --project src/KeyVaultSync -- --vault https://<your-vault>.vault.azure.net --file ./sample.json
dotnet run --project src/KeyVaultSync -- --vault https://<your-vault>.vault.azure.net --file ./sample.json
```
Expected: first run reports `Added` for each key; second run reports `Skipped` for each (values unchanged). Verify in the Azure portal that secret `ConnectionStrings--Default` exists.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add CLI program, config, and README"
```

---

## Notes for the implementer

- Run every `dotnet` command from the repo root.
- The only intentional deviation from the spec: the spec said "mock `SecretClient`"; we introduced `ISecretStore` instead so the upsert logic is testable against a plain fake (mocking the sealed Azure client is impractical). The real client is wrapped by `KeyVaultSecretStore` and verified live in Task 6.
- Auth/authorization (401/403) is a systemic failure handled once in `Program` (exit 1), not per-key — so exit code 2 is reserved strictly for invalid secret names.
