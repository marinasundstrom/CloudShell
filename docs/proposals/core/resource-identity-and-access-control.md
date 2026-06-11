# Resource Identity and Access Control Proposal

## Status

Current implementation working document.

This proposal is the running design and implementation working document for
resource identity and access. It collects design reasoning, implementation
notes, open questions, provider-mapping ideas, and future work while the
feature evolves.

The broader platform direction is described by the
[Identity and Permissions Proposal](identity-and-permissions.md), but this
proposal focuses specifically on the resource-domain model for identity and
access.


This proposal covers how CloudShell represents identity, principal kinds,
identity-provider bindings, and access relationships between resources. It
intentionally does not define secret storage, vault resources, secret
references, authentication protocols, OAuth flows, OIDC configuration, token
issuance, client credentials, or provider-specific authority behavior.

## Clarifications

CloudShell uses OAuth and OpenID Connect style authentication for Control Plane
Web APIs and platform-provided services. That protocol surface belongs to the
platform authentication and provider implementation architecture, not to the
resource-domain model defined by this proposal.

Each identity provider or authority owns how it stores identities, grants,
credentials, claims, roles, scopes, and provider-native access assignments. The
built-in identity provider may store users, application principals, grants, and
claim metadata in the Control Plane-backed identity store while exposing
authentication and token endpoints that are compatible with the OAuth/OIDC
usage expected by CloudShell components. Other providers, such as IdentityServer,
Microsoft Entra ID, Keycloak, Auth0, or Okta, may store and materialize the same
concepts differently while still satisfying the provider contract.

Resource identity and access rules are modeled separately in the CloudShell
domain. Resource identity bindings and resource access grants describe resource
intent and resource-to-resource access relationships. Scopes and claims may be
projected from those declarations, or mapped back onto those declarations during
authorization, but they are not the domain model itself and should not make the
resource graph depend on OAuth or OIDC terminology.

At runtime, CloudShell uses ASP.NET Core authentication. The configured
authentication handlers, including OIDC handlers, map incoming tokens onto the
normal .NET authentication primitives such as `ClaimsPrincipal`, `IIdentity`,
and claims. Resource Manager can then authorize requests by mapping the
authenticated principal and its claims to CloudShell resource identities,
resource access grants, resource operation names, and provider-specific policy
decisions.

These clarifications are included here to keep the proposal boundaries clear.
The detailed authentication protocol, token validation, ASP.NET Core
authentication configuration, and provider endpoint behavior should be defined
in a separate authentication/provider architecture document.

The normative feature documentation lives in
[Resource identity and permissions](../resource-identity-and-permissions.md).
That document describes the supported behavior and public model. This proposal
should remain a collection point for design intent, implementation status,
provider-mapping ideas, unresolved questions, and future slices.

## Milestones

Proposals should be sliced into independently useful milestones so CloudShell
can improve this area iteratively alongside other proposals.

1. Basic development flow and sample: declare a built-in identity provider,
   bind a Web API resource identity, grant that identity access to another
   resource, provision the Web API identity, and verify the model-level access
   boundary. Current status: implemented for the Settings and Secrets sample,
   including built-in identity provisioning, scoped resource-operation access
   claims, resource access grants, and HTTP API verification that read access
   does not imply lifecycle action or identity-management access.
2. Provider-resource authorization: model identity providers as protected
   resources with their own identities and provision/manage permissions, then
   require access to both the target resource and the selected provider
   resource before provisioning identities.
3. Managed identity and authority reconciliation: make providers register or
   reconcile identities and grants with their backing authority instead of only
   recording CloudShell declarations.
4. Microsoft Entra ID compatibility: map the same identity and grant model to
   Entra app registrations, service principals, app roles or groups, token
   validation, and automation flows.
5. UI management: keep overview identity details read-only, isolate identity
   actions in an Identity tab for identity-enabled resources, and then expand
   that tab into guided identity binding, grant editing, diagnostics, and
   provider-resource management.

## Problem

