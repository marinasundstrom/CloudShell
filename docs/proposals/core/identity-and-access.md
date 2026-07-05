# Identity and Access Proposal

## Status

Current implementation working document.

This proposal consolidates the earlier identity and permissions proposal with
the resource identity and access-control working document. Use this file as the
single proposal tracker for CloudShell identity, resource access, authorization
diagnostics, provider mapping, and remaining implementation work.

The current supported behavior and public contract are documented in
[Resource identity and permissions](../../resource-identity-and-permissions.md).
User sign-in, Control Plane authentication, remote Control Plane credentials,
and ASP.NET Core authentication configuration are documented in
[Authentication and authorization](../../authentication-and-authorization.md).

## Purpose

CloudShell needs a provider-neutral identity and access model that works across
local development, self-hosted installations, team-owned platform tooling, and
external identity systems such as Microsoft Entra ID.

The model should let resources declare identity intent, let callers evaluate
resource-scoped permissions, and let concrete identity providers materialize
that intent into tokens, claims, app roles, groups, service principals,
managed identities, or provider-specific access assignments.

CloudShell owns the resource-domain model and authorization decisions at the
Control Plane boundary. Identity providers own credential storage, token
issuance, token validation details, signing keys, client secrets,
certificates, federated credentials, and provider-native role or claim models.

## Problem

Resources can expose operations that affect system state or reveal protected
data:

- lifecycle actions such as start, stop, pause, and restart
- custom provider actions
- configuration and secret reads
- deployment, revision, and scale operations
- diagnostics, logs, and metrics
- provider and orchestrator operations
- identity provisioning and permission management

CloudShell must answer the same questions consistently for users, workloads,
services, providers, orchestrators, and automation:

- who or what is acting
- which resource is targeted
- which operation is requested
- which permission grants or claims are relevant
- whether the operation is allowed
- how the allow or deny decision can be diagnosed and audited

Without a shared model, authorization becomes provider-specific, secret access
is hard to secure, resource actions cannot be evaluated consistently, and
audit trails cannot reliably explain who or what changed a resource.

## Goals

- Model resource identity as provider-neutral resource metadata.
- Distinguish user identities from application identities, including workload,
  service, provider, orchestrator, and automation identities.
- Introduce resource identity-provider definitions and default provider
  selection.
- Support resource identity bindings for application, workload, service, and
  provider-owned scenarios.
- Model resource access as grants from a principal to a target resource
  operation.
- Authorize resource actions, configuration reads, secret reads, deployment
  operations, provider operations, and identity-management operations through
  the same resource-operation permission model.
- Support workload-to-workload and resource-to-service authentication.
- Keep resource access declarations independent from OAuth, OIDC, app-role,
  group, and RBAC implementation details.
- Preserve compatibility with OpenID Connect and OAuth 2.0 identity providers,
  including Microsoft Entra ID.
- Prove the resource identity model against at least one third-party
  standards-compliant OIDC/OAuth provider, such as Keycloak, Auth0, or Okta,
  so the model is not accidentally coupled to the built-in development
  authority.
- Provide diagnostics and audit inputs for allow and deny decisions.
- Keep local development possible before a production authority is configured.

## Non-Goals

- Do not implement a complete IAM system in the first version.
- Do not make CloudShell a general-purpose identity provider, authorization
  server, IdentityServer replacement, or Microsoft Entra ID replacement.
- Do not define organization, tenant, billing, or enterprise policy models.
- Do not replace ASP.NET Core authentication for user sign-in or API boundary
  authentication.
- Do not define application secret storage, vault resources, or secret
  references in this proposal.
- Do not put tokens, client secrets, certificates, passwords, or other
  credentials in resource identity metadata.
- Do not require every provider to implement all identity modes immediately.
- Do not make authentication protocols, token acquisition, token validation,
  or provider-owned claim mapping part of the resource-domain model.

## Current Implemented Foundation

The first resource identity and permission slices are implemented and described
normatively in
[Resource identity and permissions](../../resource-identity-and-permissions.md).
Authentication, authorization, roles, operation permissions, protected-service
bearer validation, and usage/observability permissions are described in
[Authentication and authorization](../../authentication-and-authorization.md).

