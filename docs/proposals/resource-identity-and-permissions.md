# Resource Identity and Permissions Proposal

## Status

Current implementation focus.

This proposal is the first concrete resource-level slice of the broader
[Identity and Permissions Proposal](identity-and-permissions.md). It should be
worked with that platform foundation rather than as a separate late-stage
feature.

This proposal covers resource-to-resource identity and permission handling.
It intentionally does not define secret storage or secret references.

The current feature documentation lives in
[Resource identity and permissions](../resource-identity-and-permissions.md).
Keep this proposal focused on in-flight design, open questions, and remaining
implementation work.

## Problem

CloudShell already has a user/session authentication and authorization model,
but it does not yet define how resources should authenticate to one another,
or how resource-level permissions should be evaluated.

That makes it difficult to model scenarios such as:

- an API resource that uses a workload identity or managed identity
- a provider that needs a stable identity binding for service-to-service access
- resource-scoped authorization that is independent from the current user session

## Goals

- Introduce a resource identity-provider contract for resources and resource groups.
- Let Resource Manager select or inherit a default identity provider.
- Provide a separate development identity server instance that implements
  standard OIDC and OAuth 2.0 flows for local development and testing.
- Support Microsoft Entra ID (Azure AD) as a required external provider target
  through the same OIDC/OAuth contract.
- Support resource identity bindings for workload, service, and provider-owned
  scenarios.
- Extend the existing authorization model with resource-level permission concepts.

## Non-Goals

- Do not define secret storage, vaults, or secret references here.
- Do not replace the existing user authentication model.
- Do not make the development identity server part of the CloudShell domain
  model or a required production dependency.
- Do not require authentication to be enabled just to declare, inspect, or test
  resource identity shape in local development.
- Do not require every provider to implement all identity modes immediately.

## Proposed Model

### Resource identity providers

CloudShell should introduce a small identity-provider abstraction for resources:

```csharp
public sealed record ResourceIdentityProviderDefinition(
    string Id,
    string Name,
    ResourceIdentityProviderKind Kind,
    IReadOnlyDictionary<string, string>? Settings = null);
```

Supported kinds can include:

- `BuiltIn`
- `Managed`
- `Oidc`
- `Custom`

For local development, CloudShell should host a separate reference identity
server instance and register it as an OIDC-compatible provider. That server is
development infrastructure for issuing tokens, exercising client credentials,
and validating workload authentication. Production and team environments
must be able to use Microsoft Entra ID (Azure AD) through the same provider
contract, including issuer and audience validation, claim mapping, group or app
role mapping, and client-credentials/service-principal flows for automation.
Other standards-compliant providers such as Keycloak, Auth0, and Okta should
remain replaceable options without changing CloudShell's resource identity
model.

Programmatic resource declarations should be able to express identity intent as
part of normal resource authoring. A resource may bind to a concrete provider
identity, or it may declare that it will have an identity even when the concrete
provider, subject, scopes, or claims are supplied later by deployment policy or
environment configuration.

When CloudShell authentication is disabled for isolated local development,
resource identity declarations may still be accepted and projected. Runtime
authorization is bypassed in that mode, but the declared identity shape remains
valuable because applications, providers, templates, and Resource Manager can
exercise the same identity metadata they will use later with a real provider.

Local development can support a mock or development identity provider. That
provider could issue deterministic subjects, scopes, and claims or simply
project the declared binding without acquiring tokens. This lets a team start
an app locally, verify the intended identity contract, and gradually wire the
same resource to Microsoft Entra ID or another provider before publishing.

### Resource identity bindings

Resources should be able to declare which identity provider they rely on:

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
    ResourceIdentityBindingKind Kind = ResourceIdentityBindingKind.Provider);
