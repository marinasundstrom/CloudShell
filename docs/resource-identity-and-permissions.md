# Resource Identity and Permissions

Resource identity describes how a CloudShell resource represents itself when it
needs to call another resource, platform service, provider, or external
authority. Resource permissions describe which operations an identity or user
can perform on a resource.

User authentication is documented separately in
[Authentication and authorization](authentication-and-authorization.md). This
document focuses on resource-to-resource identity, resource identity provider
selection, projected identity metadata, and resource operation permissions.

## Resource Identity

Identity is part of the core resource model. It is not modeled only as a
`ResourceCapability`, because callers need a stable way to inspect a resource's
identity intent without first interpreting provider-specific capabilities.

A resource may still advertise identity-related capabilities when a provider
supports extra behavior such as token issuance, managed identity provisioning,
protected API registration, or permission assignment. The identity binding
itself remains resource metadata.

## Identity Providers

Resource identity providers are configured independently from the user sign-in
provider. A provider definition names the identity system that can resolve
workload identity metadata or eventually issue and validate tokens for a
resource. A provider definition may also name a separate provisioning resource:
that resource represents the CloudShell-managed hook or third-party service
that can register identities and grants with the selected authority.

```json
{
  "ResourceIdentity": {
    "DefaultProviderId": "identity:entra",
    "Providers": [
      {
        "Id": "identity:entra",
        "Name": "Microsoft Entra ID",
        "Kind": "Oidc",
        "ProvisioningResourceId": "identity-provisioner:entra",
        "Settings": {
          "Authority": "https://login.microsoftonline.com/{tenantId}/v2.0",
          "Audience": "api://cloudshell-control-plane"
        }
      }
    ]
  }
}
```

Supported provider kinds:

| Kind | Use |
| --- | --- |
| `BuiltIn` | CloudShell-owned or local built-in identity behavior. |
| `Managed` | Provider-managed identity systems. |
| `Oidc` | OIDC/OAuth providers such as Microsoft Entra ID, Keycloak, Auth0, or Okta. |
| `Custom` | Provider-specific or host-specific identity mechanisms. |

## Provider Selection

Resource identity bindings resolve through `ResourceIdentityProviderCatalog`.
Providers can come from `ResourceIdentity` host configuration or from
programmatic resource declarations.

| Binding kind | Selection rule |
| --- | --- |
| `Provider` | Resolve by `ProviderId`. |
| `Required` | Resolve to the configured or programmatically declared default provider. If exactly one provider is available, that provider is the implicit default. |

When multiple providers are available, set a default explicitly for `Required`
identity bindings. Hosts can use `ResourceIdentity:DefaultProviderId`;
programmatic declarations can call `resources.UseDefaultIdentityProvider(...)`.
If a binding cannot resolve to a configured or programmatically registered
provider, Resource Manager reports a `resourceIdentityProviderUnresolved`
resource model diagnostic.

```csharp
resources.AddIdentityProvider(
    "identity:dev",
    "Development Identity",
    ResourceIdentityProviderKind.BuiltIn,
    useAsDefault: true);

var api = resources
    .Declare("applications", "application:api")
    .RequireIdentity();
```

`resources.AddIdentityProvider(...)` registers provider metadata with the
declaration model. When the provider has a provisioning resource, callers must
have `CloudShell.Identity/provisioningServices/identities/provision/action` or
`resources.manage` on that resource before provisioning identities. The
provisioning resource is not required to be the identity provider itself; it
can be a third-party service that calls an external authority API.

## Identity Bindings

Resource identity metadata is projected through `Resource.IdentityBinding`.

```csharp
public enum ResourceIdentityBindingKind
{
    Provider,
    Required
}

public sealed record ResourceIdentityBinding(
    string? ProviderId,
    string? Subject = null,
    IReadOnlyList<string>? Scopes = null,
    IReadOnlyDictionary<string, string>? Claims = null,
    ResourceIdentityBindingKind Kind = ResourceIdentityBindingKind.Provider,
    string? Name = null);
```

`Provider` means the resource names a concrete provider. `Required` means the
resource requires an identity, but provider-specific details are resolved by
the default provider selection path.

Identity binding metadata must be non-secret. Do not put tokens, client
secrets, certificates, passwords, or other credentials in identity binding
claims, scopes, or provider settings.

## API Projection

The Control Plane API projects identity metadata on `ResourceResponse.identity`.

