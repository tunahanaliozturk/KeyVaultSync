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
role, which includes the list permission used by `mapping`.

## Exit codes

- `sync`: `0` success · `1` fatal (bad input, missing vault, auth failure) · `2` partial (≥1 invalid secret name).
- `mapping`: `0` success · `1` fatal.