```

This gives providers a stable contract for identity selection, workload identity,
scopes, and provider-specific metadata. `Provider` means the binding names a
resolved identity provider. `Required` means the resource declares identity
intent but expects provider-specific details to be resolved later.

The first projection slice adds `ResourceIdentityProviderDefinition` and
`ResourceIdentityBinding` public contracts. A resource can project an optional
identity binding with kind, provider ID when resolved, subject, scopes, and
non-secret claim metadata. The Control Plane API and remote client map that
binding through `ResourceResponse.identity`.

The first provider-selection slice adds `ResourceIdentityProviderCatalog`.
Concrete `Provider` bindings resolve by provider ID. `Required` bindings
resolve to the configured default provider; when exactly one provider is
registered, that provider is the default. Multiple providers require an
explicit default. Control Plane hosts can register providers and the default
through `ResourceIdentity` configuration. Resources with identity bindings that
cannot be resolved to a registered provider produce resource model diagnostics.
Resource-group or parent-resource inheritance and provider-backed token
behavior remain separate implementation work.

### Resource permissions

Resource permissions should extend the existing resource-group and resource
authorization model:

- `resource.identity.read`
- `resource.identity.manage`
- provider-specific permission names
- Azure RBAC-style resource action operations such as
  `CloudShell.Resources/resources/lifecycle/action`

The evaluation model should be able to reason about resource identity and
resource scope instead of relying only on user session claims.

The first implementation slice maps standard CloudShell resource actions to
operation permissions:

- `run`, `stop`, `pause`, and `restart` ->
  `CloudShell.Resources/resources/lifecycle/action`
- custom actions with a declared permission -> that declared Azure-style
  operation
- custom actions without a declared permission ->
  `CloudShell.Resources/resources/actions/execute/action`

Current resource-type and resource-class operation permissions:

Permission constants should follow the same structure as the resource model:
common cross-resource permissions live in `CommonResourceOperationPermissions`,
and resource-type-specific permissions live in a dedicated class per resource
type. The current compatibility aliases under `CloudShellPermissions` can stay,
but new resource operation permissions should use the explicit catalog classes.

| Resource type or class | Action | Permission |
| --- | --- | --- |
| Any resource with standard lifecycle actions | `run`, `stop`, `pause`, `restart` | `CommonResourceOperationPermissions.LifecycleAction` |
| Any resource with a custom action and no narrower declared operation | custom action execution | `CommonResourceOperationPermissions.ExecuteCustomAction` |
| `cloudshell.network` and `cloudshell.virtualNetwork` | `reconcileEndpointMappings` | `NetworkResourceOperationPermissions.ReconcileEndpointMappings` |
| `cloudshell.loadBalancer` | `applyLoadBalancerConfiguration` | `LoadBalancerResourceOperationPermissions.ApplyConfiguration` |

The existing `resources.manage` permission remains a compatibility superset
while the model moves toward resource operation permissions.

## Initial authoring surface

```csharp
resources.AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithIdentity(identity =>
    {
        identity.Name = "api-service";
        identity.Provider = "identity:dev";
        identity.Subject = "application:api";
        identity.Scopes.Add("db.read");
        identity.Claims["appRole"] = "Api";
    });

resources.AddContainerApplication("worker", "ghcr.io/example/worker:latest")
    .RequireIdentity(name: "worker-service");
