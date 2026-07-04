# Settings and Secrets

This sample declares a Web API resource programmatically and assigns runtime
environment variables from references:

- `SAMPLE_MESSAGE` and `SAMPLE_MODE` come from a `configuration.store`
  resource through `settings.Setting(...)`.
- `SAMPLE_API_KEY` comes from a `secrets.vault` resource through
  `secrets.Secret(...)`.

The application resource stores references, not copied values. These
references are declared under the ASP.NET Core project's environment variable
attribute because they are passed to the Web API process as environment
variables. CloudShell resolves those references when the resource is started.
General `configuration` remains a separate resource configuration channel.

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
Store through the logical URI `https+http://configuration-sample-app`, using the Web
API resource identity token for authorization.

The Web API also exposes `/service-discovery/configuration-store` and
`/service-discovery/secrets-vault/{name}`. Those paths call the
Configuration Store and Secrets Vault through logical service-discovery URIs
derived from `project.references` on the ASP.NET Core project resource.
The endpoints still use the Web API resource identity token for authorization;
service discovery only locates the referenced services.

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

The sample declares Resource model Configuration Store and Secrets Vault
resources. These resources project endpoint and `*.entries.count`
summary attributes through the Resource Manager bridge, but the actual
configuration settings, secrets, grants, and backing services remain owned by
the provider-owned runtime integrations. The runtime integrations currently
start local C# service projects; a future provider implementation can back the
same resource shapes with containers.

The sample no longer declares the old `configuration:sample-app`,
`secrets-vault:sample-app`, or `application:settings-secrets-api` provider
records. It declares the built-in identity provider, Resource model
Configuration Store, Secrets Vault, and ASP.NET Core API resources, then smoke
coverage starts the services and API and verifies the API reads configuration
and secrets through provider-owned service references.

Run the sample host:

```bash
dotnet run --project samples/SettingsAndSecrets/CloudShell.SettingsAndSecrets.csproj -- --urls http://localhost:5011
```

The Web API resource listens on `http://localhost:5228` by default. Override
`Samples:SettingsAndSecrets:ApiEndpoint` when that port is already in use.
Smoke tests can also override
`Samples:SettingsAndSecrets:ConfigurationServiceEndpoint` and
`Samples:SettingsAndSecrets:SecretsServiceEndpoint` so provider-owned backing
services do not collide with detached processes from another run.

The Web API resource declaration calls `ProvisionIdentityOnStartup()`, so the
built-in identity client is registered before the API resource is started. You
can inspect provider-owned provisioning status with:

```bash
curl http://localhost:5011/api/control-plane/v1/resources/application.aspnet-core-project%3Asettings-secrets-api/identity/provisioning-status
```

Then start the Web API resource and open `/configuration`. If the API was already
running before identity provisioning, `/configuration` retries configuration
loading on the next request.

After the Web API is running, `/secrets/sample-api-key` reads the secret value
from the protected Secrets Vault service with the Web API resource identity.
