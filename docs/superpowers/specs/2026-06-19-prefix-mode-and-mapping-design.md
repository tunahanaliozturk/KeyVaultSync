# KeyVaultSync v2 — Prefix Mode + Mapping Command — Design

**Date:** 2026-06-19
**Status:** Approved
**Builds on:** 2026-06-19-keyvaultsync-design.md (v1)

## Purpose

Extend KeyVaultSync to fit a real-world deployment where a single Azure Key Vault
is shared by multiple applications, each isolated by a secret-name **prefix**
(e.g. `lm-dev-`), and secret names use an application-defined **suffix** that maps
to a .NET config key via a `SecretMappings` table (the inverse of the app's
`PrefixKeyVaultSecretManager`).

Two capabilities are added:

1. **Prefix sync mode** — push secret values from a real `appsettings.json`
   (config-key shaped, values filled in) into the vault using
   `{prefix}-{suffix}` names derived by reversing the `SecretMappings` table.
2. **Mapping command** — a read-only command that lists the vault's
   prefix-scoped secrets and emits a `SecretMappings`-shaped JSON
   (`{ suffix: configKey }`), names only, no values.

## CLI

The tool gains explicit verbs. (This changes v1's verb-less CLI; the tool is
day-zero with no external users, so the README is updated rather than kept
backward-compatible.)

```
KeyVaultSync sync    --vault <uri> --file <appsettings.json>            # Mode A (existing :->--)
KeyVaultSync sync    --profile <kv.json> --values <appsettings.json>   # Mode B (prefix + mappings)
KeyVaultSync mapping --vault <uri> --prefix <prefix> [--profile <kv.json>] [--report <out.json>]
```

- `sync`: `--profile` present selects Mode B; otherwise Mode A. In Mode B the vault
  comes from the profile's `KeyVault:VaultUri`, overridable by `--vault`.
  `--file` defaults to `appsettings.json`.
- `mapping`: `--vault` and `--prefix` required; `--profile` optional (provides the
  suffix→configKey mappings); `--report` writes JSON to a file, otherwise stdout.

## Architecture

### Naming/resolution split (refactor of v1)

In v1, `KeyVaultSyncService` performed naming, invalid-name handling, and upsert.
v2 separates naming/filtering from I/O via a resolver.

**`ISecretResolver`** — turns flattened `(key,value)` pairs into a plan plus
diagnostics:

```csharp
public sealed record PlannedSecret(string DisplayKey, string SecretName, string Value);

public sealed record ResolutionResult(
    IReadOnlyList<PlannedSecret> Planned,
    IReadOnlyList<string> Invalid,        // secret name rejected by Key Vault charset
    IReadOnlyList<string> Unmapped,       // Mode B: values key with no mapping
    IReadOnlyList<string> MissingValue);  // Mode B: mapping defined but no/empty value

public interface ISecretResolver
{
    ResolutionResult Resolve(IReadOnlyList<KeyValuePair<string, string>> flattened);
}
```

- **`FlattenSecretResolver`** (Mode A): each key → `SecretNameMapper.ToSecretName`
  (`:`→`--`); on `ArgumentException` the key goes to `Invalid`. `Unmapped` and
  `MissingValue` are always empty. Reproduces v1's Mode A behavior exactly.
- **`PrefixSecretResolver`** (Mode B): given a normalized prefix and the
  `SecretMappings` table (suffix→configKey), builds the reverse map
  (configKey→suffix, `OrdinalIgnoreCase`). For each flattened `(key,value)`:
  - mapping found and value non-blank → `name = prefix + suffix`; if
    `SecretNameMapper.IsValid(name)` then `Planned`, else `Invalid`;
  - mapping found but value null/empty/whitespace → leave unhandled (swept into
    `MissingValue`);
  - no mapping → `Unmapped`.
  After the loop, every mapping suffix not handled (written or invalid) → `MissingValue`
  (covers both empty-value and absent-key cases).

**`KeyVaultSyncService`** (simplified): takes `IReadOnlyList<PlannedSecret>` and
performs pure upsert — read current value, then Added / Updated / Skipped. No
naming, no invalid handling. Store exceptions propagate (unchanged from v1).

**`SyncResult` / `SyncAction`**: `SyncAction` becomes `{ Added, Updated, Skipped }`
(the v1 `Failed` member is removed; invalid names are now a resolver diagnostic).
`SyncEntry` carries `DisplayKey` and `SecretName`.

### Profile parsing

**`PrefixProfile`** — parses a profile JSON's `KeyVault` section using the existing
`Microsoft.Extensions.Configuration.Json`: `VaultUri`, `Prefix` (stored normalized),
and `SecretMappings` (suffix→configKey, read by enumerating
`GetSection("KeyVault:SecretMappings").GetChildren()`).

