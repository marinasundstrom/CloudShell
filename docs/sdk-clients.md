# SDK Clients

CloudShell SDK clients are package-ready client libraries for authored
services, integrations, and built-in services that call CloudShell-protected
APIs.

The SDK clients should not drag in the full Control Plane or resource-model
abstractions unless they expose that domain surface directly. Service-specific
clients depend on the small shared client credential package and their own
request/response contracts.

## Projects

- `CloudShell.Client`: shared SDK credential primitives, including
  `CloudShellResourceCredential`, `DefaultCloudShellResourceCredential`, and
  the environment-backed credential source. This package intentionally does not
  reference `CloudShell.Abstractions`.
- `CloudShell.ControlPlane.Client`: remote domain client for the Control Plane
  API. This package references `CloudShell.Abstractions` because it exposes the
  domain-shaped `IControlPlane`, `IResourceManager`, resource, log, and trace
  contracts.
- `CloudShell.Configuration.Client`: Configuration Store SDK client. It
  references `CloudShell.Client`, not the full Control Plane abstractions, and
  owns the Microsoft `IConfiguration` integration for configuration entries.
- `CloudShell.Secrets.Client`: Secrets Vault SDK client. It references
  `CloudShell.Client`, not the full Control Plane abstractions, and owns the
  Microsoft `IConfiguration` integration for vault secrets.

Future service-specific SDK clients should follow the same `.Client`
convention and avoid depending on `CloudShell.Abstractions` unless the client
explicitly exposes Control Plane domain contracts.

## Resource Credentials

Authored services running as CloudShell resources should use
`DefaultCloudShellResourceCredential` unless they need to test or override a
specific credential source:

```csharp
using CloudShell.Client.Authentication;

var credential = new DefaultCloudShellResourceCredential();
```

In ASP.NET Core services, register the credential once and supply it to SDK
clients from the application service model:

```csharp
using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;
using CloudShell.Secrets.Client;

builder.Configuration.AddCloudShellConfigurationStore();
builder.Configuration.AddCloudShellSecretsVault();
builder.Services.AddSingleton<CloudShellResourceCredential>(_ => new DefaultCloudShellResourceCredential());
builder.Services.AddSingleton<CloudShellServiceClients>();

app.MapGet("/configuration", async (
    CloudShellServiceClients clients,
    CancellationToken cancellationToken) =>
{
    var configuration = clients.CreateConfigurationStoreClient();
    return await configuration.GetEntriesAsync(cancellationToken);
});

sealed class CloudShellServiceClients(CloudShellResourceCredential credential)
{
    public ConfigurationStoreClient CreateConfigurationStoreClient() =>
        ConfigurationStoreClient.FromEnvironment(credential);

    public SecretsVaultClient CreateSecretsVaultClient() =>
        SecretsVaultClient.FromEnvironment(credential);
}
```

The first credential source reads the environment contract injected by the
resource provider that starts the workload process or container:

```text
CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT
CLOUDSHELL_IDENTITY_CLIENT_ID
CLOUDSHELL_IDENTITY_CLIENT_SECRET
CLOUDSHELL_IDENTITY_SCOPE
```

The convention is the same for local processes, direct container starts, and
descriptor-driven container orchestration:

- Resource providers inject these variables only for resources that have a
  resolved identity binding and a supported credential acquisition mechanism.
- Orchestrators that materialize workload descriptors must pass the variables
  through to the workload container or process unchanged.
- Authored services use `DefaultCloudShellResourceCredential` or an explicit
  `CloudShellResourceCredential`; service clients use that credential to
  request bearer tokens and attach `Authorization: Bearer ...`.
- Protected resource services authorize the bearer token through resource
  permission claims. For example, Secrets Vault checks
  `SecretsVault.ReadSecrets` on the vault resource before returning a secret.
- The credential values are runtime inputs. They must not be copied into
  resource attributes, generated UI details, logs, activity messages, or other
  user-facing projections.