The proposal remains the tracker for why the model exists and what remains:
durable external authority registration for protected API audiences,
provider-backed client secret storage, and any identity/access work that
directly improves the local-development MVP.

## Domain Model

### Identity and Principal Kinds

Authentication answers who the caller is. Authorization answers what that
caller may do.

CloudShell identities may represent human or non-human principals. Human
principals represent users. Non-human principals represent applications,
workloads, services, providers, orchestrators, deployment services, and
automation.

CloudShell should preserve this distinction at the domain level even when a
provider stores or projects principals differently. Resource-to-resource access
is usually performed by application identities, while interactive operations
are usually performed by user identities. Authorization should be able to
distinguish those cases.

Every identity participating in grants, authorization diagnostics, and audit
events needs a stable identifier. Provider credentials are only evidence used
to prove that identity; they are not the CloudShell identity model.

### Resource Identity Providers

Resource identity providers name identity systems that can resolve or
materialize resource identity metadata.

Provider definitions may be supplied by host configuration or programmatic
resource declarations. Identity providers should eventually be representable as
first-class protected resources with their own lifecycle, identities, and
permissions.

A provider definition can name a separate provisioning resource. That resource
represents the CloudShell-managed hook or third-party service that registers a
resource identity with the selected provider. It does not need to be the
identity provider resource itself.

CloudShell declarations may request startup provisioning for a resource
identity. This is desired state in the resource graph, similar to assigning a
managed identity to a workload before it starts. The selected identity provider
or provisioning service owns the observed state and should report it through
the provisioning status contract. For a durable provider, status should come
from provider-owned state such as app registrations, service principals,
managed identities, app-role assignments, or a provisioning database rather
than from CloudShell resource metadata.

Provider kinds:

| Kind | Use |
| --- | --- |
| `BuiltIn` | CloudShell-owned or local built-in identity behavior. |
| `Managed` | Provider-managed identity systems. |
| `Oidc` | OIDC/OAuth providers such as Microsoft Entra ID, Keycloak, Auth0, or Okta. |
| `Custom` | Provider-specific or host-specific identity mechanisms. |

A default identity provider represents the ambient authority for a host,
environment, project, or resource graph. Simple applications should be able to
require identity without first declaring an identity-provider resource.

### Provider Selection

Concrete provider bindings resolve by provider ID. Required bindings resolve
to the configured or programmatically declared default provider. When exactly
one provider is available, it can act as the implicit default.

Multiple providers require an explicit default for implicit `Required`
bindings. Resource-group inheritance, parent-resource inheritance, and
identity-provider resource references remain future work.

### Resource Identity Bindings

A resource identity binding describes resource-specific identity intent:

- provider selection or provider resolution intent
- stable identity name
- provider subject, when known
- non-secret scopes or claim metadata, when projected by a provider
- principal kind or workload role, where the provider contract needs it

Scopes and claims can be projected as provider output or non-secret hints, but
they should not become the primary authoring model for resource access. In the
normal case, CloudShell access grants describe which identity may perform which
operations on which target resources. The selected provider decides whether
that intent becomes OAuth scopes, app roles, groups, RBAC assignments, token
claims, or another provider-native shape.

Identity binding metadata must stay non-secret.

### Principals

CloudShell access control should use the common IAM shape: a principal receives
a permission grant on a protected resource. Principals are actors, not
resources. A principal can be a user, group, service account, service
principal, managed identity, workload identity, resource identity,
provider-owned identity reference, or automation identity.

The current implementation projects resource identities as
`ResourceIdentity` principals because those are the only principals CloudShell
can resolve and provision today. This is a principal source, not the access
model itself. Future user, group, service-account, and provider-owned
principal sources should feed the same grant model instead of adding separate
resource-specific access paths.

### Resource Identity Flow

CloudShell-managed platform services such as configuration stores and secrets
vaults should normally be accessed through resource identities and access
grants. Platform-managed resource-to-resource access should not use
configuration-store or vault-specific auth secrets. Provider-owned credentials
such as OAuth client secrets, certificates, federated credentials, or managed
identity endpoints are authority evidence for the assigned resource identity,
not application secrets from a CloudShell secrets vault.

