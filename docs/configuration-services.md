# Configuration Services

CloudShell includes a configuration provider that contributes `configuration.store`
resources. Each resource is a separate local configuration store with its own
entries, endpoint, resource identity metadata, and resource group assignment.
Each store owns the runtime process that serves its HTTP API; it does not
register that process as a separate application resource.

CloudShell also includes a separate Secrets provider that contributes
`secrets.vault` resources and secret-reference resolution. Hosts can call
`AddSecretsProvider()` when they only need Secrets Vault resources. Existing
hosts that call `AddConfigurationProvider()` still get both configuration store
and Secrets Vault support for compatibility.

Use separate configuration services when different projects or resource groups
need different non-secret settings. Secrets should be modeled as
`secrets.vault` references so settings and credentials remain separate.

## Resource Model

A configuration service is added from `/resources/add` by choosing
**Configuration service**. It can be assigned to any resource group, or left
in the default group.

Control Plane hosts can also declare configuration services in checked-in
startup code:

```csharp
controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("example")
        .WithDisplayName("Example Configuration")
        .WithEntries(
        [
            new("SampleMessage", "Hello from CloudShell configuration"),
            new("SampleSecret", "local-development-secret", IsSecret: true)
        ]);
});
```

Each store stores key-value entries:

- `Name`: the setting name.
- `Value`: the stored value.
- `Secret`: legacy sensitive-entry marker. New authoring should prefer
  Secrets Vault references for credentials and other secret values.

The built-in Configuration Store accepts broad App Configuration-style setting
names. Empty names, `%`, `.`, `..`, and control characters are rejected. Both
`Orders:Api:BaseUrl` and the portable `Orders--Api--BaseUrl` hierarchy form
are accepted; the CloudShell `IConfiguration` client maps `--` to `:` when it
loads entries.

Secrets Vault uses Key Vault-style secret names: 1-127 ASCII letters, digits,
and hyphens. Use names such as `Orders--Api--ClientSecret` for hierarchical
application configuration. The Secrets Vault `IConfiguration` client maps
`--` to `:` when it loads secrets.

These naming rules belong to the built-in providers. Other providers or cloud
deployment targets may apply their own character and length restrictions.

Provider-owned state is persisted in:

```text
CloudShell.Host/Data/configuration-stores.json
```

The core CloudShell database still stores only platform metadata such as the
resource registration and group assignment.

## Service Runtime

Each configuration store owns a local service process. For a store such as
`configuration:example`, the default process ID is:

```text
configuration-service-configuration-example
```

The current runtime implementation starts:

```bash
dotnet run --project CloudShell.ConfigurationStoreService/CloudShell.ConfigurationStoreService.csproj --no-launch-profile --urls http://localhost:5138
```

The actual URL is stored on the configuration store definition. If the user does
not provide one, the provider generates a stable endpoint from
`ServiceBasePort`, `ServiceHost`, and the resource ID. The default generated
range starts from:

```text
http://localhost:5138
```

Configure service instance defaults in the host through
`AddConfigurationProvider(...)`, including:

```text
ServiceBasePort
ServiceHost
ServiceUrlScheme
ServiceProjectPath
ServiceWorkingDirectory
ServiceProcessIdPrefix
```

Secrets Vault runtime defaults can be configured through either
`AddSecretsProvider(...)` or the compatibility `AddConfigurationProvider(...)`
registration:

```text
SecretsServiceBasePort
SecretsServiceProjectPath
SecretsServiceWorkingDirectory
SecretsServiceProcessIdPrefix
SecretsVaultDefinitionsPath
```

The service process receives the provider-owned store file path and its own
configuration resource ID through
`CloudShell:ConfigurationStoreService:DefinitionsPath` and
`CloudShell:ConfigurationStoreService:ResourceId`. That resource ID filter is
what keeps each process scoped to one configuration service instance.

Configuration services expose their own resource log in the Logs view. The log
uses the same local process runner as executable applications, so stdout,
stderr, and lifecycle entries are available without modeling the service as an
`application.executable` resource.

The runtime is intentionally an implementation detail of the configuration
resource type. Today it is a host-local process for development; a future
configuration provider can replace that with a container image while keeping the
resource model and logs attached to the configuration service resource.

## Application Access

Executable applications receive configuration service connection details through
resource dependencies. If an application depends on a configuration service,
CloudShell injects the configuration endpoint and resource ID when the process
starts:

```text
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_STORE_ID
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_STORE_ID
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_ENDPOINT
```

`<SERVICE_NAME>` and `<RESOURCE_ID>` are uppercased and normalized for
environment variable names. The resource-ID variables avoid collisions when two
groups use similarly named services.

Applications also need a credential acquisition path for their own resource
identity, such as the built-in development token endpoint used by the
Settings and Secrets sample:

```text
CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT
CLOUDSHELL_IDENTITY_CLIENT_ID
CLOUDSHELL_IDENTITY_CLIENT_SECRET
CLOUDSHELL_IDENTITY_SCOPE
```

