# Authentication and authorization

CloudShell uses ASP.NET Core authentication schemes for identity and a common
claims contract for authorization. Authentication and persistence are selected
independently in `CloudShell.Host/appsettings.json`.

Authentication is a hosting concern, not a separate product model. A combined
host can use one ASP.NET Core authentication setup for the shell and Control
Plane. Split hosts should use a shared authority or compatible authentication
abstractions so the UI and Control Plane agree on the caller identity while
still running in separate processes.

## Implementation status

Implemented today:

- ASP.NET Core authentication modes: Identity, dashboard secret, OpenID
  Connect, and external host-provided schemes.
- Claims-based CloudShell authorization for shell, resource-group, and resource
  access.
- Control Plane API protection when `Authentication:Enabled` is `true`.
- A remote `IControlPlane` adapter configured with a Control Plane base URL.
- An opt-in built-in Control Plane token authority for split-hosting service
  credentials.
- Remote Control Plane credentials for no-auth, static bearer tokens, and
  client-credentials tokens.
- Configuration Store and Secrets Vault runtime APIs protected by resource
  identity bearer tokens and resource-permission claims. These service APIs
  accept built-in authority tokens and can be configured to validate external
  OIDC/OAuth JWT bearer tokens through service-bearer validation settings.
- A public-preview `DefaultCloudShellResourceCredential` chain for authored and
  built-in services that need to acquire authentication evidence for their own
  resource identity. See [SDK clients](sdk-clients.md) for the lightweight
  client package and service-client boundaries.

Direction:

- Resource identity and permissions should work with Microsoft Entra ID
  (Azure AD) through standard OIDC/OAuth validation while also supporting a
  separate development identity server instance for local testing.
- Resource identity provisioning uses the provider-neutral
  `IResourceIdentityProvisioner` contract. A built-in ASP.NET Core
  Identity-backed authority can implement that contract as a reference or
  development provisioner, but resources should continue to bind to CloudShell
  identity metadata and grants so the same model can move to Microsoft Entra ID
  or another provider.
- Protected API resource metadata for services that expose their own direct
  APIs.
- Extend `DefaultCloudShellResourceCredential` with provider-backed sources
  such as managed identity endpoints, federated workload identity, local
  developer credentials, and external credential plugins.
- Optional provider-specific provisioning against systems such as Microsoft
  Entra ID, API gateways, service meshes, mTLS, signed requests, or local
  credential stores.
- Runtime API enforcement by the service or container that owns each API.
- Shared on-premise environments should be able to distinguish ordinary
  resource management from operations that affect the host machine or shared
  platform state. Host-affecting actions such as standalone local filesystem
  volume creation, host path mounts, hosts-file DNS publishing, network setup,
  public endpoint binding, and OS feature enablement should be gated by
  administrator or platform-operator permissions, even when lower-risk resource
  creation remains available to application owners.

See [Resource identity and permissions](resource-identity-and-permissions.md)
for resource identity providers, projected identity bindings, resource
operation permissions, and the current resource operation catalog.

## Authentication modes

Set `Authentication:Mode` to one of the following values.

### Local Identity

`Identity` uses the built-in CloudShell identity authority backed by ASP.NET
Core Identity with the configured persistence provider. It is the default mode.
CloudShell treats email as the local sign-in identifier for this mode, matching
the common shape of ASP.NET Core Identity deployments and third-party account
providers.

```json
{
  "Authentication": {
    "Enabled": true,
    "Mode": "Identity",
    "AllowLocalSetup": true
  }
}
```

When `AllowLocalSetup` is enabled and no users exist, `/account/setup` creates
the first administrator. Keep this option disabled in production after
provisioning accounts.

When CloudShell runs in `Identity` mode, administrators with
`shell.configure` can use the shell Users page to create additional local test
users. This is a rudimentary built-in identity-provider surface for local
development and permission-boundary testing. It can assign one built-in role
and basic CloudShell scope claims such as resource-group scope, direct resource
scope, and a single resource-permission claim. It is not the final provider
management model; future identity-provider integrations should use a dedicated
provider/setup surface and provider-neutral hooks.

The built-in identity provider is an authority in its own right. It owns the
CloudShell-facing endpoints, token issuance, local principal projection, and
grant application surface for the built-in provider. It is not limited to local
passwords: because it is based on ASP.NET Core Identity, it can also rely on
upstream third-party sign-in services such as Microsoft Account, Google, or
another ASP.NET Core authentication handler. In that shape, the upstream
provider authenticates the user, ASP.NET Core Identity stores or links the
local user record, and the built-in CloudShell authority projects the resulting
user as a principal that can receive resource grants and appear in activity
logs. Email remains the user-facing login identifier; CloudShell principal IDs
remain provider-owned stable references and should not be treated as login
names.

