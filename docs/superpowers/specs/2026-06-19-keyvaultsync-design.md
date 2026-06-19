# KeyVaultSync — Design

**Date:** 2026-06-19
**Status:** Approved

## Purpose

A .NET console tool that synchronizes all key-value pairs from an `appsettings.json`
file into an Azure Key Vault. For each pair: if the secret is missing it is added,
if it exists and the value changed it is updated, and if the value is identical it is
skipped (no new version created). Eliminates the manual, one-by-one entry of secrets.

## Scope

- Input: a single JSON file (default `appsettings.json` format), bulk upload.
- Output target: one Azure Key Vault, selected per run.
- Auth: `DefaultAzureCredential` (no credentials stored in the tool).
- Out of scope (YAGNI): deleting secrets that exist in the vault but not the file;
  reading secrets back out; multiple files per run; non-JSON inputs.

## Architecture

Single small project, responsibilities split so each unit is independently testable.

```
KeyVaultSync/                    (separate repo)
├── KeyVaultSync.sln
├── src/KeyVaultSync/
│   ├── Program.cs               CLI entry: arg parse, wires the flow, reports result
│   ├── JsonFlattener.cs         nested JSON -> flat key-value list (":" separator)
│   ├── SecretNameMapper.cs      "A:B" <-> "A--B"; validates Key Vault name charset
│   ├── KeyVaultSyncService.cs   upsert logic (read-compare-write per secret)
│   ├── SyncResult.cs            Added/Updated/Skipped counters + per-key report
│   └── appsettings.json         vault URL fallback
├── tests/KeyVaultSync.Tests/
└── README.md
```

### Units

**JsonFlattener** — pure function, no Azure dependency.
`{"ConnectionStrings":{"Default":"x"}}` -> `ConnectionStrings:Default = x`.
Arrays expand to `Key:0`, `Key:1` (matches .NET configuration convention).
Only leaf string/number/bool values are emitted; objects are recursed into.

**SecretNameMapper** — pure function. Converts `:` to `--` for Key Vault, since
secret names allow only `a-zA-Z0-9-`. Validates the resulting name; an invalid name
produces a clear error so the caller can skip that key and continue.

**KeyVaultSyncService** — takes a `SecretClient`. For each mapped secret:
read current value -> if not found (404) add it (Added); if found and value differs,
write new version (Updated); if identical, skip (Skipped).

**Program** — reads `--vault` and `--file` args, falls back to the tool's own
`appsettings.json` for the vault URL, constructs the client with
`DefaultAzureCredential`, runs the service, prints the `SyncResult`.

## Data Flow

```
appsettings.json -> JsonFlattener -> flat key-value list
                 -> per key: SecretNameMapper (":" -> "--")
                 -> KeyVaultSyncService.Upsert -> Azure Key Vault
                 -> SyncResult (e.g. 3 Added, 1 Updated, 5 Skipped)
```

## CLI Usage

```
KeyVaultSync --vault https://myvault.vault.azure.net --file ./appsettings.json
```

- `--vault` omitted -> read vault URL from the tool's own `appsettings.json`.
- `--file` omitted -> default to `./appsettings.json`.

## Authentication

`DefaultAzureCredential`: locally uses `az login` / Visual Studio sign-in; in CI/CD
uses Managed Identity or environment variables. No secret material in the tool.

## Error Handling

| Condition | Behavior | Exit code |
|-----------|----------|-----------|
| Input file missing / invalid JSON | Clear message | 1 |
| Vault URL absent (no arg, no config) | Clear message | 1 |
| Auth/authorization failure (401/403) | Message hinting the `Key Vault Secrets Officer` RBAC role is required | 1 |
| Invalid secret name | Skip that key, list it as a warning, continue others | (see below) |
| All keys processed successfully | — | 0 |
| Partial failure (some keys errored/skipped due to error) | — | 2 |

## NuGet Packages

- `Azure.Security.KeyVault.Secrets`
- `Azure.Identity`

## Testing

xUnit:
- **JsonFlattener** — nested objects, arrays, empty object, mixed value types.
- **SecretNameMapper** — `:` <-> `--` round-trip; invalid-character rejection.
- **KeyVaultSyncService** — mocked `SecretClient` covering Added / Updated / Skipped
  and the 404-means-missing path.
