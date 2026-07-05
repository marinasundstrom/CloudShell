# CloudShell Go SDK

This experimental Go SDK provides runtime service clients for Go workloads.
The first clients cover Configuration Store and Secrets Vault.

The SDK is separate from the Go launcher package in `Launchers/Go/cloudshell`.
Launcher code declares resources and applies templates; this package runs
inside workloads after CloudShell starts them.

By default, `DefaultCredential` resolves credentials in this order:

1. `CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT`, `CLOUDSHELL_IDENTITY_CLIENT_ID`,
   `CLOUDSHELL_IDENTITY_CLIENT_SECRET`, and optional
   `CLOUDSHELL_IDENTITY_SCOPE`.
2. Environment bearer tokens such as `CLOUDSHELL_CONFIGURATION_TOKEN`,
   `CLOUDSHELL_SECRETS_TOKEN`, `CLOUDSHELL_CONTROL_PLANE_TOKEN`, or
   `CLOUDSHELL_TOKEN`.
3. The active CloudShell profile from `~/.cloudshell/config.json` or
   `CLOUDSHELL_CONFIG_DIR`, with `CLOUDSHELL_PROFILE` selecting a profile.

Example:

```go
client, err := cloudshell.ConfigurationStoreFromEnvironment("", nil)
if err != nil {
    return err
}

settings, err := client.GetSettings(context.Background())
```

```go
vault, err := cloudshell.SecretsVaultFromEnvironment("", nil)
if err != nil {
    return err
}

secret, err := vault.GetSecret(context.Background(), "Sample--ApiKey")
```