Those identity variables are not configuration-store secrets. They represent
the provider-selected credential mechanism for the application resource
identity. A production provider may use a managed identity endpoint,
certificate, federated credential, workload identity, or another provider-owned
mechanism instead. The workload resource provider is responsible for injecting
the appropriate credential acquisition variables or endpoints when it starts
the process or container; application declarations should normally only assign
the identity with `WithIdentity(...)`.
Applications should use `DefaultCloudShellResourceCredential` from
`CloudShell.Client` when they need to acquire their own resource identity
token directly. The service-specific SDK clients and their `IConfiguration`
integrations use that credential chain internally.

Applications fetch settings from:

```text
GET <configuration-service-endpoint>/api/configuration/stores/{resource-id}/entries
GET <configuration-service-endpoint>/api/configuration/stores/{resource-id}/entries/{name}
```

The query-string route remains available for compatibility, but projected
resource endpoints use the path-based route because Microsoft service discovery
does not preserve query strings when resolving logical service URIs.

Pass a bearer token for the calling resource identity:

```text
Authorization: Bearer <token>
```

The configuration service runtime validates the token and requires a matching
resource-permission grant for
`ConfigurationStoreResourceOperationPermissions.ReadEntries` on the target
configuration store resource. Missing tokens return `401`; invalid tokens,
missing services, or missing grants return an unavailable or access-denied
result from the caller's perspective.

This is intentionally the same integration model authored Web APIs should use.
Use the public-preview `ConfigurationStoreClient` from
`CloudShell.Configuration.Client` for direct Configuration Store service calls.
See [SDK clients](sdk-clients.md) for package boundaries and client usage.

Applications that depend on a Secrets Vault receive:

```text
CLOUDSHELL_SECRETS_<VAULT_NAME>_VAULT_ID
CLOUDSHELL_SECRETS_<VAULT_NAME>_ENDPOINT
CLOUDSHELL_SECRETS_<RESOURCE_ID>_VAULT_ID
CLOUDSHELL_SECRETS_<RESOURCE_ID>_ENDPOINT
```

The endpoint points at the protected vault secrets collection. Use the
public-preview `SecretsVaultClient` from `CloudShell.Secrets.Client` for direct
Secrets Vault service calls. See [SDK clients](sdk-clients.md).

In this model, the caller owns a resource identity, obtains authentication evidence through
the selected identity provider, and the protected service validates that
evidence before applying CloudShell resource grants. A built-in configuration
service may own a specialized resource type and provider-owned runtime, but its
protected API should not use a private auth path that third-party services
cannot also use.

## Microsoft Configuration API

Applications can consume CloudShell configuration through the
`CloudShell.Configuration.Client` package. The same package that owns
`ConfigurationStoreClient` also owns the standard
`Microsoft.Extensions.Configuration` provider integration:

```csharp
using CloudShell.Configuration.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddCloudShellConfigurationStore();
```

By default, the provider discovers the first injected
`CLOUDSHELL_CONFIGURATION_*_ENDPOINT` and the matching
`CLOUDSHELL_IDENTITY_*` credential acquisition variables. To select a specific
service or configure explicit connection details:

```csharp
builder.Configuration.AddCloudShellConfigurationStore(options =>
{
    options.ServiceName = "Example Configuration";
    options.Timeout = TimeSpan.FromSeconds(5);
});
```

Loaded entries are available through normal `IConfiguration` lookup:

```csharp
var value = builder.Configuration["SampleMessage"];
```

Provider diagnostics are exposed under `CloudShell:ConfigurationStore:*`,
including `Status`, `Detail`, `Source`, `LoadedKeys`, and `SecretKeys`. The
provider does not throw when the service is unavailable; it records
unavailable status so the application can continue running and log the state.

Secrets Vault has its own service-specific configuration integration in
`CloudShell.Secrets.Client`:

```csharp
using CloudShell.Secrets.Client;

builder.Configuration.AddCloudShellSecretsVault();
```

By default, secret names are configuration keys and `--` maps to the .NET
configuration `:` delimiter. Provider diagnostics are exposed under
`CloudShell:SecretsVault:*`.

## Sample

The host declares an `Example Configuration` service programmatically. If an
executable application depends on that service, the sample app can use
`CloudShell.Configuration.Client` to read the injected CloudShell endpoint and
token, load settings at startup, log connection failures, and continue running
if the service is unavailable. It can use `CloudShell.Secrets.Client` the same
way for vault-backed secrets.

When the sample app is started from Resource Manager, open:

```text
http://localhost:5127/configuration
```

The sample returns the provider status and loaded keys from `IConfiguration`.
Secret values are masked in the response.

See [Programmatic resources](programmatic-resources.md) for the declaration and
persistence model.

## Templates

Configuration services support resource group templates. Export includes
non-secret entry values. Secret entries are exported as placeholders with an
empty value so templates do not leak secrets by default. Import creates a new
configuration service and generates a fresh access token.