| Field | Meaning |
| --- | --- |
| `kind` | `Provider` or `Required`. |
| `name` | Stable CloudShell identity name, when declared. |
| `providerId` | Provider ID when the binding names a concrete provider. |
| `subject` | Provider-specific subject or workload name, when known. |
| `scopes` | Requested scopes or provider-specific permission hints. |
| `claims` | Non-secret provider-specific claim metadata. |

The remote Control Plane client maps this response back to
`Resource.IdentityBinding`.

## Programmatic Declarations

Programmatic resource declarations can attach identity intent before a real
identity provider exists. The initial authoring model supports one optional
identity binding per resource:

```csharp
var api = resources
    .AddAspNetCoreProject("api", "../Api/Api.csproj")
    .WithDisplayName("API")
    .WithIdentity(identity =>
    {
        identity.Name = "api-service";
        identity.Provider = "development";
        identity.Subject = "application:api";
        identity.Scopes.Add("database.read");
        identity.Claims.Add("resource", "application:api");
    });

resources
    .AddContainer("worker", "ghcr.io/example/worker:latest")
    .RequireIdentity(["queue.read"], name: "worker-service");
```

`WithIdentity(...)` declares a concrete provider binding. `RequireIdentity(...)`
declares that the resource must have an identity even when the provider is not
known yet. Resource Manager projects the declaration through
`Resource.IdentityBinding`; if the provider cannot be resolved, it reports a
`resourceIdentityProviderUnresolved` diagnostic instead of rejecting the
resource.

Future mock identity support should go beyond projecting metadata. It should
let local tests declare user or workload principals and exercise permission
boundaries between resources before the same model is connected to Microsoft
Entra ID or another production authority.

Programmatic declarations can also record permission grants from one resource
identity to another resource:

```csharp
var api = resources
    .AddAspNetCoreProject("api", "../Api/Api.csproj")
    .WithDisplayName("API")
    .WithIdentity("development", name: "api-service");

var database = resources.Declare("database", "database:app");

database.Allow(api.Identity, "Database/databases/readWrite/action");
```

These grants can be evaluated against the declaration model with
`ResourcePermissionGrantEvaluator`, and the Control Plane exposes read/evaluate
operations through `IResourceManager` and the HTTP API:

```text
GET /api/control-plane/v1/resource-permission-grants
POST /api/control-plane/v1/resource-permission-grants/evaluate
POST /api/control-plane/v1/identity-providers/{providerId}/setup
POST /api/control-plane/v1/resources/{resourceId}/identity/provision
GET /api/control-plane/v1/resources/{resourceId}/identity/provisioning-status
```

Grant evaluation answers whether the declared model contains a matching grant.
Resource action execution can also carry an explicit acting resource identity;
when it does, Resource Manager evaluates declared grants for that identity
instead of falling back to the current user's resource permissions. This is a
model-level enforcement path for programmatic identities and tests.

The CloudShell resource owns its identity, capabilities, and resource-level
permission requirements. The managed workload behind that resource, such as a
process, container, configuration service, or Secrets Vault service, is
responsible for using the information passed by its provider. Providers should
transfer identity and secret material through safe runtime mechanisms such as
environment variables, mounted configuration, token endpoints, or platform
managed identity facilities without storing raw credentials in the resource
model.

This rule applies to built-in CloudShell resource services as well as authored
services. Configuration Store, Secrets Vault, and future CloudShell-owned
helper services should dogfood the same resource identity, credential
acquisition, access-grant, and protected-service API contracts that extension
authors use for their own Web APIs. A built-in resource type can own specialized
provider configuration and runtime state, but it should not get a separate
identity mechanism unless the exception and replacement path are documented.
`ResourcePermissionClaimAuthorization` in `CloudShell.Abstractions` is the
shared helper for protected services that need to evaluate scoped
`cloudshell.resource-permission` claims. It is a public preview API until the
protected-service authentication surface is finalized for MVP.
`DefaultCloudShellResourceCredential` is the matching public-preview credential
chain for services that need to obtain authentication evidence for their own
resource identity. Its first source reads the injected `CLOUDSHELL_IDENTITY_*`
environment contract; later sources should extend the same chain instead of
adding service-specific token acquisition code. Environment variables are the
current runtime injection convention for executables and containers, but local
development identities can later be resolved from a file or developer profile
on disk through the same credential chain.

The workload resource provider owns projecting that credential acquisition
mechanism into the process or container it starts. Authored resources should
declare an identity binding such as `WithIdentity(...)` and grants; they should
not normally declare `CLOUDSHELL_IDENTITY_*` variables manually.
Built-in identity can be projected directly by the application provider. Other
identity providers can implement `IResourceIdentityCredentialEnvironmentProvider`
to supply the same runtime environment contract for a resolved resource
identity. That keeps provider-specific client IDs, secrets, token endpoints,
files, or future managed-identity endpoints behind the provider while authored
services continue to use `DefaultCloudShellResourceCredential`.