Username sign-in is an explicit built-in Identity policy, not the default. Set
`Authentication:BuiltInIdentity:AllowUserNameSignIn` to `true` only when the
host intentionally allows local ASP.NET Core Identity usernames as sign-in
identifiers in addition to email addresses. This affects browser sign-in,
built-in authority password-token requests, and in-memory identity users. When
disabled, in-memory user names remain CloudShell principal keys for grants and
directory lookups, while the ASP.NET Core Identity user name and sign-in
identifier stay email-based.

### Dashboard secret

`Secret` presents a dashboard-secret login and creates an administrator
session. Store the secret outside committed configuration.

```json
{
  "Authentication": {
    "Enabled": true,
    "Mode": "Secret"
  }
}
```

Configure the secret with user secrets, a secret store, or an environment
variable:

```bash
Authentication__Secret="replace-with-a-long-random-secret"
```

### OpenID Connect

`OpenIdConnect` works with Microsoft Entra ID (Azure AD) and other OIDC
providers. CloudShell uses an authorization-code flow with PKCE and a local
cookie session.

```json
{
  "Authentication": {
    "Enabled": true,
    "Mode": "OpenIdConnect",
    "RoleClaimType": "roles",
    "OpenIdConnect": {
      "Authority": "https://login.microsoftonline.com/TENANT_ID/v2.0",
      "ClientId": "CLIENT_ID",
      "CallbackPath": "/signin-oidc",
      "RequireHttpsMetadata": true,
      "Scopes": [ "openid", "profile", "email" ]
    }
  }
}
```

Set `Authentication__OpenIdConnect__ClientSecret` through a secure
configuration provider. `MetadataAddress` can be used instead of `Authority`.

Directionally, split-hosting deployments should treat the CloudShell Control
Plane as a protected API resource. Configure the UI host with a Control Plane
credential that can authenticate calls to that API resource. The UI host keeps
its own local sign-in session, then the remote `IControlPlane` adapter asks the
credential for an authentication result when it calls the Control Plane
service.

For OAuth-based deployments, that usually means attaching a bearer token:

```text
Authorization: Bearer <control-plane-access-token>
```

The Control Plane host validates the credential against the same authority or
trusted authentication abstraction and uses the resulting `ClaimsPrincipal`
with the normal CloudShell authorization policies. The browser should not need
to understand Control Plane credentials when the UI is server-side or
BFF-hosted; the UI server acquires and attaches the authentication metadata on
behalf of the signed-in user.

The credential boundary should follow the Azure SDK pattern: application code
configures a credential object, and transport code asks that credential for a
provider-specific authentication result. Shell views and resource extensions
should not manually build `Authorization` headers or other authentication
metadata.

In Microsoft Entra ID terms, the Control Plane is represented by its own app
registration/API resource, for example `api://cloudshell-control-plane`.
Delegated scopes such as `ControlPlane.Access` or app-only permissions are
defined on that resource. The UI application requests tokens for those scopes;
the Control Plane validates that the token audience is the Control Plane API.
That is the Azure/OAuth implementation of the protected API resource model, not
the only supported shape.

### External ASP.NET Core scheme

`External` tells CloudShell to use schemes registered by custom host code or a
trusted extension:

```json
{
  "Authentication": {
    "Enabled": true,
    "Mode": "External",
    "DefaultScheme": "CustomCookie",
    "ChallengeScheme": "CustomProvider"
  }
}
```

The named authentication handlers must be registered with
`IServiceCollection.AddAuthentication()` before the application is built.
This mode supports providers that aren't OIDC without coupling CloudShell to a
provider-specific package.

## Hosting-specific authentication

Common and split hosting use the same claims and authorization model, but they
attach identity to requests differently.

| Hosting shape | Authentication configuration | Control Plane calls |
| --- | --- | --- |
| Combined UI and Control Plane | One ASP.NET Core auth setup, usually Identity, Secret, OIDC, or External. | In-process `IControlPlane` calls share the current `ClaimsPrincipal`. |
| Split UI and Control Plane | UI signs users in with the configured provider. Control Plane validates credentials from the same authority or trusted authentication abstraction. | Direction: remote `IControlPlane` calls use a configured credential that attaches the required authentication metadata, commonly `Authorization: Bearer <token>`. |

