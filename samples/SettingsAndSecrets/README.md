# Settings and Secrets

This sample declares a Web API resource programmatically and assigns runtime
environment variables from references:

- `SAMPLE_MESSAGE` and `SAMPLE_MODE` come from a `configuration.store`
  resource through `settings.Entry(...)`.
- `SAMPLE_API_KEY` comes from a `secrets.vault` resource through
  `secrets.Secret(...)`.

The application resource stores references, not copied values. CloudShell
resolves those references when the resource is started.

The Web API also references the Configuration Store and Secrets Vault as
discoverable services. `WithReference(...)` records the endpoint relationship,
and `WithServiceDiscovery()` projects those endpoints into the current
application-level service discovery configuration. The Web API enables
`Microsoft.Extensions.ServiceDiscovery` so it can resolve logical service URIs
from that configuration. The service-specific SDK endpoint variables still
exist for this first client integration, but the resource graph now shows the
intended direction: discovery locates the service, while identity and grants
authorize access to it.

The sample Web API exposes `/service-discovery/configuration` to prove the
current service discovery path end to end. That endpoint calls the Configuration
Store through the logical URI `https+http://sample-app-settings`, using the Web
API resource identity token for authorization.

The Web API also dogfoods the public-preview SDK clients and their
service-specific Microsoft configuration integrations. It calls
`AddCloudShellConfigurationStore()` from `CloudShell.Configuration.Client` and
`AddCloudShellSecretsVault()` from `CloudShell.Secrets.Client`, registers
`DefaultCloudShellResourceCredential` as the application resource credential,
then supplies that credential to `ConfigurationStoreClient` and
`SecretsVaultClient` through its service-client model. Those integrations and
clients discover the service endpoints injected by the dependent
`configuration.store` and `secrets.vault` resources.

The sample also declares a built-in development identity provider. The Web API
resource has a `settings-secrets-api` identity. The configuration store grants
that identity `ConfigurationStoreResourceOperationPermissions.ReadEntries`, and
the Secrets Vault grants it
`SecretsVaultResourceOperationPermissions.ReadSecrets`. In this first flow the
Web API identity is provisioned automatically when the Control Plane starts.
The application resource provider injects the `CLOUDSHELL_IDENTITY_*`
credential acquisition environment when it starts the Web API resource. The
configuration store and vault are protected target resources; they do not need
their own identities unless they later call another resource or provider.

Run the sample host:

```bash
dotnet run --project samples/SettingsAndSecrets/CloudShell.SettingsAndSecrets.csproj -- --urls http://localhost:5011
```

The Web API resource listens on `http://localhost:5227` by default. Override
`Samples:SettingsAndSecrets:ApiEndpoint` when that port is already in use.

The Web API resource declaration calls `ProvisionIdentityOnStartup()`, so the
built-in identity client is registered before the API resource is started. You
can inspect provider-owned provisioning status with:

```bash
curl http://localhost:5011/api/control-plane/v1/resources/application%3Asettings-secrets-api/identity/provisioning-status
```

Then start the Web API resource and open `/configuration`. If the API was already
running before identity provisioning, `/configuration` retries configuration
loading on the next request.

After the Web API is running, `/secrets/sample-api-key` reads the secret value
from the protected Secrets Vault service with the Web API resource identity.
