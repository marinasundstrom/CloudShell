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

The domain separates related but distinct concepts:

- An identity provider is an environment capability. It belongs to the cloud
  plane and determines whether resource identity can be resolved at all.
- A user principal represents an authenticated human or operator.
- A resource principal represents a CloudShell resource acting through a
  resource identity binding.
- An app principal is the common resource-principal case for executable,
  project, container app, or managed application resources. It is not a
  separate credential mechanism; it is a resource identity attached to an app
  resource.
- A device principal represents a device enrolled through a Device Registry.
  It is backed by a `deviceIdentity` principal category so devices can be
  granted access without pretending they are application resources.
- A resource identity binding is per-resource intent. It says that a resource
  has, or requires, an identity that can later be resolved and provisioned by a
  provider.
- A principal or actor is the identity that performs an operation. This follows
  common IAM terminology: users, groups, service accounts, service principals,
  managed identities, workload identities, and provider-owned identities are
  all possible principals. Resource identities are the first principal source
  CloudShell can resolve today.
- A permission grant authorizes a principal to perform an operation against a
  target resource.
- Resource events and activity logs should record the acting principal so users
  can see which resource or user triggered an operation.

The current Resource Manager Access control view is intentionally narrower
than the full domain. It uses resources with identity bindings as a transitional
principal source because those are the principals CloudShell can currently
resolve and display. The durable model is not resource-to-resource access
control; it is principal-to-resource grants. Target resources do not
conceptually need their own identity to be protected.

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
Providers can come from `ResourceIdentity` host configuration, built-in identity
host setup, or host-level provider registration.

| Binding kind | Selection rule |
| --- | --- |
| `Provider` | Resolve by `ProviderId`. |
| `Required` | Resolve to the configured or host-registered default provider. If exactly one provider is available, that provider is the implicit default. |

When multiple providers are available, set a default explicitly for `Required`
identity bindings. Hosts can use `ResourceIdentity:DefaultProviderId`, built-in
identity host setup, or `controlPlane.AddIdentityProvider(..., useAsDefault: true)`.
If a binding cannot resolve to a configured or host-registered provider,
Resource Manager reports a `resourceIdentityProviderUnresolved` resource model
diagnostic.

```csharp
controlPlane.AddIdentityProvider(
    "identity:dev",
    "Development Identity",
    ResourceIdentityProviderKind.BuiltIn,
    useAsDefault: true);

controlPlane.DefineResources(resources =>
{
    var api = resources
        .Declare("applications.aspnet-core-project", "application:api")
        .RequireIdentity();
});
```

Control Plane resource-definition authoring can read host-registered
identity-provider metadata while declaring graph resources:

```csharp
controlPlane.AddIdentityProvider(
    "identity:dev",
    "Development Identity",
    ResourceIdentityProviderKind.BuiltIn,
    useAsDefault: true);

controlPlane.DefineResources(resources =>
{
    var identityProvider = resources.GetIdentityProvider("identity:dev");

    resources
        .AddAspNetCoreProject("api", apiProjectPath)
        .WithIdentity(identityProvider);
});
```

`controlPlane.AddIdentityProvider(...)` registers provider metadata with the
Control Plane declaration model. `resources.GetIdentityProvider(...)` only
reads that host context while building graph resources; it does not declare an
identity provider resource and it is not part of the `ResourceDefinition`
interchange format. The built-in identity setup also adds its built-in identity
provider at host level, not as a resource. Future identity-provider resources
may be modeled separately, but they are intentionally out of scope for the
current ResourceDefinition authoring model. When the provider has a
provisioning resource, callers must have
`CloudShell.Identity/provisioningServices/identities/provision/action` or
`resources.manage` on that resource before provisioning identities. The
provisioning resource is not required to be the identity provider itself; it can
be a third-party service that calls an external authority API.

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

For local development and tests, hosts can register the built-in provider with
an in-memory ASP.NET Core Identity store. It registers provider metadata,
creates configured test users as login accounts, exposes them as user
principals, maps programmatic grants to resource-permission claims, and clears
that state when the process stops.

The in-memory user name is the CloudShell principal key used by grants and
directory lookups. Built-in Identity sign-in remains email-only unless the host
explicitly enables `Authentication:BuiltInIdentity:AllowUserNameSignIn`.

```csharp
controlPlane.ConfigureInMemoryIdentity(identity =>
{
    identity.Users.Add(
        "alice",
        password: "CloudShell123!",
        displayName: "Alice Local Developer",
        email: "alice@example.test",
        role: "CloudShell.Reader");
});

controlPlane.DefineResources(resources =>
{
    var identity = resources.GetIdentityProvider();
    var alice = identity.GetUser("alice");

    var database = resources.Declare("database", "database:app");

    database.Allow(alice, "Database/databases/manage/action");
});
```

