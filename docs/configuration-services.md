# Configuration Services

CloudShell includes a configuration provider that contributes `configuration.store`
resources. Each resource is a separate local configuration store with its own
settings, endpoint, resource identity metadata, and resource group assignment.
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
Certificates are also vault-backed sensitive values, but resources should use
typed `CertificateReference` values instead of generic secret references when a
certificate is expected, such as load balancer HTTPS bindings.

## Resource Model

A configuration service is added from `/resources/add` by choosing
**Configuration service**. It can be assigned to any resource group, or left
in the default group.

Control Plane hosts can also declare configuration services in checked-in
startup code:

```csharp
controlPlane.DefineResources(resources =>
{
    resources
        .AddConfigurationStore("example")
        .WithDisplayName("Example Configuration")
        .WithEndpoint("http://localhost:5138");
});
```

Configuration Store settings, secret values, and certificate payloads are
provider/runtime data.
Templates may include create-only seed attributes for local development:
`seed.settings` on a new Configuration Store, and `seed.secrets` or
`seed.certificates` on a new Secrets Vault. The `seed` hierarchy is reserved
for create-time input and is not accepted when updating an existing resource.
The Control Plane materializes those values into provider-owned runtime state
after the create commit succeeds, then strips the seed attributes from accepted
graph state. Normal resource-template export does not emit seeded settings,
secret values, or certificate payloads.

Each store stores key-value settings:

- `Name`: the setting name.
- `Value`: the stored value.

The built-in Configuration Store accepts broad App Configuration-style setting
names. Empty names, `%`, `.`, `..`, and control characters are rejected. Both
`Orders:Api:BaseUrl` and the portable `Orders--Api--BaseUrl` hierarchy form
are accepted; the CloudShell `IConfiguration` client maps `--` to `:` when it
loads settings.

Secrets Vault uses Key Vault-style secret names: 1-127 ASCII letters, digits,
and hyphens. Use names such as `Orders--Api--ClientSecret` for hierarchical
application configuration. The Secrets Vault `IConfiguration` client maps
`--` to `:` when it loads secrets.

These naming rules belong to the built-in providers. Other providers or cloud
deployment targets may apply their own character and length restrictions.

## Resource Manager Management

Graph-backed Configuration Store resources contribute a **Settings** tab in
Resource Manager when the UI host has access to the provider runtime manager.
That tab manages provider-owned runtime settings and rewrites the sidecar
definition file used by the backing Configuration Store service. Setting values
do not become Resource graph attributes.
The `configuration.store.inspect` operation uses the provider-execution
boundary. The built-in resource-type registration supplies a runtime inspector;
if a host or test constructs the operation path without one, the inspect action
is unavailable with a missing-inspector reason and direct provider-execution
calls return the same diagnostic instead of reporting a silent no-op success.

Host configuration source inspection follows the same boundary. A
`configuration.host.inspect` operation is projected for graph-backed host
configuration source resources, but it is unavailable when no host
configuration source inspector is registered. Direct provider-execution calls
return a missing-inspector diagnostic instead of treating the absent adapter as
a successful empty inspection.

Graph-backed Secrets Vault resources contribute **Secrets** and
**Certificates** tabs under the same host/runtime boundary. Existing secret and
certificate values are masked in the UI and preserved unless a replacement
value is entered. Certificate values can be pasted, uploaded as PEM or PFX/P12
content, or generated as a self-signed development certificate from the
Certificates tab. Secret values and certificate payloads stay in
provider-owned runtime state and sidecar definition files; Resource Manager
only projects non-secret metadata such as counts, content type, thumbprint,
subject, validity dates, and private-key presence.
The `secrets.vault.inspect` operation also uses the provider-execution
boundary. The built-in resource-type registration supplies a runtime
inspector; if the operation path is constructed without one, the inspect action
is unavailable with a missing-inspector reason and direct provider-execution
calls return the same diagnostic.

Load balancer HTTPS entrypoints can reference a Secrets Vault certificate
through a typed certificate reference. The load balancer resource stores only
the vault resource ID, certificate name, and optional version. The Traefik load
balancer provider can resolve PEM certificates from the vault and write
provider-owned certificate/key files for HTTPS entrypoints. Issuer/renewal
flows, PFX materialization, and provider-specific TLS handling beyond Traefik
remain future work.

Provider-owned state is persisted in:

```text
CloudShell.Host/Data/configuration-stores.json
```

The core CloudShell database still stores only platform metadata such as the
resource registration and group assignment.

The create-only seed path is intended for development launchers, samples, and
local environments. Production or on-premise hosts should treat seeded secret
values as sensitive template input and prefer a permission-protected
secret-import path when that workflow is added. Secrets Vault secret and
certificate settings carry a `Version`, and references may request a specific
version. If a runtime write omits the version, CloudShell generates one. If a
write changes the payload for an existing version, the previous payload is kept
and the changed payload is stored as a new generated version. When a reference
omits the version, the runtime resolves the last matching setting for that name.
Durable retention policy, activation/disabled state, soft-delete, and
import/export semantics remain future vault work. Configuration Store setting
versioning is not supported yet.

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
stderr, and lifecycle settings are available without modeling the service as an
`application.executable` resource.

The runtime is intentionally an implementation detail of the configuration
resource type. Today it is a host-local process for development; a future
configuration provider can replace that with a container image while keeping the
resource model and logs attached to the configuration service resource.

## Future Container-Backed Instances

Configuration Store and Secrets Vault resources should continue to be modeled
as resource instances, not as special Control Plane services. A
`configuration.store` resource is one configuration store. A `secrets.vault`
resource is one vault. The service process or container that serves that
instance is the resource's runtime realization.