CloudShell already has a user/session authentication model, but it does not
yet define identity and access as part of the resource domain model.
Resources can be authored, projected, managed, and inspected, but there is no
provider-neutral way to declare that a resource has an identity, what kind of
principal that identity represents, or which identity provider is expected to
resolve it.

That makes it difficult to model scenarios such as:

- an API resource that needs an application or workload identity
- a provider that needs a stable provider-owned identity
- a resource graph that needs to expose identity intent before any provider has
  projected that identity into provider-specific authentication concepts
- access rules that determine which principal may read a resource or perform
  resource actions

## Goals

- Model identity as provider-neutral resource metadata.
- Introduce a resource identity-provider contract for resources and resource
  groups.
- Let Resource Manager select or inherit a default identity provider.
- Distinguish user identities from application, service, provider,
  automation, and workload identities as CloudShell domain concepts.
- Treat principal kind as CloudShell identity-domain metadata that can be
  projected into provider-specific concepts, rather than as an OAuth or OIDC
  primitive.
- Support resource identity bindings for application, workload, service, and
  provider-owned scenarios.
- Model access as relationships between source principals, target resources,
  and operations.
- Leave authentication, token acquisition, token validation, and
  protocol-specific authorization behavior to concrete identity-provider
  implementations.

## Non-Goals

- Do not define application secret storage, vaults, or secret references here.
- Do not define authentication flows, token acquisition, token validation,
  authorization protocols, client credentials, provider-owned secrets, or
  claim-mapping rules here.
- Do not replace the existing user authentication model.
- Do not make CloudShell a general-purpose identity provider, authorization
  server, or identity authority platform.
- Do not require the built-in ASP.NET Core Identity-backed provider to support
  the full surface area of OAuth, OpenID Connect, IdentityServer, Microsoft
  Entra ID, or another production authority.
- Do not make the development identity server part of the CloudShell domain
  model or a required production dependency.
- Do not require authentication to be enabled just to declare, inspect, or test
  resource identity shape in local development.
- Do not require every provider to implement all identity modes immediately.

### Secret terminology

This proposal may use configuration stores or secrets vaults as examples of
resources that can be protected by access rules. Those examples refer to
application data resources in the CloudShell resource graph.

They are not the same thing as provider-owned credentials used by an identity
provider or authorization flow, such as OAuth client secrets, signing keys,
token credentials, certificates, or other authority-specific secret material.
Provider-owned credentials belong to concrete identity-provider
implementations and are outside this resource-domain proposal.

## Proposed Model

### Resource identity providers

CloudShell should introduce a small identity-provider abstraction for resources:

Identity providers should eventually be representable as first-class resources.

The initial implementation may keep provider definitions configuration-backed,
but the resource model should support identity-provider registration through the
same programmatic authoring surface used for other resources.

CloudShell should also support a default identity provider that is supplied by
the host, environment, or project configuration without being explicitly
authored as a resource. The provider still participates in the effective
resource graph as the ambient authority used to resolve identity requirements,
but simple applications should not have to declare an identity provider
resource before opting into identity.

A default identity provider represents the ambient identity authority for a
host, environment, or project. It may be projected into the effective graph by
CloudShell even when it was not explicitly declared by the application. An
identity provider resource represents an explicitly authored shared authority,
connection, or provisioning endpoint used by one or more resource identities in
the resource graph.

Examples include:

- Development Identity Provider
- IdentityServer Provider
- Microsoft Entra ID Provider
- Keycloak Provider

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

### Built-in identity provider and provisioning endpoints

The built-in identity provider should be treated as a minimal CloudShell-owned
authority implementation, not as the CloudShell identity domain model itself.
It exists to support local development, self-hosted installations, tests,
samples, and simple team environments where an external authority has not yet
been configured.

The built-in provider can be backed by ASP.NET Core Identity or a similar
store. It should store the concrete provider-side state needed to authenticate
CloudShell users and applications, including principal records, non-secret
claim metadata, declared grants, and provider-managed credential references.
Since CloudShell needs to represent both human users and application or
workload identities, the provider should distinguish user principals from
application principals rather than treating applications as ordinary users.