Keep CloudShell authorization independent from the hosting topology. Roles,
direct claims, resource-group scopes, and resource scopes should be issued by
or mapped from the shared authority, then evaluated by CloudShell in the
Control Plane.

## Control Plane credentials

The remote Control Plane client should model authentication with a credential
abstraction rather than taking raw tokens, API keys, or provider-specific
secrets from UI code. A minimal shape is:

```csharp
public abstract class ControlPlaneCredential
{
    public abstract ValueTask<ControlPlaneAuthenticationResult> AuthenticateAsync(
        ControlPlaneAuthenticationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ControlPlaneAuthenticationContext(
    Uri Resource,
    IReadOnlyList<string> Scopes);

public sealed record ControlPlaneAuthenticationResult(
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset? ExpiresOn = null);
```

Recommended credential types:

- `CurrentUserControlPlaneCredential`: acquires delegated authentication
  material for the signed-in UI user.
- `ClientSecretControlPlaneCredential` or `ClientCertificateControlPlaneCredential`:
  acquires application authentication material for trusted service-to-service
  calls.
- `StaticControlPlaneCredential`: uses configured headers, a bearer token, or
  an API key for local development and tests.
- `ChainedControlPlaneCredential`: tries several credentials in order for
  environment-specific hosting.

The remote adapter owns credential caching and header attachment. Callers
continue to depend on `IControlPlane`; they do not depend on OIDC, OAuth, API
keys, cookies, or credential storage details.

For Azure-compatible providers, the request context should identify the Control
Plane API resource and the required scope on that resource, such as
`api://cloudshell-control-plane/ControlPlane.Access`. Client-credential flows
can request the app permission through the provider's equivalent of the
resource-level `.default` scope. Other providers can map the same abstraction
to their own audience, scope, API key, signed request, or service identity
model.

The built-in remote adapter supports no credentials, static bearer tokens, and
client-credentials tokens issued by the Control Plane token authority. Delegated
current-user credentials are still directional.

Enable the built-in token authority on the Control Plane host:

```json
{
  "Authentication": {
    "BuiltInAuthority": {
      "Enabled": true,
      "Issuer": "https://control-plane.example.com",
      "Audience": "cloudshell-control-plane",
      "Clients": {
        "cloudshell-ui": {
          "Secret": "replace-with-secret",
          "Scopes": [ "ControlPlane.Access" ],
          "Roles": [ "CloudShell.Administrator" ]
        }
      }
    }
  }
}
```

Configure the split UI host to request client-credentials tokens:

```json
{
  "CloudShell": {
    "ControlPlane": {
      "BaseAddress": "https://control-plane.example.com",
      "Credential": {
        "Mode": "ClientCredentials",
        "ClientId": "cloudshell-ui",
        "ClientSecret": "replace-with-secret",
        "Scopes": [ "ControlPlane.Access" ]
      }
    }
  }
}
```

If `SigningKeyPem` is not configured, the built-in authority generates an
ephemeral RSA signing key at startup. Configure a persistent signing key for
shared or long-running hosts.

## Protected API resources direction

Access control for independently hosted APIs requires each protected API to
describe how callers authenticate to it. In this context, "resource" usually
means a running service with its own API surface, such as a configuration
service. OAuth providers do that with identity-provider API-resource
registration. Other providers may use service accounts, API keys, signed
requests, mutual TLS, or another authentication contract. The CloudShell
Control Plane is one protected API resource. Other CloudShell-managed services
that expose their own protected HTTP APIs can also declare protected API
metadata.

Do not assume every CloudShell resource registration is also a separately
protected API resource. A CloudShell resource is part of CloudShell inventory
and authorization. A protected API resource is an authentication contract used
for direct calls to a hosted API. A resource needs provider-specific
registration only when callers should authenticate directly to that resource's
API.

For each protected hosted API, provision or configure:

- An API resource identifier or service identity, such as
  `api://cloudshell-control-plane`, `api://cloudshell-resource-{resource-id}`,
  or a provider-specific service name.
- The allowed caller credentials for user calls and service-to-service calls.
- Provider-specific permissions, scopes, roles, keys, certificates, or trust
  relationships.
- The client applications or services allowed to use those credentials.
- The validation settings the hosted API uses, such as issuer,
  audience/resource, accepted scopes or roles, key validation, certificate
  validation, or signed-request validation.

