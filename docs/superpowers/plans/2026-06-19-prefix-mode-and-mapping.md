# KeyVaultSync v2 — Prefix Mode + Mapping Command — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a prefix-based sync mode (push values into `{prefix}-{suffix}` secrets via a reversed `SecretMappings` table) and a read-only `mapping` command that emits a `SecretMappings`-shaped JSON for a vault's prefix-scoped secrets.

**Architecture:** Extract naming/filtering from `KeyVaultSyncService` into an `ISecretResolver` (Mode A: `FlattenSecretResolver`; Mode B: `PrefixSecretResolver`). The service becomes a pure upsert engine over `PlannedSecret`. A segregated `ISecretLister` + `MappingService` power the read-only `mapping` command. `Program` routes two verbs: `sync` and `mapping`.

**Tech Stack:** C# / .NET 10, `Azure.Security.KeyVault.Secrets`, `Azure.Identity`, `Microsoft.Extensions.Configuration.Json`, xUnit.

## Global Constraints

- Target framework `net10.0`; Nullable `enable`; Implicit usings `enable`.
- Repo root is the `KeyVaultSync` repository; all paths relative to it. Run every `dotnet` command from the repo root.
- A root `nuget.config` pins restore to nuget.org — leave it alone. No new NuGet packages.
- Key Vault secret names allow only `a-zA-Z0-9-`, max length 127.
- Prefix normalization: append a trailing `-` if absent (mirrors the app's `PrefixKeyVaultSecretManager.NormalizePrefix`).
- Suffix↔configKey and prefix matching are `OrdinalIgnoreCase` (mirrors the app's `FrozenDictionary` + `StartsWith`).
- Auth: `DefaultAzureCredential` only — no stored credentials.
- `sync` exit codes: `0` success; `1` fatal (bad/missing input, missing vault, auth/authorization failure); `2` partial (≥1 `Invalid` secret name). `mapping` exit codes: `0` success; `1` fatal. No `2` for mapping.
- Both commands operate strictly on `{prefix}-*` names (prefix isolation in a shared vault).
- TDD: write the failing test, watch it fail, implement, watch it pass, commit.

---

### Task 1: PrefixNaming helper

**Files:**
- Create: `src/KeyVaultSync/PrefixNaming.cs`
- Create: `tests/KeyVaultSync.Tests/PrefixNamingTests.cs`

**Interfaces:**
- Produces: `static class PrefixNaming` with `static string Normalize(string prefix)` — appends a trailing `-` if absent; throws `ArgumentException` on null/empty/whitespace.

- [ ] **Step 1: Write the failing test**

Create `tests/KeyVaultSync.Tests/PrefixNamingTests.cs`:
```csharp
using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class PrefixNamingTests
{
    [Fact]
    public void Appends_trailing_dash_when_missing()
    {
        Assert.Equal("lm-dev-", PrefixNaming.Normalize("lm-dev"));
    }

    [Fact]
    public void Leaves_existing_trailing_dash()
    {
        Assert.Equal("lm-dev-", PrefixNaming.Normalize("lm-dev-"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_prefix(string prefix)
    {
        Assert.Throws<ArgumentException>(() => PrefixNaming.Normalize(prefix));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL — `PrefixNaming` does not exist (build error).

- [ ] **Step 3: Implement `PrefixNaming.cs`**

Create `src/KeyVaultSync/PrefixNaming.cs`:
```csharp
namespace KeyVaultSync;

public static class PrefixNaming
{
    public static string Normalize(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return prefix.EndsWith('-') ? prefix : prefix + "-";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS (3 new tests + existing suite).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add PrefixNaming.Normalize helper"
```

---

### Task 2: Resolver refactor (extract naming from the sync service)

This task refactors v1's shipped code. After it, `KeyVaultSyncService` is a pure
upsert engine, `SyncAction` no longer has `Failed`, and Mode A naming runs through
`FlattenSecretResolver`. The CLI stays verb-less here (`--vault`/`--file`); verbs
arrive in Task 6.

**Files:**
- Create: `src/KeyVaultSync/ISecretResolver.cs`
- Create: `src/KeyVaultSync/FlattenSecretResolver.cs`
- Modify: `src/KeyVaultSync/SyncResult.cs` (replace entirely)
- Modify: `src/KeyVaultSync/KeyVaultSyncService.cs` (replace entirely)
- Modify: `src/KeyVaultSync/Program.cs` (replace entirely)
- Modify: `tests/KeyVaultSync.Tests/SyncResultTests.cs` (replace entirely)
- Modify: `tests/KeyVaultSync.Tests/KeyVaultSyncServiceTests.cs` (replace entirely)
- Create: `tests/KeyVaultSync.Tests/FlattenSecretResolverTests.cs`

**Interfaces:**
- Produces: `record PlannedSecret(string DisplayKey, string SecretName, string Value)`.
- Produces: `record ResolutionResult(IReadOnlyList<PlannedSecret> Planned, IReadOnlyList<string> Invalid, IReadOnlyList<string> Unmapped, IReadOnlyList<string> MissingValue)`.
- Produces: `interface ISecretResolver { ResolutionResult Resolve(IReadOnlyList<KeyValuePair<string,string>> flattened); }`.
- Produces: `class FlattenSecretResolver : ISecretResolver`.
- Produces: `enum SyncAction { Added, Updated, Skipped }`; `record SyncEntry(string DisplayKey, string SecretName, SyncAction Action)`; `class SyncResult` with `Add`, `Entries`, `int Count(SyncAction)`.
- Produces: `class KeyVaultSyncService(ISecretStore store)` with `Task<SyncResult> SyncAsync(IReadOnlyList<PlannedSecret> planned, CancellationToken ct = default)`.
- Consumes: `SecretNameMapper` (unchanged), `ISecretStore` (unchanged), `JsonFlattener` (unchanged).

- [ ] **Step 1: Write the resolver/service tests (failing)**

Replace `tests/KeyVaultSync.Tests/SyncResultTests.cs` with:
```csharp
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
```

Replace `tests/KeyVaultSync.Tests/KeyVaultSyncServiceTests.cs` with:
```csharp
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
```

Create `tests/KeyVaultSync.Tests/FlattenSecretResolverTests.cs`:
```csharp
using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class FlattenSecretResolverTests
{
    private static IReadOnlyList<KeyValuePair<string, string>> Pairs(params (string Key, string Value)[] kv)
        => kv.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList();

    [Fact]
    public void Maps_colon_keys_to_double_dash()
    {
        var r = new FlattenSecretResolver().Resolve(Pairs(("ConnectionStrings:Default", "x")));
        Assert.Single(r.Planned);
        Assert.Equal("ConnectionStrings:Default", r.Planned[0].DisplayKey);
        Assert.Equal("ConnectionStrings--Default", r.Planned[0].SecretName);
        Assert.Equal("x", r.Planned[0].Value);
        Assert.Empty(r.Invalid);
    }

    [Fact]
    public void Records_invalid_key_as_invalid_and_continues()
    {
        var r = new FlattenSecretResolver().Resolve(Pairs(("Bad Key", "x"), ("Good", "y")));
        Assert.Contains("Bad Key", r.Invalid);
        Assert.Single(r.Planned);
        Assert.Equal("Good", r.Planned[0].SecretName);
    }

    [Fact]
    public void Unmapped_and_missing_are_always_empty()
    {
        var r = new FlattenSecretResolver().Resolve(Pairs(("A", "1")));
        Assert.Empty(r.Unmapped);
        Assert.Empty(r.MissingValue);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: FAIL — build errors (`PlannedSecret`, `ISecretResolver`, new `SyncEntry`/`SyncAction` shape, new `SyncAsync` signature do not exist yet).

- [ ] **Step 3: Implement `ISecretResolver.cs`**

Create `src/KeyVaultSync/ISecretResolver.cs`:
```csharp
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
```

- [ ] **Step 4: Replace `SyncResult.cs`**

Replace `src/KeyVaultSync/SyncResult.cs` with:
```csharp
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
```

- [ ] **Step 5: Replace `KeyVaultSyncService.cs`**

Replace `src/KeyVaultSync/KeyVaultSyncService.cs` with:
```csharp
namespace KeyVaultSync;

public sealed class KeyVaultSyncService
{
    private readonly ISecretStore _store;

    public KeyVaultSyncService(ISecretStore store) => _store = store;

    public async Task<SyncResult> SyncAsync(
        IReadOnlyList<PlannedSecret> planned,
        CancellationToken ct = default)
    {
        var result = new SyncResult();

        foreach (var p in planned)
        {
            var existing = await _store.GetValueAsync(p.SecretName, ct);
            if (existing is null)
            {
                await _store.SetValueAsync(p.SecretName, p.Value, ct);
                result.Add(new(p.DisplayKey, p.SecretName, SyncAction.Added));
            }
            else if (existing != p.Value)
            {
                await _store.SetValueAsync(p.SecretName, p.Value, ct);
                result.Add(new(p.DisplayKey, p.SecretName, SyncAction.Updated));
            }
            else
            {
                result.Add(new(p.DisplayKey, p.SecretName, SyncAction.Skipped));
            }
        }

        return result;
    }
}
```

- [ ] **Step 6: Implement `FlattenSecretResolver.cs`**

Create `src/KeyVaultSync/FlattenSecretResolver.cs`:
```csharp
namespace KeyVaultSync;

public sealed class FlattenSecretResolver : ISecretResolver
{
    public ResolutionResult Resolve(IReadOnlyList<KeyValuePair<string, string>> flattened)
    {
        var planned = new List<PlannedSecret>();
        var invalid = new List<string>();

        foreach (var (key, value) in flattened)
        {
            try
            {
                var name = SecretNameMapper.ToSecretName(key);
                planned.Add(new(key, name, value));
            }
            catch (ArgumentException)
            {
                invalid.Add(key);
            }
        }

        return new(planned, invalid, [], []);
    }
}
```

- [ ] **Step 7: Replace `Program.cs` (verb-less, resolver-based)**

Replace `src/KeyVaultSync/Program.cs` with:
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

var resolution = new FlattenSecretResolver().Resolve(pairs);

var client = new SecretClient(new Uri(vault), new DefaultAzureCredential());
var service = new KeyVaultSyncService(new KeyVaultSecretStore(client));

SyncResult result;
try
{
    result = await service.SyncAsync(resolution.Planned);
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
    Console.WriteLine($"  {e.Action,-7} {e.SecretName}");
foreach (var key in resolution.Invalid)
    Console.WriteLine($"  INVALID {key}");

Console.WriteLine(
    $"\nDone. Added: {result.Count(SyncAction.Added)}, " +
    $"Updated: {result.Count(SyncAction.Updated)}, " +
    $"Skipped: {result.Count(SyncAction.Skipped)}, " +
    $"Invalid: {resolution.Invalid.Count}");

return resolution.Invalid.Count > 0 ? 2 : 0;

static string? GetArg(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all tests; the suite reflects the new shapes).

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor: extract naming into ISecretResolver, simplify sync service"
```

---

### Task 3: PrefixProfile (parse the KeyVault profile section)

**Files:**
- Create: `src/KeyVaultSync/PrefixProfile.cs`
- Create: `tests/KeyVaultSync.Tests/PrefixProfileTests.cs`

**Interfaces:**
- Consumes: `PrefixNaming.Normalize` (Task 1), `Microsoft.Extensions.Configuration`.
- Produces: `sealed class PrefixProfile` with `string? VaultUri`, `string Prefix` (normalized), `IReadOnlyDictionary<string,string> SecretMappings` (suffix→configKey, `OrdinalIgnoreCase`), and `static PrefixProfile Load(string path)`. `Load` throws `InvalidOperationException` if `KeyVault:Prefix` is missing/blank.

- [ ] **Step 1: Write the failing test**

Create `tests/KeyVaultSync.Tests/PrefixProfileTests.cs`:
```csharp
using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class PrefixProfileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kvprofile-{Guid.NewGuid():N}.json");

    private void WriteProfile(string json) => File.WriteAllText(_path, json);

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Loads_vault_prefix_and_mappings()
    {
        WriteProfile("""
        {
          "KeyVault": {
            "VaultUri": "https://kv-passgate-dev.vault.azure.net",
            "Prefix": "lm-dev",
            "SecretMappings": {
              "db-connectionstring": "ConnectionStrings:DefaultConnection",
              "sendgrid-apikey": "SendGrid:APIKey"
            }
          }
        }
        """);

        var profile = PrefixProfile.Load(_path);

        Assert.Equal("https://kv-passgate-dev.vault.azure.net", profile.VaultUri);
        Assert.Equal("lm-dev-", profile.Prefix);
        Assert.Equal(2, profile.SecretMappings.Count);
        Assert.Equal("ConnectionStrings:DefaultConnection", profile.SecretMappings["db-connectionstring"]);
        Assert.Equal("SendGrid:APIKey", profile.SecretMappings["SENDGRID-APIKEY"]); // case-insensitive
    }

    [Fact]
    public void Throws_when_prefix_missing()
    {
        WriteProfile("""{ "KeyVault": { "VaultUri": "https://x.vault.azure.net" } }""");
        Assert.Throws<InvalidOperationException>(() => PrefixProfile.Load(_path));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL — `PrefixProfile` does not exist.

- [ ] **Step 3: Implement `PrefixProfile.cs`**

Create `src/KeyVaultSync/PrefixProfile.cs`:
```csharp
using Microsoft.Extensions.Configuration;

namespace KeyVaultSync;

public sealed class PrefixProfile
{
    public string? VaultUri { get; }
    public string Prefix { get; }
    public IReadOnlyDictionary<string, string> SecretMappings { get; }

    private PrefixProfile(string? vaultUri, string prefix, IReadOnlyDictionary<string, string> mappings)
    {
        VaultUri = vaultUri;
        Prefix = prefix;
        SecretMappings = mappings;
    }

    public static PrefixProfile Load(string path)
    {
        var full = Path.GetFullPath(path);
        var config = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(full)!)
            .AddJsonFile(Path.GetFileName(full), optional: false)
            .Build();

        var prefix = config["KeyVault:Prefix"];
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new InvalidOperationException($"Profile '{path}' is missing KeyVault:Prefix.");
        }

        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in config.GetSection("KeyVault:SecretMappings").GetChildren())
        {
            if (child.Value is not null)
            {
                mappings[child.Key] = child.Value;
            }
        }

        return new PrefixProfile(config["KeyVault:VaultUri"], PrefixNaming.Normalize(prefix), mappings);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add PrefixProfile to parse the KeyVault profile section"
```

---

### Task 4: PrefixSecretResolver (Mode B resolution)

**Files:**
- Create: `src/KeyVaultSync/PrefixSecretResolver.cs`
- Create: `tests/KeyVaultSync.Tests/PrefixSecretResolverTests.cs`

**Interfaces:**
- Consumes: `ISecretResolver`/`PlannedSecret`/`ResolutionResult` (Task 2), `PrefixNaming.Normalize` (Task 1), `SecretNameMapper.IsValid` (existing).
- Produces: `sealed class PrefixSecretResolver : ISecretResolver`; constructor `PrefixSecretResolver(string prefix, IReadOnlyDictionary<string,string> suffixToConfigKey)`. Reverse-maps configKey→suffix (`OrdinalIgnoreCase`). Per flattened pair: mapped + non-blank value → `Planned(configKey, prefix+suffix, value)` (or `Invalid` if the name fails `IsValid`); mapped + blank value → swept to `MissingValue`; unmapped → `Unmapped`. After the loop, every mapping suffix not handled (planned or invalid) → `MissingValue`.

- [ ] **Step 1: Write the failing test**

Create `tests/KeyVaultSync.Tests/PrefixSecretResolverTests.cs`:
```csharp
using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class PrefixSecretResolverTests
{
    private static readonly Dictionary<string, string> Mappings = new()
    {
        ["partnercenter-clientsecret"] = "PartnerCenter:ClientSecret",
        ["sendgrid-apikey"] = "SendGrid:APIKey",
    };

    private static IReadOnlyList<KeyValuePair<string, string>> Pairs(params (string Key, string Value)[] kv)
        => kv.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList();

    [Fact]
    public void Maps_config_key_to_prefixed_secret_name()
    {
        var r = new PrefixSecretResolver("lm-dev", Mappings)
            .Resolve(Pairs(("PartnerCenter:ClientSecret", "topsecret")));

        Assert.Single(r.Planned);
        Assert.Equal("PartnerCenter:ClientSecret", r.Planned[0].DisplayKey);
        Assert.Equal("lm-dev-partnercenter-clientsecret", r.Planned[0].SecretName);
        Assert.Equal("topsecret", r.Planned[0].Value);
    }

    [Fact]
    public void Matches_config_key_case_insensitively()
    {
        var r = new PrefixSecretResolver("lm-dev", Mappings)
            .Resolve(Pairs(("partnercenter:clientsecret", "v")));
        Assert.Single(r.Planned);
        Assert.Equal("lm-dev-partnercenter-clientsecret", r.Planned[0].SecretName);
    }

    [Fact]
    public void Records_unmapped_keys()
    {
        var r = new PrefixSecretResolver("lm-dev", Mappings)
            .Resolve(Pairs(("PartnerCenter:ClientSecret", "v"), ("PiiEncryption:Key", "abc")));
        Assert.Contains("PiiEncryption:Key", r.Unmapped);
        Assert.DoesNotContain("PartnerCenter:ClientSecret", r.Unmapped);
    }

    [Fact]
    public void Blank_value_for_mapped_key_becomes_missing_value()
    {
        var r = new PrefixSecretResolver("lm-dev", Mappings)
            .Resolve(Pairs(("PartnerCenter:ClientSecret", "   ")));
        Assert.Empty(r.Planned);
        Assert.Contains("partnercenter-clientsecret", r.MissingValue);
    }

    [Fact]
    public void Mapping_with_no_key_in_values_becomes_missing_value()
    {
        var r = new PrefixSecretResolver("lm-dev", Mappings)
            .Resolve(Pairs(("PartnerCenter:ClientSecret", "v")));
        // sendgrid-apikey was never provided
        Assert.Contains("sendgrid-apikey", r.MissingValue);
        Assert.DoesNotContain("partnercenter-clientsecret", r.MissingValue);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL — `PrefixSecretResolver` does not exist.

- [ ] **Step 3: Implement `PrefixSecretResolver.cs`**

Create `src/KeyVaultSync/PrefixSecretResolver.cs`:
```csharp
namespace KeyVaultSync;

public sealed class PrefixSecretResolver : ISecretResolver
{
    private readonly string _prefix;
    private readonly IReadOnlyDictionary<string, string> _suffixToConfigKey;
    private readonly Dictionary<string, string> _configKeyToSuffix;

    public PrefixSecretResolver(string prefix, IReadOnlyDictionary<string, string> suffixToConfigKey)
    {
        _prefix = PrefixNaming.Normalize(prefix);
        _suffixToConfigKey = suffixToConfigKey;
        _configKeyToSuffix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (suffix, configKey) in suffixToConfigKey)
        {
            _configKeyToSuffix[configKey] = suffix;
        }
    }

    public ResolutionResult Resolve(IReadOnlyList<KeyValuePair<string, string>> flattened)
    {
        var planned = new List<PlannedSecret>();
        var invalid = new List<string>();
        var unmapped = new List<string>();
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in flattened)
        {
            if (!_configKeyToSuffix.TryGetValue(key, out var suffix))
            {
                unmapped.Add(key);
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue; // swept into MissingValue below
            }

            var name = _prefix + suffix;
            if (SecretNameMapper.IsValid(name))
            {
                planned.Add(new(key, name, value));
            }
            else
            {
                invalid.Add(key);
            }
            handled.Add(suffix);
        }

        var missingValue = _suffixToConfigKey.Keys
            .Where(s => !handled.Contains(s))
            .ToList();

        return new(planned, invalid, unmapped, missingValue);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add PrefixSecretResolver for prefix+mappings mode"
```

---

### Task 5: ISecretLister + listing + MappingService

**Files:**
- Create: `src/KeyVaultSync/ISecretLister.cs`
- Modify: `src/KeyVaultSync/KeyVaultSecretStore.cs` (add `ISecretLister` implementation)
- Create: `src/KeyVaultSync/MappingService.cs`
- Create: `tests/KeyVaultSync.Tests/FakeSecretLister.cs`
- Create: `tests/KeyVaultSync.Tests/MappingServiceTests.cs`

**Interfaces:**
- Produces: `interface ISecretLister { Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default); }`.
- Produces: `class MappingService(ISecretLister lister)` with `Task<IReadOnlyDictionary<string,string>> BuildAsync(string prefix, IReadOnlyDictionary<string,string> mappings, CancellationToken ct = default)`. Filters names by `StartsWith(normalizedPrefix, OrdinalIgnoreCase)`, computes `suffix = name[prefix.Length..]` (skips empty), `configKey = mappings[suffix] ?? suffix`. Returns a `SortedDictionary` (Ordinal) suffix→configKey.
- Modifies: `KeyVaultSecretStore` now implements `ISecretStore, ISecretLister` (adds `ListNamesAsync` via `GetPropertiesOfSecretsAsync`).

- [ ] **Step 1: Write the failing test**

Create `tests/KeyVaultSync.Tests/FakeSecretLister.cs`:
```csharp
using KeyVaultSync;

namespace KeyVaultSync.Tests;

public sealed class FakeSecretLister : ISecretLister
{
    private readonly IReadOnlyList<string> _names;
    public FakeSecretLister(params string[] names) => _names = names;
    public Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default)
        => Task.FromResult(_names);
}
```

Create `tests/KeyVaultSync.Tests/MappingServiceTests.cs`:
```csharp
using KeyVaultSync;
using Xunit;

namespace KeyVaultSync.Tests;

public class MappingServiceTests
{
    private static readonly Dictionary<string, string> Mappings = new()
    {
        ["db-connectionstring"] = "ConnectionStrings:DefaultConnection",
    };

    [Fact]
    public async Task Includes_only_prefix_scoped_secrets()
    {
        var lister = new FakeSecretLister(
            "lm-dev-db-connectionstring",
            "lm-dev-sendgrid-apikey",
            "passgate-other-secret");

        var result = await new MappingService(lister).BuildAsync("lm-dev", Mappings);

        Assert.False(result.ContainsValue("passgate-other-secret"));
        Assert.DoesNotContain("other-secret", result.Keys);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Maps_suffix_to_config_key_or_falls_back_to_suffix()
    {
        var lister = new FakeSecretLister("lm-dev-db-connectionstring", "lm-dev-sendgrid-apikey");

        var result = await new MappingService(lister).BuildAsync("lm-dev", Mappings);

        Assert.Equal("ConnectionStrings:DefaultConnection", result["db-connectionstring"]);
        Assert.Equal("sendgrid-apikey", result["sendgrid-apikey"]); // no mapping -> suffix as-is
    }

    [Fact]
    public async Task Skips_secret_equal_to_bare_prefix()
    {
        var lister = new FakeSecretLister("lm-dev-");
        var result = await new MappingService(lister).BuildAsync("lm-dev", Mappings);
        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL — `ISecretLister` / `MappingService` do not exist.

- [ ] **Step 3: Implement `ISecretLister.cs`**

Create `src/KeyVaultSync/ISecretLister.cs`:
```csharp
namespace KeyVaultSync;

public interface ISecretLister
{
    Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement `MappingService.cs`**

Create `src/KeyVaultSync/MappingService.cs`:
```csharp
namespace KeyVaultSync;

public sealed class MappingService
{
    private readonly ISecretLister _lister;

    public MappingService(ISecretLister lister) => _lister = lister;

    public async Task<IReadOnlyDictionary<string, string>> BuildAsync(
        string prefix,
        IReadOnlyDictionary<string, string> mappings,
        CancellationToken ct = default)
    {
        var normalized = PrefixNaming.Normalize(prefix);
        var names = await _lister.ListNamesAsync(ct);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            if (!name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = name[normalized.Length..];
            if (string.IsNullOrEmpty(suffix))
            {
                continue;
            }

            result[suffix] = mappings.TryGetValue(suffix, out var configKey) ? configKey : suffix;
        }

        return result;
    }
}
```

- [ ] **Step 5: Add `ISecretLister` to `KeyVaultSecretStore.cs`**

Replace `src/KeyVaultSync/KeyVaultSecretStore.cs` with:
```csharp
using Azure;
using Azure.Security.KeyVault.Secrets;

namespace KeyVaultSync;

public sealed class KeyVaultSecretStore : ISecretStore, ISecretLister
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

    public async Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default)
    {
        var names = new List<string>();
        await foreach (var prop in _client.GetPropertiesOfSecretsAsync(ct))
        {
            names.Add(prop.Name);
        }
        return names;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS (3 new MappingService tests + existing suite).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add ISecretLister, secret listing, and MappingService"
```

---

### Task 6: Program verbs (sync Mode A/B + mapping) + README

**Files:**
- Modify: `src/KeyVaultSync/Program.cs` (replace entirely with the verb router)
- Modify: `README.md` (replace entirely)

**Interfaces:**
- Consumes: `FlattenSecretResolver`, `PrefixSecretResolver`, `PrefixProfile`, `KeyVaultSyncService`, `KeyVaultSecretStore`, `MappingService`, `JsonFlattener`, `SyncResult`, `SyncAction`, `ResolutionResult`; `DefaultAzureCredential`, `SecretClient`.
- CLI verbs: `sync` (Mode A `--vault --file`; Mode B `--profile --values [--vault]`), `mapping` (`--vault --prefix [--profile] [--report]`).

- [ ] **Step 1: Replace `Program.cs`**

Replace `src/KeyVaultSync/Program.cs` with:
```csharp
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
```

- [ ] **Step 2: Build and run the unit suite**

Run: `dotnet test`
Expected: PASS (all tests).

- [ ] **Step 3: Verify verb routing and arg errors without Azure**

Run (no args):
```bash
dotnet run --project src/KeyVaultSync
```
Expected: prints the usage banner, exit code `1`.

Run (unknown verb):
```bash
dotnet run --project src/KeyVaultSync -- frobnicate
```
Expected: `Unknown command 'frobnicate'` + usage, exit `1`.

Run (mapping missing prefix):
```bash
dotnet run --project src/KeyVaultSync -- mapping --vault https://x.vault.azure.net
```
Expected: `Error: --prefix is required for mapping.`, exit `1`.

Run (sync mode B, missing values file):
```bash
dotnet run --project src/KeyVaultSync -- sync --profile ./nope.json --values ./nope2.json
```
Expected: `Error: profile file not found: ./nope.json`, exit `1`.

Confirm exit codes with `echo $?` (bash) / `$LASTEXITCODE` (PowerShell).

- [ ] **Step 4: Replace `README.md`**

Replace `README.md` with:
````markdown
# KeyVaultSync

Console tool for Azure Key Vault with two commands:

- **`sync`** — push key-value pairs from a JSON file into a vault (add missing, update changed, skip identical).
- **`mapping`** — read-only: emit a `SecretMappings`-shaped JSON (`{ suffix: configKey }`) for a vault's prefix-scoped secrets.

## sync

### Mode A — flatten convention (`:` → `--`)

```bash
KeyVaultSync sync --vault https://myvault.vault.azure.net --file ./appsettings.json
```

Nested keys are flattened (`ConnectionStrings:Default`) and `:` is converted to `--`
(`ConnectionStrings--Default`), the Microsoft Key Vault configuration default.

### Mode B — prefix + mappings

For a vault shared by multiple apps, where secrets are named `{prefix}-{suffix}` and
a `SecretMappings` table (suffix → config key) drives reads (as in
`PrefixKeyVaultSecretManager`). Supply a profile (the KeyVault config section) and a
values file (a real appsettings with filled-in values):

```bash
KeyVaultSync sync --profile ./keyvault.json --values ./appsettings.Development.json
```

The tool reverses `SecretMappings` (config key → suffix), writes `{prefix}-{suffix}`
secrets, and reports:
- **Unmapped** — values keys with no mapping (not managed secrets).
- **MissingValue** — mappings with no value supplied (still empty in the vault).

The vault URL comes from the profile's `KeyVault:VaultUri` (override with `--vault`).

## mapping

```bash
KeyVaultSync mapping --vault https://myvault.vault.azure.net --prefix lm-dev --profile ./keyvault.json --report ./mapping.json
```

Lists the vault's `{prefix}-*` secrets and emits `{ suffix: configKey }` JSON (values
are never read). With no `--profile`, `configKey` falls back to the suffix. Without
`--report`, the JSON is written to stdout.

## Prefix isolation

Both commands operate only on `{prefix}-*` names, so secrets belonging to other apps
in the same vault are never read, written, or deleted.

## Authentication

Uses `DefaultAzureCredential`: locally `az login`; in CI/CD a managed identity or
`AZURE_*` environment variables. The identity needs the **Key Vault Secrets Officer**
role (and list permission for `mapping`).

## Exit codes

- `sync`: `0` success · `1` fatal (bad input, missing vault, auth failure) · `2` partial (≥1 invalid secret name).
- `mapping`: `0` success · `1` fatal.
````

- [ ] **Step 5: Live verification (manual — OPTIONAL/SKIP if no Azure access)**

If you have `az login` and a test vault with **Key Vault Secrets Officer**:

Create `kv.json`:
```json
{ "KeyVault": { "VaultUri": "https://<your-vault>.vault.azure.net", "Prefix": "lm-dev", "SecretMappings": { "db-connectionstring": "ConnectionStrings:DefaultConnection" } } }
```
Create `vals.json`:
```json
{ "ConnectionStrings": { "DefaultConnection": "Server=." }, "AllowedHosts": "*" }
```
Run:
```bash
dotnet run --project src/KeyVaultSync -- sync --profile ./kv.json --values ./vals.json
dotnet run --project src/KeyVaultSync -- mapping --vault https://<your-vault>.vault.azure.net --prefix lm-dev --profile ./kv.json
```
Expected: first run reports `Added lm-dev-db-connectionstring` and `Unmapped: AllowedHosts`; mapping prints `{ "db-connectionstring": "ConnectionStrings:DefaultConnection" }`. If you lack Azure access, SKIP and note it in your report.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add sync/mapping verbs, Mode B, and update README"
```

---

## Notes for the implementer

- Run every `dotnet` command from the repo root.
- Task 2 is a refactor of shipped code; keep the build green by replacing files in the order listed (tests first, then sources) and only committing once `dotnet test` passes.
- `OrdinalIgnoreCase` for suffix/config-key/prefix matching is required (it mirrors the consuming app's `FrozenDictionary` and `StartsWith` semantics) — do not switch to ordinal-case-sensitive.
- The tool's own `src/KeyVaultSync/appsettings.json` (from v1) is unused by the verb router and may remain as a harmless placeholder.