```

Programmatic identity declarations are not limited to local or unauthenticated
development. A declaration can bind to a production identity provider, point at
a mock provider, or state only that the resource requires an identity whose
provider-specific details are resolved later. The mock-provider path is a
convenience for local development before wiring the same app to Microsoft Entra
ID or another production provider.

The first implemented surface supports one optional identity binding per
resource. That identity is stored on the programmatic declaration, projected
through `Resource.IdentityBinding`, and exposed through the Control Plane API
and remote client. This gives CloudShell a concrete model-building test case
without requiring a working token issuer or provider-backed identity lifecycle.

Mock identity support should later cover declared user or workload principals,
not only resource metadata. That would let local tests exercise permission
boundaries between resources before the same declarations are backed by
Microsoft Entra ID or another production authority.

The first grant authoring surface stores declarations such as
`target.Allow(source.Identity, permission)` in the programmatic declaration
store. The declaration model can evaluate those grants with
`ResourcePermissionGrantEvaluator`. Resource action execution now accepts an
explicit acting resource identity and, when supplied, evaluates the declared
grant model instead of falling back to the current user's resource permissions.
This is the first model-level enforcement path for programmatic identities, but
CloudShell does not yet prove that identity with a token, issue grants as token
claims, or register them with an external authority.

The CloudShell UI should later expose identity management for resources,
including editing identity bindings and managing grants. The first UI step is
read-only display of a resource's identity binding in the generated Resource
Manager detail view. Later management UI should operate against the same
resource identity model rather than creating a separate permission system.

Managed identity should be modeled as provider behavior over the same binding.
A managed identity provider can eventually register or provision the resource
identity, map grants to authority-specific assignments or app roles, and keep
the CloudShell resource model provider-neutral.

A built-in ASP.NET Core Identity-backed authority can be useful as a reference,
development, or team-owned provisioner for resource identities. It should not
become the CloudShell identity domain model. Resource declarations should bind
to provider-neutral identity metadata and grants, and the provisioner should
translate that model into its backing store. Moving to Microsoft Entra ID or
another provider should replace or reconcile the provisioner without changing
the resource model.

The first provisioning slice adds the provider-neutral
`IResourceIdentityProvisioner` contract and a Control Plane provisioning
planner. The planner resolves declared identity bindings to configured
resource identity providers, groups identities by provider, and includes grants
where the provisioned identity is the caller. Concrete provisioners for the
built-in authority, Microsoft Entra ID, or another system remain separate
provider implementations.

Supporting one or more identities on a resource programmatically is likely
worth adding before the provider-backed token lifecycle is complete. That
authoring surface should be able to declare identity metadata and then use the
declared identity in permission grants:

```csharp
var api = app.AddProject("api")
    .WithIdentity(identity =>
    {
        identity.Name = "api-service";
        identity.Provider = "development";
        identity.Claims.Add("resource", "api");
    });

database.Allow(api.Identity, DatabaseActions.ReadWrite);
secretStore.Allow(api.Identity, SecretActions.Read);
```

Declaring identities programmatically helps while building the model, whether
the identity is backed by a live provider or by a mock/development provider.
The same declarations later help deployment automation because CloudShell can
register identities and permission grants with the selected authority instead
of discovering that intent from provider-specific configuration.

Open authoring questions:

- Whether `api.Identity` is a single default identity or a collection with a
  default identity selected by name.
- Whether multiple identities per resource are worth supporting in the first
  authoring model, or whether a single default identity should remain the
  initial constraint until a concrete provider requires more.
- Whether `.WithIdentity(...)` creates an identity immediately or declares
  intent that a provider resolves later.
- Whether permission grants such as `Allow(...)` live on target resources,
  resource groups, provider-specific builders, or a shared permission builder.
- How provider-specific actions such as `DatabaseActions.ReadWrite` map to the
  CloudShell operation-permission catalog and token claims.

Identity should be innate resource metadata, not only a `ResourceCapability`.
A resource can have identity intent even before a provider supports token
issuance, and consumers should be able to inspect that intent through the
uniform resource model. Capabilities can still advertise provider behavior such
as managed identity provisioning, token issuance, protected API support, or
permission-assignment support.

## Remaining tasks

- Define default resource identity-provider selection inheritance from resource
  groups or parent resources.
- Add the reference development identity server hosting path and OIDC/OAuth
  configuration.
- Add a mock/development identity-provider mode that works when CloudShell
  authentication is disabled for local development, still projects the declared
  identity binding, and can simulate user or workload principals for
  permission-boundary tests.
- Add Microsoft Entra ID (Azure AD) provider configuration and compatibility
  tests for tokens, claim mapping, groups or app roles, and service-principal
  automation.
- Decide how resource identity should inherit from a resource group or parent
  resource.
- Add resource-level permission names and policy evaluation rules.
- Decide whether to expand the initial single-identity authoring API to
  multiple identities per resource.
- Extend declared permission grants beyond model-level resource action
  execution into mock identity tests, token claims, provider-backed identity
  proof, and provider or authority registration.
- Add Resource Manager UI workflows for managing resource identity bindings and
  permission grants.
- Add concrete managed identity provider behavior for registering or
  provisioning resource identities and grants with the backing authority.
- Wire the identity contract into at least one provider-backed workload type.