The built-in provider should expose a small token-based endpoint surface that
is close enough to OpenID Connect and OAuth-style usage for CloudShell
components and resource applications to authenticate against it. That surface
can include provisioning endpoints used by Resource Manager or provider
implementations to register application identities, reconcile grants, and
project declared resource access into token-shaped authorization evidence.

This built-in endpoint surface is intentionally shallow. It should not imply
that CloudShell is implementing a complete OAuth authorization server,
complete OpenID Connect provider, IdentityServer replacement, Microsoft Entra
ID replacement, or general-purpose identity platform. The built-in provider is
a development and self-hosting convenience that materializes the CloudShell
resource identity and access model into a usable local authority.

Compliance and interoperability should be tested separately against
IdentityServer or another real OIDC provider. Those tests should verify that
CloudShell's provider abstraction can map resource identity bindings and
access grants to standards-compliant issuer metadata, token validation,
audiences, claims, scopes, client credentials, app roles, groups, or other
provider-native concepts without making those protocol details part of the
resource-domain model.

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

Identity bindings should describe resource-specific identity intent.

Examples include:

- identity name
- subject
- required principal kind
- provider selection or provider resolution intent

Scopes and claims may appear in the projected model as abstract identity
evidence or provider output, but they should not be treated as the primary
authoring model for resource access. In the normal case, CloudShell access
grants should describe which resource identity may perform which operations on
which target resource, and the selected identity provider should materialize
that intent into provider-specific scopes, claims, app roles, groups, RBAC
assignments, or token shape.

Provider configuration should remain shared and live on the identity provider.
A resource should reference a provider rather than duplicate provider
connection details.

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
Concrete `Provider` bindings resolve by provider ID or provider resource
reference. `Required` bindings resolve to the configured default provider; when
exactly one provider is registered, that provider is the implicit default. A
host may also declare a default identity provider without adding an identity
provider resource to the graph. Multiple configured providers require an
explicit default for implicit `Required` bindings. Control Plane hosts can
register providers and the default through `ResourceIdentity` configuration.
Resources with identity bindings that cannot be resolved to a registered or
default provider produce resource model diagnostics. Resource-group or
parent-resource inheritance and provider-backed token behavior remain separate
implementation work.

The current implementation supports configuration-backed provider definitions
and programmatic provider definitions through `resources.AddIdentityProvider(...)`.
Programmatic declarations can select a default with
`resources.UseDefaultIdentityProvider(...)`, and Resource Manager diagnostics
and provisioning plans resolve against the combined configured and declared
provider catalog. First-class identity-provider resources and provider resource
references remain proposal work.

### Resource access rules

Resource access rules should define which source principal may perform which
operation on which target resource. Permission names are the operation catalog
used to evaluate those access rules:

-- `resource.identity.read`
-- `resource.identity.manage`
-- provider-specific permission names
-- Azure RBAC-style resource action operations such as
  `CloudShell.Resources/resources/lifecycle/action`

The access evaluation model should be able to reason about CloudShell principal kind, resource identity, target resource, and operation instead of relying only on user session claims.

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
| `configuration.store` and `ResourceClass.Configuration` | configuration entry read | `ConfigurationStoreResourceOperationPermissions.ReadEntries` |
| `cloudshell.network` and `cloudshell.virtualNetwork` | `reconcileEndpointMappings` | `NetworkResourceOperationPermissions.ReconcileEndpointMappings` |
| `cloudshell.loadBalancer` | `applyLoadBalancerConfiguration` | `LoadBalancerResourceOperationPermissions.ApplyConfiguration` |
| `secrets.vault` and `ResourceClass.SecretsVault` | application secret value read | `SecretsVaultResourceOperationPermissions.ReadSecrets` |

The existing `resources.manage` permission remains a compatibility superset
while the model moves toward resource operation permissions.