**`PrefixNaming.Normalize(string)`** — shared helper: appends a trailing `-` if
absent (mirrors the app's `PrefixKeyVaultSecretManager.NormalizePrefix`). Used by
`PrefixSecretResolver`, `PrefixProfile`, and `MappingService`.

### Listing / mapping

**`ISecretLister`** — segregated read interface:
`Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default)`.

**`KeyVaultSecretStore`** implements both `ISecretStore` (get/set) and
`ISecretLister` (`ListNamesAsync` via `GetPropertiesOfSecretsAsync`, collecting
`.Name`).

**`MappingService`** — given a normalized prefix and the optional mappings table,
lists names, keeps those where `name.StartsWith(prefix, OrdinalIgnoreCase)`,
computes `suffix = name[prefix.Length..]` (skipping empty suffixes), and
`configKey = mappings.TryGetValue(suffix) ?? suffix` (mirrors the app's `GetKey`).
Returns an ordered `IReadOnlyDictionary<string,string>` (suffix→configKey). Never
reads secret values.

## Data Flow

**Mode B sync:**
```
--profile kv.json → PrefixProfile (prefix, vaultUri, mappings)
--values  app.json → JsonFlattener → (configKey, value) list
   → PrefixSecretResolver.Resolve → Planned + Unmapped + MissingValue (+ Invalid)
   → KeyVaultSyncService.SyncAsync(Planned) → upsert {prefix}-{suffix} names
   → console report
```

**Mapping:**
```
--vault, --prefix, --profile → MappingService.BuildAsync
   → KeyVaultSecretStore.ListNamesAsync → filter StartsWith(prefix)
   → { suffix: configKey } → JSON to stdout or --report file
```

## Reporting and Exit Codes

**`sync` (console text):**
```
  Added    lm-dev-partnercenter-clientsecret
  Updated  lm-dev-partnercenter-clientid
  Skipped  lm-dev-partnercenter-vaultprefix

Unmapped (not a managed secret):
  PiiEncryption:Key
  ConnectionStrings:WebApiDatabase
  AllowedHosts

MissingValue (mapping defined, no value — still empty in vault):
  sendgrid-apikey
  peakauth-masterkey

Done. Added: N, Updated: N, Skipped: N, Invalid: N
Unmapped: N, MissingValue: N
```
Exit codes: `0` all good (Unmapped/MissingValue are warnings only); `1` fatal
(missing/invalid input file, missing vault, auth/authorization failure); `2`
partial (at least one `Invalid` secret name).

**`mapping`:** writes the JSON object to `--report` file or stdout. Exit `0` on
success; `1` on fatal error (missing args, vault unreachable, auth failure). No
exit `2` (read-only; nothing to partially fail).

## Prefix Isolation (shared vault)

Both commands operate strictly on `{prefix}-*` names: `sync` only ever GETs/SETs
names it builds with the prefix; `mapping` filters the listing by
`StartsWith(prefix)`. Secrets belonging to other applications in the same vault
(e.g. a `passgate-` prefix alongside `lm-dev-`) are never read, written, or
deleted. This mirrors the app's `PrefixKeyVaultSecretManager.Load` isolation.

## Out of Scope (YAGNI)

- Deleting orphaned secrets (the mapping command reports inventory; it never
  deletes). Reconciliation/orphan-diffing beyond the raw `{suffix: configKey}`
  inventory is left to the consumer comparing the JSON against their app's
  `SecretMappings`.
- Emitting secret values in the mapping output.
- Suffix-keyed flat input (superseded by the profile+values model).
- Multiple vaults/profiles per run.

## NuGet Packages

No new packages. `Azure.Security.KeyVault.Secrets`, `Azure.Identity`,
`Microsoft.Extensions.Configuration.Json` already referenced.

## Testing

xUnit:
- **PrefixNaming** — normalize idempotence (`lm-dev`→`lm-dev-`, `lm-dev-`→`lm-dev-`).
- **PrefixProfile** — parses VaultUri, Prefix, SecretMappings from a sample profile JSON.
- **FlattenSecretResolver** — nested→names, invalid key→Invalid, no Unmapped/MissingValue.
- **PrefixSecretResolver** — mapped+value→Planned (`prefix+suffix`); unmapped key→Unmapped;
  empty value→MissingValue; mapping with no key→MissingValue; case-insensitive match;
  invalid resulting name→Invalid.
- **KeyVaultSyncService** (simplified) — Added/Updated/Skipped; Skipped writes nothing;
  non-ArgumentException from store propagates.
- **MappingService** — prefix filter (ignores other-prefix names), suffix extraction,
  `mappings[suffix] ?? suffix` fallback, empty-suffix skipped; uses a fake `ISecretLister`.
- End-to-end Mode B via `FakeSecretStore` — asserts `{prefix}-{suffix}` names written.