Service endpoints are a separate concern. Configuration Store, Secrets Vault,
and other resource-backed services should be discovered through the same
service discovery and networking model as other services. Until network-level
service discovery is available, applications configure the SDK endpoint
variables explicitly or receive them from the current local development host
integration.

The credential contract is public preview. Future sources can add managed
identity endpoints, federated workload identity, local development
credentials, external provider plugins, or platform-specific brokers without
changing service-client code.

## Control Plane Client

Use `CloudShell.ControlPlane.Client` when a service needs the domain-shaped
Control Plane API:

```csharp
using CloudShell.Client.Authentication;
using CloudShell.ControlPlane.Client;

var credential = new DefaultCloudShellResourceCredential();
var controlPlane = new RemoteControlPlane(
    new Uri("https://control-plane.example.com"),
    credential,
    ["ControlPlane.Access"]);

var resources = await controlPlane.ListResourcesAsync();
```

DI registration supports the same credential object:

```csharp
builder.Services.AddRemoteControlPlane(
    new Uri("https://control-plane.example.com"),
    new DefaultCloudShellResourceCredential(),
    ["ControlPlane.Access"]);
```

## Configuration Store Client

Use `CloudShell.Configuration.Client` for direct Configuration Store service
calls:

```csharp
using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;

var credential = new DefaultCloudShellResourceCredential();
var configuration = ConfigurationStoreClient.FromEnvironment(credential);
var entries = await configuration.GetEntriesAsync();
```

Applications that configure Configuration Store endpoint discovery use
variables such as:

```text
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_STORE_ID
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_STORE_ID
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_ENDPOINT
```

The endpoint points at the protected entries collection. The client requests a
resource identity token and sends it as a bearer token on each service call.

The same package provides the configuration-provider integration:

```csharp
using CloudShell.Configuration.Client;

builder.Configuration.AddCloudShellConfigurationStore(options =>
{
    options.ServiceName = "Sample App Settings";
});
```

Provider diagnostics are exposed under `CloudShell:ConfigurationStore:*`,
including `Status`, `Detail`, `Source`, `LoadedKeys`, and `SecretKeys`.

## Secrets Vault Client

Use `CloudShell.Secrets.Client` for direct Secrets Vault service calls:

```csharp
using CloudShell.Client.Authentication;
using CloudShell.Secrets.Client;

var credential = new DefaultCloudShellResourceCredential();
var vault = SecretsVaultClient.FromEnvironment(credential);
var secret = await vault.GetSecretAsync("sample-api-key");
```

Applications that configure Secrets Vault endpoint discovery use variables
such as:

```text
CLOUDSHELL_SECRETS_<VAULT_NAME>_VAULT_ID
CLOUDSHELL_SECRETS_<VAULT_NAME>_ENDPOINT
CLOUDSHELL_SECRETS_<RESOURCE_ID>_VAULT_ID
CLOUDSHELL_SECRETS_<RESOURCE_ID>_ENDPOINT
```

The endpoint points at the protected vault secrets collection. The client
requests a resource identity token and sends it as a bearer token on each
service call.

The same package provides the configuration-provider integration for secrets:

```csharp
using CloudShell.Secrets.Client;

builder.Configuration.AddCloudShellSecretsVault(options =>
{
    options.VaultName = "Sample App Secrets";
});
```

Secret names are loaded as configuration keys. By default, `--` in secret
names maps to the .NET configuration `:` delimiter, matching the Azure Key
Vault-style convention. Provider diagnostics are exposed under
`CloudShell:SecretsVault:*`, including `Status`, `Detail`, `Source`, and
`LoadedKeys`.

## Stability

These SDK clients are public preview APIs. CloudShell owns the client
credential contract and the built-in service-client contracts, but package
names, constructor options, credential chain sources, and response types may
evolve before the MVP API is declared stable.