Protected CloudShell service APIs validate runtime credentials separately from
credential acquisition. Configuration Store and Secrets Vault use the shared
CloudShell bearer middleware to accept either built-in authority tokens or
configured external OIDC/OAuth JWT bearer tokens, then apply
`ResourcePermissionClaimAuthorization` to the resulting principal. The token
issuer owns signing keys, client credentials, and claim mapping. CloudShell
only requires the validated principal to carry scoped
`cloudshell.resource-permission` claims for the target resource operation.

The Control Plane client supports the same SDK-style credential flow. Authored
services can pass a `CloudShellResourceCredential` to `RemoteControlPlane` or
to `AddRemoteControlPlane(...)` and use the domain-shaped `IControlPlane` or
manager interfaces without passing raw bearer tokens through each call. Future
service-specific SDK clients should reuse this credential contract.
`CloudShell.Configuration.Client` and `CloudShell.Secrets.Client` are the first
service-specific client packages that follow that pattern, and both depend on
the lightweight `CloudShell.Client` credential package instead of the full
Control Plane abstraction surface.

`ProvisionResourceIdentityAsync(resourceId)` asks the resolved identity
provider to provision one resource identity and its matching permission grants.
Programmatic declarations can request this when the Control Plane starts by
calling `ProvisionIdentityOnStartup()` after declaring an identity:

```csharp
var api = resources
    .AddAspNetCoreProject("api", "../Api/Api.csproj")
    .WithDisplayName("API")
    .WithIdentity("development", name: "api-service")
    .ProvisionIdentityOnStartup();
```

Startup provisioning is declarative desired behavior owned by the CloudShell
resource graph. The observed provisioned/not-provisioned state remains owned
by the selected identity provider. `GetResourceIdentityProvisioningStatusAsync`
and the HTTP status endpoint ask the provider for that observed state. Providers
with durable identity stores should answer from their provider-owned state,
such as service principals, app registrations, managed identities, app-role
assignments, or a provisioning service database. Providers that cannot report
status return `Unknown` with a diagnostic. The built-in development provider
reports whether its in-memory client registration currently exists.

`SetupResourceIdentityProviderAsync(providerId)` is separate from resource
identity provisioning. It asks a provider-level setup handler to reconcile or
validate provider-owned bootstrap such as OIDC client mappers, trust metadata,
admin API reachability, or provider-specific registration prerequisites. Setup
returns diagnostics and does not write provider-specific state into resource
metadata. When the provider names a provisioning resource, callers need
`CloudShell.Identity/provisioningServices/identities/provision/action` or
`resources.manage` on that provisioning resource. Providers without a setup
handler return a warning diagnostic so hosts can expose setup as an optional
operation without requiring every provider to implement it.

The first built-in provider implementation is development-oriented: it
registers an in-memory client-credentials client with the built-in authority
and projects declared grants as scoped resource-permission token claims. The
token also carries compatibility `cloudshell.permission` and
`cloudshell.resource` claims, but authorization prefers the scoped
`cloudshell.resource-permission` claim so a permission granted on one resource
cannot combine with a different resource claim. This makes the basic flow
demonstrable, including a Web API identity receiving read access to
Configuration Store and Secrets Vault resources. In that flow the Web API owns
the provisioned identity and the configuration store or vault is the protected
target resource; the target resource does not need its own identity unless it
later needs to call another resource or provider.

The Settings and Secrets sample verifies the provider-backed flow end to end:
the Web API receives a credential acquisition endpoint and identity client
credential, requests a bearer token from the built-in authority, and calls the
configuration and secrets backing services with scoped
`cloudshell.resource-permission` claims. The backing services no longer accept
configuration-store or vault-specific service tokens. External OIDC/OAuth
providers can be trusted by configuring the backing services'
`Authentication:ServiceBearer` settings. External authority registration for
API audiences and durable provider-backed client storage remain future work.

The built-in provider MVP is verified at the Control Plane API boundary. A
provisioned resource identity token can call an action on a resource only when
the token has the matching scoped operation permission for that target. A read
grant lets the API resolve the target resource, but it does not imply action
execution or identity-management permission.

