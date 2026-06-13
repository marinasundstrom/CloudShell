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
Web API identity is provisioned automatically when the Control Plane starts,
while the configuration store and vault are protected target resources; they do
not need their own identities unless they later call another resource or
provider.

Run the sample host:

```bash
dotnet run --project samples/SettingsAndSecrets/CloudShell.SettingsAndSecrets.csproj -- --urls http://localhost:5011
```

The Web API resource declaration calls `ProvisionIdentityOnStartup()`, so the
built-in identity client is registered before the API resource is started. You
can inspect provider-owned provisioning status with:

```bash
curl http://localhost:5011/api/control-plane/v1/resources/application%3Asettings-secrets-api/identity/provisioning-status
```

Then run the Web API resource and open `/configuration`. If the API was already
running before identity provisioning, `/configuration` retries configuration
loading on the next request.