Built-in platform services and authored services should use the same identity
and access integration points. A Secrets Vault, Configuration Store, or
CloudShell-owned helper service may be projected by a built-in resource type,
but its runtime API should acquire and validate identity through the same
provider-neutral resource identity, credential acquisition, and access-grant
contracts that an authored Web API would use. Special built-in shortcuts are
allowed only as documented transitional implementation details.

The intended flow is:

1. A resource declares or receives an identity binding.
2. The selected identity provider materializes that identity for the current
   environment.
3. The runtime exposes a credential acquisition mechanism to the resource.
4. The resource requests authentication evidence for the target service.
5. The target service validates that evidence and maps the authenticated
   principal back to CloudShell resource identities, grants, and operation
   permissions.

The credential acquisition mechanism is not the identity itself. For example,
Azure's `DefaultAzureCredential` discovers the best available credential source
for the current environment; it does not define the Azure managed identity
assigned to a workload. CloudShell should make the same distinction. A resource
identity binding describes the assigned identity, a credential mechanism proves
that identity in the current environment, and access grants describe what that
identity may do.

CloudShell should provide a `DefaultAzureCredential`-style resource credential
chain for authored services and built-in services. The right time to start is
when more than one CloudShell service or sample needs to acquire resource
identity tokens, or when a second credential source is needed. That point has
arrived for the Settings and Secrets flow: the first public-preview
`DefaultCloudShellResourceCredential` source reads the injected
`CLOUDSHELL_IDENTITY_*` environment contract and uses client credentials
against the configured token endpoint. The workload resource provider is
responsible for injecting that credential acquisition environment when it
starts a process or container from a resource with an identity binding.
Environment variables are the common runtime projection because they work for
executables and containers. The chain now also has a local profile credential
source that reads the active profile from `~/.cloudshell/config.json` or
`CLOUDSHELL_CONFIG_DIR`, with `CLOUDSHELL_PROFILE` selecting a profile. The
TypeScript, Java, and Go service clients now mirror both parts of that contract:
inside CloudShell-started containers they use `CLOUDSHELL_IDENTITY_*`
client-credentials acquisition first, and outside injected workloads they can
fall back to environment bearer tokens or the shared profile. Python, CLI
login/profile commands, and future Control Plane bindings should use the same
credential source order. Future sources should be added to the same chain for
managed identity endpoints, federated workload identity, refreshable developer
credentials, OS secure-store integration, external provider plugins, or
platform-specific credential brokers. A stored developer identity is a
credential source, not the resource identity itself.

CloudShell client SDKs should accept `CloudShellResourceCredential` objects in
the same way Azure SDK clients accept credential objects. The Control Plane
domain client now supports this directly, and future service-specific clients
for Configuration Store, Secrets Vault, or other protected resource services
should layer over the same credential contract instead of inventing
service-specific authentication options. The first service-specific clients
live in `CloudShell.Configuration.Client` and `CloudShell.Secrets.Client`;
both are public preview and dogfood the shared resource credential chain from
`CloudShell.Client`.

Whether a materialized resource identity uses a client secret, certificate,
federated credential, managed identity endpoint, signed assertion, or no
explicit secret is delegated to the selected identity provider. Provider-owned
credential material is separate from application secrets stored in a
CloudShell secrets vault.

### Resource Access Grants

Resource access grants define which principal may perform which operation on
which target resource.

Conceptually:

```text
Principal
  -> operation permission
  -> target resource
```

Examples:

```text
User alice
  -> CloudShell.Resources/resources/lifecycle/action
  -> application:api
```

```text
application:api identity
  -> CloudShell.Secrets/vaults/readSecrets/action
  -> secrets:vault
```

```text
default orchestrator identity
  -> CloudShell.Resources/runtimeResources/create/action
  -> container host
```

Grants are domain relationships. Provider implementations may project them
into token claims, scopes, app roles, groups, RBAC assignments, or other
authority-specific records.

The target resource is the protected object. It does not need its own identity
binding just to be protected by grants. A resource identity binding is needed
only when that resource itself should act as a principal.

Future versions may add inheritance, wildcard matching, roles, effective
permission projection, and policy evaluation. Providers that reconcile access
outside CloudShell can already report requested-versus-effective grant status
so Resource Manager can distinguish saved grant intent from provider-applied
access.

### Operation Permissions

Resource operation permissions use Azure RBAC-style operation names. The
current operation catalog lives in the specification doc and should be updated
whenever a new action or protected resource operation is added.