`resources.GetIdentityProvider()` returns a Control Plane identity-provider
context, not a Resource graph node. `GetUser(...)` creates a provider-scoped
principal reference using the selected provider ID. User existence and
credentials remain owned by the configured identity provider and are validated
by the provider/authentication path rather than by the resource template.

Persisted users for the built-in provider use the same ASP.NET Core Identity
manager path backed by the configured database store and are managed by the
account UI. The in-memory setup exists so local development and sample projects
can model users, login, roles, claims, and access grants without keeping state
after shutdown. Third-party providers should feed the same principal and grant
model through their own directory/provisioning integrations.

`CloudShell.LocalDevelopmentHost` registers the built-in provider as the
default resource identity provider for launcher-based local development.
Launcher appsettings can define local development principals under
`ResourceIdentity:BuiltIn:Users`; the built-in provider exposes those users in
the Access control principal directory even when they are only host
configuration and not resource-template declarations.

Programmatic declarations can also record permission grants from a principal
to a target resource. A resource identity is one principal source, exposed by
the declaring resource through `Principal`; it is separate from the resource's
`Identity` binding configuration.

```csharp
var api = resources
    .AddAspNetCoreProject("api", "../Api/Api.csproj")
    .WithDisplayName("API")
    .WithIdentity("development", name: "api-service");

var database = resources.Declare("database", "database:app");

database.Allow(api.Principal, "Database/databases/readWrite/action");
```

The same intent is encoded in resource templates as ordinary attributes. This
is the cross-launcher contract for C#, Java, JavaScript/TypeScript, and future
resource builders: the subject resource carries `identity.*`, and each target
resource carries inbound `access.grants`.

```yaml
resources:
  - type: application.executable
    name: api
    identity:
      kind: provider
      providerId: identity:development
      name: api-service

  - type: application.rabbitmq
    name: rabbitmq
    access:
      grants:
        - principal:
            kind: resourceIdentity
            id: application.executable:api/identities/api-service
            providerId: identity:development
            sourceResourceId: application.executable:api
            sourceIdentityName: api-service
          permission: CloudShell.Messaging/rabbitMQ/publish/action
```

Those attributes are Resource Manager declaration metadata. They do not define
provider-native credentials for RabbitMQ, SQL Server, or any other backing
service. Providers can use the declared grants to reconcile native users or
permissions, but the native credential material stays provider-owned and must
not be exposed through identity attributes, logs, diagnostics, samples, or
templates.

Use `ResourceAccessPermissions` when the grant represents a resource access
level instead of a provider-specific operation string:

```csharp
frontend.Allow(api, ResourceAccessPermissions.Reference);
api.Allow(frontend, ResourceAccessPermissions.Read);
api.Allow(frontend, ResourceAccessPermissions.Manage);
api.Allow(frontend, ResourceAccessPermissions.Operate(
    CommonResourceOperationPermissions.LifecycleAction));
```

These grants can be evaluated against the declaration model with
`ResourcePermissionGrantEvaluator`, and the Control Plane exposes read/evaluate
operations through `IResourceManager` and the HTTP API:

```text
GET /api/control-plane/v1/resource-principals
GET /api/control-plane/v1/resource-permission-grants
GET /api/control-plane/v1/resource-permission-grants/status
POST /api/control-plane/v1/resource-permission-grants/evaluate
POST /api/control-plane/v1/identity-providers/{providerId}/setup
POST /api/control-plane/v1/resources/{resourceId}/identity/provision
GET /api/control-plane/v1/resources/{resourceId}/identity/provisioning-status
```

Grant evaluation answers whether the declared model contains a matching grant.
Grant status answers whether a provider reports the requested grant as
effectively applied, pending, failed, drifted, not applied, or unknown.
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
operation without requiring every provider to implement it. The projected
identity provisioning resource also exposes a `setupIdentityProvider` Resource
Manager action that runs the setup hook for the attached provider with the same
permission requirement. If a provisioning resource is projected without a
matching provider definition, Resource Manager reports an action availability
reason instead of dispatching setup to a provider.

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
separate generated Identity tab. Identity and Access control appear when the
environment has a default resource identity provider, including a default
selected by programmatic resources. The Identity tab reflects whether identity
is enabled for the selected resource with an `Enable identity` checkbox, lists
declared permission grants, exposes the provisioning command when the selected
resource has an identity binding, and shows provider-reported provisioning
status and diagnostics for that resource identity. Enabling identity from
Resource Manager stores a `Required` binding through
`SetResourceIdentityAsync(...)`, so the binding resolves through the
environment default provider instead of hard-coding a provider ID.

