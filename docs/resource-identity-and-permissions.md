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
resource.

```json
{
  "ResourceIdentity": {
    "DefaultProviderId": "identity:entra",
    "Providers": [
      {
        "Id": "identity:entra",
        "Name": "Microsoft Entra ID",
        "Kind": "Oidc",
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
declaration model; it is not yet a first-class identity-provider resource with
its own lifecycle.

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
    .AddAspNetCoreProject("application:api", "API", "../Api/Api.csproj")
    .WithIdentity(identity =>
    {
        identity.Name = "api-service";
        identity.Provider = "development";
        identity.Subject = "application:api";
        identity.Scopes.Add("database.read");
        identity.Claims.Add("resource", "api");
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
    .AddAspNetCoreProject("application:api", "API", "../Api/Api.csproj")
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
POST /api/control-plane/v1/resources/{resourceId}/identity/provision
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

`ProvisionResourceIdentityAsync(resourceId)` asks the resolved identity
provider to provision one resource identity and its matching permission grants.
The first built-in provider implementation is development-oriented: it
registers an in-memory client-credentials client with the built-in authority
and projects declared grants as scoped resource-permission token claims. The
token also carries compatibility `cloudshell.permission` and
`cloudshell.resource` claims, but authorization prefers the scoped
`cloudshell.resource-permission` claim so a permission granted on one resource
cannot combine with a different resource claim. This makes the basic flow
demonstrable, including a Web API identity receiving read access to a Secrets
Vault-shaped resource. In that flow the Web API owns the provisioned identity
and the vault is the protected target resource; the vault does not need its own
identity unless it later needs to call another resource or provider. External
authority registration, durable client storage, and bearer token proof against
provider-backed workloads remain future work.

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

Full identity provisioning should treat identity providers as resources with
their own identities and permissions. The current MVP authorizes provisioning
through `resources.manage` on the target resource. A later slice should require
permission to provision or manage identities on the selected identity-provider
resource as well.

Provisioning starts from the provider-neutral `IResourceIdentityProvisioner`
contract. The Control Plane can build provisioning requests from declared
resource identities and matching permission grants, grouped by resolved
resource identity provider. A concrete provisioner translates that request into
its backing authority.

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
| Any resource with standard lifecycle actions | `run`, `stop`, `pause`, `restart` | `CommonResourceOperationPermissions.LifecycleAction` |
| Any resource with a custom action and no narrower declared operation | custom action execution | `CommonResourceOperationPermissions.ExecuteCustomAction` |
| `configuration.store` and `ResourceClass.Configuration` | configuration entry read | `ConfigurationStoreResourceOperationPermissions.ReadEntries` |
| `cloudshell.network` and `cloudshell.virtualNetwork` | `reconcileEndpointMappings` | `NetworkResourceOperationPermissions.ReconcileEndpointMappings` |
| `cloudshell.loadBalancer` | `applyLoadBalancerConfiguration` | `LoadBalancerResourceOperationPermissions.ApplyConfiguration` |
| `secrets.vault` and `ResourceClass.SecretsVault` | secret value read | `SecretsVaultResourceOperationPermissions.ReadSecrets` |

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

Programmatic identity grants can still be evaluated in this mode. Passing an
explicit acting resource identity to a resource action checks the declared
grant model even when no token authority is configured. This is useful for
early permission-boundary tests, but it is not a substitute for provider-backed
identity proof.

Development identity providers should stay replaceable infrastructure. A local
provider can model deterministic subjects, scopes, and claims, while the same
resource identity model can later be backed by Microsoft Entra ID or another
provider.

## Related Design Work

The active proposal tracks unfinished design and implementation work:
[Identity and access proposal](proposals/core/identity-and-access.md).