## Initial authoring surface

The simplest authoring path should allow a host or application to declare a
default identity provider and let resources use it implicitly:

```csharp
resources.AddIdentityProvider(
    "identity:dev",
    "Development Identity",
    ResourceIdentityProviderKind.BuiltIn,
    useAsDefault: true);

var api = resources
    .AddContainerApplication("api", "ghcr.io/example/api:latest")
    .RequireIdentity();
```

For resource graphs that need an explicit provider resource, the provider can be
registered and referenced directly:

```csharp
var identityProvider = resources
    .AddIdentityProvider("identity:dev")
    .WithOidc();

var api = resources
    .AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithIdentity(identityProvider);

var worker = resources
    .AddContainerApplication("worker", "ghcr.io/example/worker:latest")
    .RequireIdentity();
```

The default authoring path should avoid requiring callers to manually duplicate
provider-specific parameters on each resource. When a resource uses the default
identity provider or references an identity provider resource, CloudShell should
be able to resolve reasonable identity binding defaults from the selected
provider, the resource graph, and the resource declaration.

Examples of provider- or graph-derived values include:

- identity name
- subject
- audience
- scopes
- claims
- issuer or authority reference
- provider-specific registration metadata

Manual identity configuration should remain available for cases where the
author knows the stable identity intent that should override provider or graph
defaults:

```csharp
var api = resources
    .AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithIdentity(identityProvider, identity =>
    {
        identity.Name = "api-service";
        identity.Subject = "application:api";
    });
```

Scopes and claims should usually be derived from resource access grants rather
than authored directly on the resource identity binding. For example, an
access grant from the API identity to a database read operation may be
projected by one provider as an OAuth scope, by another as an app role or
group assignment, and by another as an RBAC role assignment. The CloudShell
model should preserve the abstract access relationship and let the provider
decide how that relationship is materialized.

The important distinction is that manual configuration should be an override,
not the normal shape of the model. Most resources should be able to say that
they use or require identity, declare access through `Allow(...)` grants, and
let the selected provider resolve the concrete identity parameters and emitted
authorization evidence.

Programmatic identity declarations are not limited to local or unauthenticated
development. A declaration can bind to a production identity provider, point at
a mock provider, or state only that the resource requires an identity whose
provider-specific details are resolved later. The mock-provider path is a
convenience for local development before wiring the same app to Microsoft Entra
ID or another production provider. Provider-specific credentials used for that
wiring are provider implementation details, not resource-graph secrets.

The first implemented surface supports one optional identity binding per
resource. That identity is stored on the programmatic declaration, projected
through `Resource.IdentityBinding`, and exposed through the Control Plane API
and remote client. This gives CloudShell a concrete model-building test case
without requiring a working token issuer or provider-backed identity lifecycle.

Mock identity support should later cover declared user or workload principals,
not only resource metadata. That would let local tests exercise permission
boundaries between resources before the same declarations are backed by
Microsoft Entra ID or another production authority.

The first grant authoring surface stores access grants between declared
resource identities and target resources.

Examples:

```csharp
secretStore.Allow(api.Identity, SecretActions.Read);
database.Allow(api.Identity, DatabaseActions.ReadWrite);
queue.Allow(worker.Identity, QueueActions.Consume);
```

A grant connects:

- a source principal or resource identity
- a target resource
- one or more allowed operations

The declaration model can evaluate those grants with
`ResourceAccessGrantEvaluator`.

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
var identityProvider = app.AddIdentityProvider("identity:development")
    .WithOidc();

var api = app.AddProject("api")
    .WithIdentity(identityProvider);