The generated Access control tab shows who can access the current resource.
It treats the current resource as the protected target and does not require the
target to have its own identity binding. For the current MVP, the assignable
principals come from the Control Plane principal lookup API, filtered to
resource identities because resource identities are the assignable principal
type this slice can provision and reconcile. The tab offers a searchable
principal picker, records grant intent from the selected principal to the
current resource, groups assigned permissions by principal, and can revoke
those grants. When the current resource itself has an identity, that identity
is not included in the assignment picker. The permission picker is filtered to
operations that are relevant to the current target resource, such as
configuration-entry reads for Configuration Store resources, secret reads for
Secrets Vault resources, mount permissions for volumes, networking
reconciliation for network resources, and resource-action permissions where the
target advertises actions. User, group, service account, and provider-owned
principal references require grant commands and Resource Manager assignment
surfaces beyond resource identities.

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

Identity providers integrate through a standard provider-neutral adapter
surface:

- `IResourceIdentityProviderSetupHandler` sets up or reconciles
  provider-level infrastructure, such as clients, audiences, roles, groups,
  token mappers, or a bridge service.
- `IResourceIdentityDirectoryProvider` queries identity data from the backing
  directory, such as users, groups, service principals, managed identities,
  workload identities, and provider-owned identity references.
- `IResourceIdentityProvisioner` provisions or reconciles resource identities
  and the matching access grants for those identities.
- `IResourceIdentityProvisioningStatusProvider` reports observed
  provisioning and grant-reconciliation state from provider-owned data.
- `IResourceIdentityCredentialEnvironmentProvider` supplies runtime credential
  acquisition settings when a selected provider needs workload-specific token
  configuration.

The Control Plane builds provisioning requests from declared resource
identities and matching permission grants, grouped by resolved resource
identity provider. A concrete provider adapter can call the backing authority
directly, or it can call a provider-owned web service that translates the
CloudShell request into Microsoft Entra ID, Active Directory, Keycloak,
OIDC/OAuth, RBAC, app-role, group, or provider-native records.

The built-in ASP.NET Core identity provider is the reference implementation for
simple local-development environments. It provisions resource identity clients,
issues scoped resource-permission tokens, reports provisioning status, and
exposes provisioned resource identity clients through the same directory hook
that third-party providers implement. The Control Plane also lists declared
resource identities directly so Resource Manager can assign resource-to-resource
grants before the first provisioning run.

## Operation Permissions

Resource access is evaluated as an ordered effective level:

| Level | Meaning |
| --- | --- |
| `None` | The resource is not disclosed. |
| `Reference` | The resource may appear as a locked or redacted relationship node but cannot be inspected. |
| `Read` | The caller can inspect resource details and non-secret operational data. |
| `Operate` | The caller can execute at least one resource operation and can inspect the resource. |
| `Manage` | The caller can administer the resource and use management-level operations. |

`resources.reference` grants reference-level discovery. `resources.read`
grants read-level inspection. Resource operation permissions grant
operate-level access for matching actions. `resources.manage` grants
manage-level access and remains the compatibility superset for resource
actions.

Observability uses a separate grouped permission, `observability.read`, while
usage statistics use `usage.read`. Telemetry signal-specific permissions are
`observability.logs.read`, `observability.traces.read`, and
`observability.metrics.read`. These determine
which telemetry areas a caller can enter. Telemetry records are still filtered
to resources where the caller has read-level resource access.

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
| `application.rabbitmq` | publish to broker | `RabbitMQResourceOperationPermissions.Publish` |
| `application.rabbitmq` | consume from broker | `RabbitMQResourceOperationPermissions.Consume` |
| `application.rabbitmq` | configure broker resources | `RabbitMQResourceOperationPermissions.Configure` |
| `application.rabbitmq` | `application.rabbitmq.reconcile-access` | `RabbitMQResourceOperationPermissions.ReconcileAccess` |
| Resource identity provisioning service | resource identity provisioning | `ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities` |

When adding a new resource action, document the operation permission in this
catalog. Prefer a resource-type-specific operation for meaningful provider or
platform actions. Use the generic custom-action execute permission only when no
narrower operation exists yet.

## Local Development

When `Authentication:Enabled` is `false` and
`Authentication:EvaluateClaimsWhenDisabled` is not set, CloudShell runs in the
permissive local-development mode. The Control Plane API is unauthenticated,
user authorization is not enforced at the boundary, and all resource,
resource-action, and observability operations are allowed. Resource identity
metadata can still be projected and diagnosed. This allows local hosts,
templates, providers, and Resource Manager UI surfaces to exercise the intended
identity shape before a production identity provider is configured.

Local permission-boundary tests can set
`Authentication:EvaluateClaimsWhenDisabled` to `true` and provide a mock
authenticated principal. CloudShell then evaluates the normal permission,
resource-group, resource, and resource-permission claims even though the
authentication pipeline itself is disabled.

In-memory users belong to the built-in identity provider. They are useful for
turning on real sign-in and resource grants in local samples, but they do not
change the permissive behavior unless authentication is enabled or a test opts
into disabled-authentication claim evaluation.

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