The current implementation already gives each instance an isolated host-local
process and provider-owned state, which is sufficient for the local-development
MVP. CloudShell should not prioritize replacing that working isolation before
the app-centric MVP is stable. The future path is to let a provider materialize
the same resource instance as a container-backed runtime when a host profile or
environment requires stronger isolation, operational packaging, or more
production-like lifecycle behavior.

That future shape should preserve the same user-facing resource model:

- the Control Plane owns the `configuration.store` or `secrets.vault` resource,
  lifecycle policy, permissions, dependency relationships, endpoint projection,
  logs, health, and Resource Manager operations
- the provider owns the runtime implementation, state format, seed import,
  secret/configuration data plane, and diagnostics needed to operate the
  instance
- the execution boundary materializes the provider runtime as a local process,
  container, external managed service, or future agent assignment without
  changing the authored resource definition
- other resources reference the store or vault by resource ID, not by the
  process ID, container ID, or host-local implementation details

This is the same direction as SQL Server and other provider-backed service
resources: the resource is the stable management and dependency boundary, while
the backing process or container is an implementation detail. Runtime helper
containers, sidecars, process handles, data files, and provider caches should
remain provider-owned runtime state unless they need their own independent
resource identity for diagnostics, authorization, lifecycle actions, or
relationships.

When the execution/agent boundary is introduced, Configuration Store and
Secrets Vault providers should be able to move their local service startup
behind that boundary as typed provider execution. The Control Plane should
still orchestrate desired state and observe status through the provider
contracts; it should not require users to author a separate app or container
resource just to get an isolated vault or configuration store.

Lifecycle actions require a Configuration Store runtime controller. The built-in
provider registration supplies the local process runtime controller for normal
hosts. If a custom or direct operation path is constructed without that
controller, Resource Manager projects the lifecycle action as unavailable with a
missing-controller reason, and direct provider-execution calls return the same
readiness failure as a diagnostic instead of succeeding as a no-op.

Secrets Vault lifecycle actions use the same contract. The built-in provider
registration supplies the local process runtime controller for normal hosts,
while custom or direct operation paths without a Secrets Vault runtime
controller project unavailable actions and return missing-controller
provider-execution diagnostics.

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
GET <configuration-service-endpoint>/api/configuration/stores/{resource-id}/settings
GET <configuration-service-endpoint>/api/configuration/stores/{resource-id}/settings/{name}
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
`ConfigurationStoreResourceOperationPermissions.ReadSettings` on the target
configuration store resource. Missing tokens return `401`; invalid tokens,
missing services, or missing grants return an unavailable or access-denied
result from the caller's perspective.

This is intentionally the same integration model authored Web APIs should use.
Use the public-preview `ConfigurationStoreClient` from
`CloudShell.Configuration.Client` for direct Configuration Store service calls.
See [SDK clients](sdk-clients.md) for package boundaries and client usage.
The experimental TypeScript package under
`sdk/typescript/configuration-client` mirrors the same direct client shape for
Node.js applications: it discovers injected Configuration Store endpoints,
sends bearer tokens, reads all settings or a single setting, and can map portable
`--` setting names to `:` configuration keys.
The `samples/TypeScriptConfigurationClient` sample shows the same flow from a
Node.js application by setting
`CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT` and a bearer token.
JavaScript app resources now also derive Configuration Store and Secrets Vault
SDK endpoint variables from graph references, including resources projected as
`application.container-app`. `samples/TypeScriptContainerApp` uses that path
from a TypeScript-launcher-authored Node.js container app and reads both
services through `sdk/typescript/configuration-client` using the injected
workload identity.
The experimental Go package under `sdk/go/cloudshell` provides the same direct
Configuration Store client shape for Go workloads, using the default credential
chain documented in [SDK clients](sdk-clients.md). The
`samples/GoContainerApp` launcher sample shows the same flow by declaring
Configuration Store and Secrets Vault resources, granting a container app
identity read access, and launching a Go container app that reads both services
from the `/configuration` endpoint without returning secret values.
`samples/JavaContainerApp` and `samples/PythonContainerApp` provide the same
Configuration Store, Secrets Vault, and identity proof for Java and Python
container apps.

Applications that depend on a Secrets Vault receive:

```text
CLOUDSHELL_SECRETS_<VAULT_NAME>_VAULT_ID
CLOUDSHELL_SECRETS_<VAULT_NAME>_ENDPOINT
CLOUDSHELL_SECRETS_<RESOURCE_ID>_VAULT_ID
CLOUDSHELL_SECRETS_<RESOURCE_ID>_ENDPOINT
```

The endpoint points at the protected vault secrets collection. Use the
public-preview `SecretsVaultClient` from `CloudShell.Secrets.Client` for direct
Secrets Vault service calls. The same client can read certificate metadata and
certificate values from the sibling protected certificate collection. See
[SDK clients](sdk-clients.md).
Both the direct client and resource graph references support optional
versioned secret and certificate reads; omitting the version resolves the
current last matching vault setting.

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

Loaded settings are available through normal `IConfiguration` lookup:

```csharp
var value = builder.Configuration["SampleMessage"];
```

Provider diagnostics are exposed under `CloudShell:ConfigurationStore:*`,
including `Status`, `Detail`, `Source`, and `LoadedKeys`. The
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
Secrets are read through the separate Secrets Vault client and masked in that
response.

See [Programmatic resources](programmatic-resources.md) for the declaration and
persistence model.

## Templates

Configuration services support resource group templates. Export includes
setting values because Configuration Store settings are not secret material.
Secrets should be stored in Secrets Vault resources. Import creates a new
configuration service and generates a fresh access token.