database.Allow(api.Identity, DatabaseActions.ReadWrite);
secretStore.Allow(api.Identity, SecretActions.Read);
```

Provider-specific identity details can still be supplied explicitly when needed,
but the resource model should first try to resolve identity parameters from the
default identity provider or explicit identity provider resource, resource name,
resource kind, declared grants, and other graph metadata.

Declaring identities programmatically helps while building the model, whether
the identity is backed by a live provider or by a mock/development provider.
The same declarations later help deployment automation because CloudShell can
register identities and permission grants with the selected authority instead
of discovering that intent from provider-specific configuration.


- Whether `api.Identity` is a single default identity or a collection with a
  default identity selected by name.
- Whether multiple identities per resource are worth supporting in the first
  authoring model, or whether a single default identity should remain the
  initial constraint until a concrete provider requires more.
- Whether `.WithIdentity(...)` creates an identity immediately or declares
  intent that a provider resolves later.
- Whether access grants such as `Allow(...)` live on target resources,
  resource groups, provider-specific builders, or a shared access builder.
- Whether identity providers should remain configuration-backed, become
  first-class resources, or support both explicit resources and projected
  default providers in the effective graph.
- How default identity providers should be declared and overridden by resource
  groups, parent resources, or explicit provider resources.
- Whether local development should default to the built-in identity provider
  when a resource requires identity but no explicit identity provider has been
  declared, and how that fallback should be disabled or overridden.
- Whether grants should support a distinction between requested access and
  effective access.
- Whether resources should reference provider objects directly instead of
  provider IDs in the programmatic authoring model.
- Which identity binding parameters should be provider-derived by default, which
  should require explicit author input, and which projected scopes or claims
  should be derived from declared access grants.
- How provider-specific actions such as `DatabaseActions.ReadWrite` map to the
  CloudShell operation-permission catalog and token claims.

Identity should be innate resource metadata, not only a `ResourceCapability`.
A resource can have identity intent even before a provider supports token
issuance, and consumers should be able to inspect that intent through the
uniform resource model. Capabilities can still advertise provider behavior such
as managed identity provisioning, token issuance, protected API support, or
permission-assignment support.


- Define default resource identity-provider selection inheritance from resource
  groups or parent resources.
- Define how a default identity provider is supplied by host, environment, or
  project configuration and projected into the effective graph without being
  explicitly authored as an identity-provider resource.
- Decide whether the local development environment should automatically use
  the built-in identity provider as the default when no explicit identity
  provider is declared.
- Add the built-in identity provider hosting path, provisioning endpoints, and
  shallow token-based authority surface for local development and self-hosted
  scenarios.
- Add IdentityServer or real OIDC compliance tests that verify the provider
  abstraction can map CloudShell identity bindings and access grants to
  standards-compliant authority behavior.
- Add a mock/development identity-provider mode that works when CloudShell
  authentication is disabled for local development, still projects the declared
  identity binding, and can simulate user or workload principals for
  permission-boundary tests.
- Add Microsoft Entra ID (Azure AD) provider mapping notes and compatibility
  tests for provider-native concepts such as token shape, claim mapping,
  groups or app roles, and service-principal automation, without making those
  protocol details part of the CloudShell resource identity domain model.
- Decide how resource identity should inherit from a resource group or parent
  resource.
- Add resource-level operation names and resource access evaluation rules.
- Decide whether to expand the initial single-identity authoring API to
  multiple identities per resource.
- Extend declared access grants beyond model-level resource action execution
  into mock identity tests, provider-backed identity projection, and provider
  registration. Current built-in-provider tests can use token-shaped evidence,
  but the protocol details should remain provider implementation behavior.
- Add Resource Manager UI workflows beyond the current read-only overview
  identity summary and generated Identity tab, including guided management for
  resource identity bindings and permission grants.
- Add concrete managed identity provider behavior for registering or
  provisioning resource identities and grants with the backing authority.
- Define identity-provider resource registration and lifecycle.
- Require permission to provision or manage identities on the selected
  identity-provider resource, in addition to permission on the target resource.
- Define how provider resources expose shared authority configuration.
- Define default identity parameter resolution from provider resources and the
  resource graph.
- Define provider-backed reconciliation of identities and grants.
- Define grant provisioning against external authorities.
- Wire the identity contract into at least one provider-backed workload type.