The generated Resource Manager overview displays basic identity binding
metadata when a resource has one. Resource identity actions are isolated in a
separate generated Identity tab that appears only for resources with identity
enabled; that tab lists declared permission grants and exposes the provisioning
command. Editing identity bindings and permission grants in the CloudShell UI
is future work.

Managed identity behavior is also future work. A managed identity provider
should be able to resolve a resource identity binding and, where supported,
register or provision that identity and its grants with the backing authority.
That backing authority can be a built-in ASP.NET Core Identity-backed
development or team authority, but CloudShell resources should not depend on
ASP.NET Core Identity types. The stable contract remains the provider-neutral
resource identity binding and permission grant model so the same declarations
can later be reconciled with Microsoft Entra ID or another provider.

Full identity provisioning can involve identity providers, provisioning hooks,
and third-party registration services as separate resources. The current MVP
authorizes provisioning through `resources.manage` on the target resource and,
when the selected provider names a provisioning resource,
`CloudShell.Identity/provisioningServices/identities/provision/action` or
`resources.manage` on that provisioning resource.

Provisioning starts from the provider-neutral `IResourceIdentityProvisioner`
contract. Provider setup starts from the separate provider-neutral
`IResourceIdentityProviderSetupHandler` contract. Runtime credential injection
starts from `IResourceIdentityCredentialEnvironmentProvider` when a selected
provider needs to supply workload-specific token acquisition settings. The
Control Plane can build provisioning requests from declared resource identities
and matching permission grants, grouped by resolved resource identity provider.
A concrete provisioner, setup handler, or runtime credential provider
translates that request into its backing authority.

## Operation Permissions

Resource actions use Azure RBAC-style operation names. Resource Manager checks
the required operation permission before executing an action. `resources.manage`
currently remains a compatibility superset for resource actions.

Permission constants are grouped by scope. Cross-resource operation permissions
live in `CommonResourceOperationPermissions`. Resource-type-specific operation
permissions live in one class per resource type, such as
`NetworkResourceOperationPermissions` or
`LoadBalancerResourceOperationPermissions`. Older `CloudShellPermissions`
members remain compatibility aliases.

| Resource type or class | Action | Permission |
| --- | --- | --- |
| Any resource with standard lifecycle actions | `start`, `stop`, `pause`, `restart` | `CommonResourceOperationPermissions.LifecycleAction` |
| Any resource with a custom action and no narrower declared operation | custom action execution | `CommonResourceOperationPermissions.ExecuteCustomAction` |
| `configuration.store` and `ResourceClass.Configuration` | configuration entry read | `ConfigurationStoreResourceOperationPermissions.ReadEntries` |
| `cloudshell.network` and `cloudshell.virtualNetwork` | `reconcileEndpointMappings` | `NetworkResourceOperationPermissions.ReconcileEndpointMappings` |
| `cloudshell.loadBalancer` | `applyLoadBalancerConfiguration` | `LoadBalancerResourceOperationPermissions.ApplyConfiguration` |
| `secrets.vault` and `ResourceClass.SecretsVault` | secret value read | `SecretsVaultResourceOperationPermissions.ReadSecrets` |
| Resource identity provisioning service | resource identity provisioning | `ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities` |

When adding a new resource action, document the operation permission in this
catalog. Prefer a resource-type-specific operation for meaningful provider or
platform actions. Use the generic custom-action execute permission only when no
narrower operation exists yet.

## Local Development

When `Authentication:Enabled` is `false`, CloudShell does not enforce
token-based user authorization at the Control Plane boundary. Resource identity
metadata can still be projected and diagnosed. This allows local hosts,
templates, providers, and Resource Manager UI surfaces to exercise the intended
identity shape before a production identity provider is configured.

Local permission-boundary tests can set
`Authentication:EvaluateClaimsWhenDisabled` to `true` and provide a mock
authenticated principal. CloudShell then evaluates the normal permission,
resource-group, resource, and resource-permission claims even though the
authentication pipeline itself is disabled.

Programmatic identity grants can still be evaluated in this mode. Passing an
explicit acting resource identity to a resource action checks the declared
grant model even when no token authority is configured. This is useful for
early permission-boundary tests. The Settings and Secrets sample covers the
provider-backed local proof by using the built-in authority and bearer tokens
against concrete configuration and secrets backing services.

Development identity providers should stay replaceable infrastructure. A local
provider can model deterministic subjects, scopes, and claims, while the same
resource identity model can later be backed by Microsoft Entra ID or another
provider.

## Related Design Work

The active proposal tracks unfinished design and implementation work:
[Identity and access proposal](proposals/core/identity-and-access.md).
