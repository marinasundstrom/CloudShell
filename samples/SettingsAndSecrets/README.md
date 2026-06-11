# Settings and Secrets

This sample declares a Web API resource programmatically and assigns runtime
environment variables from references:

- `SAMPLE_MESSAGE` and `SAMPLE_MODE` come from a `configuration.store`
  resource through `settings.Entry(...)`.
- `SAMPLE_API_KEY` comes from a `secrets.vault` resource through
  `secrets.Secret(...)`.

The application resource stores references, not copied values. CloudShell
resolves those references when the resource is started.

The sample also declares a built-in development identity provider. The Web API
resource has a `settings-secrets-api` identity. The configuration store grants
that identity `ConfigurationStoreResourceOperationPermissions.ReadEntries`, and
the Secrets Vault grants it
`SecretsVaultResourceOperationPermissions.ReadSecrets`. In this first flow the
Web API identity is provisioned, while the configuration store and vault are
protected target resources; they do not need their own identities unless they
later call another resource or provider.

Open the Web API resource details and use **Provision identity**, or call:

```bash
curl -X POST http://localhost:5011/api/control-plane/v1/resources/application%3Asettings-secrets-api/identity/provision
```

```bash
dotnet run --project samples/SettingsAndSecrets/CloudShell.SettingsAndSecrets.csproj -- --urls http://localhost:5011
```