Permission constants should follow the resource model:

- common cross-resource operations live in
  `CommonResourceOperationPermissions`
- resource-type-specific operations live in dedicated classes such as
  `NetworkResourceOperationPermissions`,
  `LoadBalancerResourceOperationPermissions`,
  `ConfigurationStoreResourceOperationPermissions`, and
  `SecretsVaultResourceOperationPermissions`

The older `resources.manage` permission remains a compatibility superset while
the model moves toward explicit operation permissions.

### Secret and Configuration Access

Secret access must be authorized through resource permissions. A resource
should not gain secret read access solely because it references a secret.

Configuration stores and secrets vaults are examples of target resources
protected by access grants. They are separate from provider-owned identity
credentials such as OAuth client secrets, signing keys, certificates, or
federated credentials.

The resource owns the identity and permission requirements. The managed
process, container, service, configuration provider, or vault provider owns the
safe runtime transfer and enforcement path.

### Provider and Orchestrator Permissions

Providers and orchestrators may need elevated permissions for platform
operations such as creating runtime-managed resources, reconciling backing
infrastructure, applying network mappings, creating revisions, or provisioning
identities.

Those permissions should be explicit, diagnosable, and auditable. Provider
implementation authority should not bypass the Control Plane authorization
model for operations that affect CloudShell resources or expose protected
resource data.

### Authorization Evaluation

Resource Manager coordinates authorization before dispatching protected
operations to providers or orchestrators.

Evaluation should be able to reason about:

- authenticated user principals
- acting resource identities
- principal kind
- target resource
- requested operation permission
- declared grants
- token or claim evidence mapped back to resource grants
- provider-specific policy decisions where applicable

When a resource action carries an explicit acting resource identity, Resource
Manager evaluates declared grants for that identity and does not fall back to
the current user's permissions for that path.

### Diagnostics and Audit

Authorization decisions should feed diagnostics and future audit events:

- actor identity and principal kind
- target resource
- requested operation
- evaluated permission
- allow or deny result
- deny reason when safe to disclose
- provider involved, where relevant

Action capability reasons should explain denied or unavailable actions without
leaking provider-specific internals or credential material.

### API and UI Projection

The Control Plane API should project established domain concepts instead of
inventing transport-only identity concepts.

Implemented projection:

- `ResourceResponse.identity`
- resource permission grant list and evaluation endpoints
- resource identity provisioning endpoint

UI direction:

- Generated overview displays basic identity binding metadata.
- Generated Identity tab lists declared grants, exposes provisioning where
  available, and shows provisioning status and diagnostics for the selected
  resource.
- Future Resource Manager workflows should add guided management for identity
  bindings, permission grants, diagnostics, provider-resource selection, and
  provider-resource management.

Normal users should only see identity, grant, diagnostic, and access-history
details they are authorized to view.

## Authentication and Provider Integration

CloudShell uses ASP.NET Core authentication at runtime. OIDC, OAuth, cookies,
bearer tokens, external schemes, and built-in token authority behavior map
incoming requests to normal .NET authentication primitives such as
`ClaimsPrincipal`, identities, and claims. Resource Manager then maps those
principals and claims to CloudShell identities, resource access grants,
resource operation permissions, and provider-specific policy decisions.

For local development, CloudShell can use a built-in or separate development
identity provider. That provider is development infrastructure, not the
CloudShell domain model. It should speak standard OIDC/OAuth where practical
so the same provider abstraction can later be tested against IdentityServer,
Microsoft Entra ID, Keycloak, Auth0, Okta, or another standards-compliant
authority.

Microsoft Entra ID compatibility is a required target. The provider contract
must be able to map CloudShell resource identities and grants to Entra concepts
such as app registrations, service principals, app roles or groups,
issuer/audience validation, token claims, and client-credentials or
service-principal automation flows.

## Milestones

1. Basic development flow and sample.
   Implemented for the Settings and Secrets sample: declare a built-in
   identity provider, bind a Web API resource identity, grant that identity
   access to configuration and secret resources, provision the Web API identity
   at Control Plane startup, call provider-backed configuration and secrets
   services with a bearer token, and verify read, lifecycle action, and
   identity-management permission boundaries at the HTTP API.
