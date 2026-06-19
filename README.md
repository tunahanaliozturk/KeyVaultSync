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