CloudShell should treat protected API registration as a resource capability, not
as a universal requirement. Providers for resources with protected APIs can
surface the required authentication metadata and later automate provisioning
against a provider such as Microsoft Entra ID, an API gateway, a service mesh,
or a local development credential store. Resources that are accessed only
through the Control Plane can rely on Control Plane authorization instead of
being registered as separate protected API resources.

The service that owns the API must enforce its own runtime authentication. If a
resource is implemented as a container, the containerized service is responsible
for validating incoming credentials and applying its API authorization rules.
CloudShell can manage resource registration, lifecycle, endpoint discovery,
dependency wiring, and authentication metadata, but it does not automatically
protect arbitrary HTTP endpoints running inside a resource container.

This section describes the direction for resources that expose their own direct
APIs. Today, CloudShell applies its built-in authorization at the Control Plane
boundary. Configuration Store and Secrets Vault use the shared
`cloudshell.resource-permission` claim evaluator after bearer-token
authentication. Hosts can keep using the built-in authority or configure the
service APIs to trust an external OIDC/OAuth issuer:

```json
{
  "Authentication": {
    "ServiceBearer": {
      "Enabled": true,
      "Authority": "http://localhost:8080/realms/cloudshell",
      "Issuer": "http://localhost:8080/realms/cloudshell",
      "Audience": "cloudshell-services",
      "RequireHttpsMetadata": false
    }
  }
}
```

`Audience` is optional for local or provider-default token shapes, but shared
and production hosts should configure it so the protected service validates
that the token was issued for the intended API resource. `SigningKeyPem` can
be used for local development and tests when OIDC discovery metadata is not
available.

## Roles and permissions

Roles map to permissions through configuration. The default roles are:

- `CloudShell.Administrator`: all permissions and all scopes.
- `CloudShell.Contributor`: shell read plus resource read, runtime-managed
  resource inspection, observability read, create, and manage permissions.
- `CloudShell.Reader`: shell, resource read, and observability read
  permissions.

Available permissions are:

- `shell.read`
- `shell.configure`
- `resource-groups.read`
- `resource-groups.create`
- `resource-groups.manage`
- `resources.read`
- `resources.runtime-managed.read`
- `resources.create`
- `resources.manage`
- `observability.read`
- `observability.logs.read`
- `observability.traces.read`
- `observability.metrics.read`
- `CloudShell.Resources/resources/lifecycle/action`
- `CloudShell.Resources/resources/actions/execute/action`
- `CloudShell.Network/networks/reconcileEndpointMappings/action`
- `CloudShell.Network/loadBalancers/applyConfiguration/action`

Resource action permissions use Azure RBAC-style operation names. Standard
lifecycle actions such as start, stop, pause, and restart map to
`CloudShell.Resources/resources/lifecycle/action`. Custom resource actions can
declare a specific operation permission, such as
`CloudShell.Network/loadBalancers/applyConfiguration/action`; otherwise they use
`CloudShell.Resources/resources/actions/execute/action`. `resources.manage`
remains a compatibility superset for resource actions.

Observability permissions gate access to telemetry signal areas. The grouped
`observability.read` permission allows logs, traces, metrics, Dependencies,
Service map, and the telemetry workspace. Signal-specific permissions can be
granted independently with `observability.logs.read`,
`observability.traces.read`, and `observability.metrics.read`. These
permissions do not override resource access: telemetry rows are returned only
for resources the caller can read. Resource-scoped log source metadata, log
reads, and log streams are subject to the same rule. A caller must be able to
read the common logs feature and the owning resource; provider-owned or
artifact-owned log sources without a resource owner are gated by the common
logs permission.

Resource operation permissions should be documented when they are added. The
current resource-type and resource-class operation catalog is:

| Resource type or class | Action | Permission |
| --- | --- | --- |
| Any resource with standard lifecycle actions | `start`, `stop`, `pause`, `restart` | `CloudShell.Resources/resources/lifecycle/action` |
| Any resource with a custom action and no narrower declared operation | custom action execution | `CloudShell.Resources/resources/actions/execute/action` |
| `cloudshell.network` and `cloudshell.virtualNetwork` | `reconcileEndpointMappings` | `CloudShell.Network/networks/reconcileEndpointMappings/action` |
| `cloudshell.loadBalancer` | `applyLoadBalancerConfiguration` | `CloudShell.Network/loadBalancers/applyConfiguration/action` |

Role permissions and group scopes can be replaced in configuration:

```json
{
  "Authentication": {
    "RolePermissions": {
      "Platform.Reader": [ "resource-groups.read", "resources.read" ]
    },
    "RoleResourceGroups": {
      "Platform.Reader": [ "RESOURCE_GROUP_ID" ]
    }
  }
}
```