2. Provisioning-resource authorization.
   Implemented for provisioning hooks and status: a provider can name a
   provisioning resource, callers must have provisioning permission on that
   resource as well as manage permission on the target resource to provision,
   and callers must have read permission on the target and provisioning
   resource to query status.
3. Managed identity and authority reconciliation.
   The first Keycloak sample validates external user authentication, CloudShell
   role claim mapping, and a sample-scoped resource identity provisioner that
   creates Keycloak clients, grant roles, service-account role assignments, and
   resource-permission token mappers. It now wires provisioned credentials into
   a workload that calls a protected Configuration Store with the external
   authority. Next, move beyond the sample implementation with durable
   provider-backed secret storage and production-style protected API audience
   registration.
4. Microsoft Entra ID compatibility.
   Map the same principal, resource identity, and grant model to Entra app
   registrations, service principals, managed identities, app roles or groups,
   token validation, directory queries, and automation flows.
5. UI management.
   Expand the generated Identity tab into guided identity binding, grant
   editing, richer diagnostics, provisioning, and provider-resource
   management. The tab now shows provisioning status and status diagnostics
   for identity-bound resources, and identity provisioning resources expose a
   setup action for provider bootstrap and reconciliation.
   The broader environment setup experience should also let an operator choose
   and configure the default identity provider for the CloudShell environment,
   run provider setup/reconcile hooks, and show whether the selected provider
   is ready for user sign-in, resource identity provisioning, and protected
   service-bearer validation. Per-resource Identity tabs can then focus on the
   binding, grant, and provisioning state for that specific resource.
6. Audit integration.
   Persist authorization decision events and connect identity/access decisions
   to the platform traceability stream.

## Remaining Tasks

- Define default provider inheritance from resource groups and parent
  resources.
- Decide whether local development should automatically use the built-in
  provider when no explicit identity provider is declared.
- Add durable provider-backed provisioning and status reconciliation for
  identities and grants.
- Add conventional user, group, service-account, service-principal, managed
  identity, workload-identity, and provider-owned principal grant assignment
  commands and Resource Manager surfaces. Resource identities remain the
  current assignable principal type while the broader IAM assignment contract is
  finalized.
- Add Microsoft Entra ID provider mapping notes and compatibility tests.
- Define identity-provider resources, provisioning-service resources,
  lifecycle, configuration projection, and protected management operations.
- Decide when identity-management operations should require authorization on
  the selected identity-provider resource, the provisioning resource, or both.
- Continue assigning documented operation permissions for configuration
  updates, deployment operations, logs, diagnostics, provider actions, and
  future runtime-managed resources.
- Continue authorization diagnostics beyond resource-action capability reasons,
  especially for configuration updates, deployment operations, logs,
  diagnostics, provider actions, and audit event payloads.
- Resource action capability diagnostics now include provider-owned
  application checks for missing configuration or secret reference targets and
  missing identity read grants before orchestration dispatch.
- Add effective permission APIs only after the grant model and provider mapping
  semantics are stable.
- Add audit event schemas for resource actions, provider operations, identity
  provisioning, secret access, and authorization decisions.
- Decide whether to expand beyond one identity per resource.
- Define provider-derived identity parameters such as subject, audience,
  scopes, claims, issuer, and authority references.
- Expand requested-versus-effective grant status with provider-specific
  reconciliation, drift detection, and user-safe diagnostics for providers
  that apply access asynchronously.

## Open Questions

- Should permissions or identity providers be modeled as resources in the first
  management UI slice, or should provider resources land first and permissions
  remain relationship records?
- Which identity binding parameters should be provider-derived by default, and
  which should require explicit author input?
- How should resource identity inherit from resource groups, parent resources,
  environment policy, and explicit provider resources?
- Should access grants live on target resources, groups, a shared access
  builder, or all of those with clear precedence?
- When should the model add multiple identities per resource?
- How should provider and orchestrator identities be granted bootstrap
  permissions without hiding privileged behavior?
- How should CloudShell expose effective permissions through the API without
  freezing a policy-engine design too early?
- Which deny reasons are safe for users, and which should be available only in
  privileged diagnostics or audit logs?
- How should token claim mapping preserve the pairing between resource and
  operation across different identity providers?
- How should provider-native grants be reconciled when the backing authority is
  changed outside CloudShell?