The role claim type is configured with `Authentication:RoleClaimType`.

## Permission evaluation modes

CloudShell supports four authorization modes. Use the same model for the UI
and Control Plane API so behavior stays consistent in combined and split
hosts:

| Case | Configuration | Behavior |
| --- | --- | --- |
| Permissive local development | `Authentication:Enabled=false` and `Authentication:EvaluateClaimsWhenDisabled=false` | No authentication fallback policy is installed, the Control Plane API is unauthenticated, and CloudShell allows all shell, resource, resource-action, and observability operations. |
| Local permission-boundary testing | `Authentication:Enabled=false` and `Authentication:EvaluateClaimsWhenDisabled=true` | ASP.NET Core authentication remains disabled, but CloudShell evaluates the supplied mock authenticated principal's permissions, scopes, and resource grants. |
| Built-in identity | `Authentication:Enabled=true` and `Authentication:Mode=Identity` | CloudShell uses the built-in ASP.NET Core Identity-backed provider. This is useful for local team testing, simple self-hosting, and gradual identity adoption with in-memory or persisted users. |
| External identity | `Authentication:Enabled=true` with the configured external schemes and role claim mapping | CloudShell evaluates claims from the host's identity provider, such as OIDC/OAuth, Entra ID, Active Directory federation, or another host-integrated authority. |

In-memory users and programmatic grants are test data for the built-in identity
path. They do not turn authorization on by themselves; authorization is
enforced only when authentication is enabled, or when a local test explicitly
sets `EvaluateClaimsWhenDisabled` and supplies a mock authenticated principal.

For the early local development loop, disable authentication and treat the
environment as fully trusted. When you need to prove user boundaries, enable
the built-in provider with in-memory users or use the disabled-authentication
claim-evaluation mode in tests. When the Control Plane or UI is placed on a
shared host, keep `Authentication:Enabled=true` and either continue with the
built-in provider or integrate the host with an external identity provider.
Request-bound resource operations are still audited as user activity in the
permissive local development mode. Without an authenticated principal, the
Control Plane records the generic actor `user`; background work without an HTTP
request remains system activity unless the caller supplies an explicit actor.

## Direct claims

Identity providers can issue permissions and scopes directly:

| Claim type | Value |
| --- | --- |
| `cloudshell.permission` | A permission name or `*` |
| `cloudshell.resource-group` | A resource-group ID, `__ungrouped` for the default group, or `*` |
| `cloudshell.resource` | A resource ID or `*` |

Access requires both an operation permission and a matching scope. A direct
resource claim can grant access to one resource without granting access to its
entire group. Creating resource groups requires wildcard resource-group scope
because the new group ID doesn't exist before creation.

Shell configuration permissions are global. `shell.read` allows a user to view
shell configuration surfaces such as the extension catalog. `shell.configure`
allows a user to change global shell composition, including enabling or
disabling user-managed extensions.

Resources inherit access from their registered root resource or resource group.
Unauthorized registrations are filtered before Resource Manager builds the
resource tree, and write operations are checked again in secured store
decorators.

## Disabling authentication

For isolated development only, set `Authentication:Enabled` to `false`.
Authorization services then allow all operations and no authentication
fallback policy is installed. This also makes the Control Plane API
unauthenticated, so do not use this setting for shared or production hosts.

Local permission-boundary tests can opt into claim evaluation without enabling
the full ASP.NET Core authentication pipeline by setting
`Authentication:EvaluateClaimsWhenDisabled` to `true` and supplying a mock
authenticated `ClaimsPrincipal` through the host or test context. In that mode,
CloudShell evaluates the normal permission, resource-group, resource, and
resource-permission claims instead of allowing every operation. Leave this
disabled for the simplest early-development loop.

Programmatic resource identity declarations are part of the resource model, not
only an unauthenticated development feature. Resources may declare concrete
identity bindings or state that they require an identity whose provider-specific
details are resolved later. When authentication is disabled, CloudShell does not
enforce token-based authorization, but resources can still project identity
bindings for local testing, templates, provider wiring, and UI inspection. A
mock or development identity provider can project deterministic subjects,
scopes, and claims before the same resource is later wired to Microsoft Entra
ID or another production provider.

Resource identity providers can be registered in host configuration. A
`Required` identity binding resolves to `DefaultProviderId`; when exactly one
provider is registered, that provider is the implicit default.

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
