# Control plane API

CloudShell should treat control-plane services as separate products from the WebUI shell. The shell remains customizable and extensible; shell integrations connect the shell to control-plane services through versioned API contracts.

## Naming

Use **CloudShell Control Plane** for the service and **CloudShell Control Plane API** for its HTTP contract.

Avoid naming the WebUI host as the control plane. The WebUI is the shell surface. The control plane owns resource inventory, registrations, lifecycle procedures, logs, and provider-backed operational data.

## Versioning

The first HTTP API is versioned as `v1`:

```text
/api/control-plane/v1
```

The matching OpenAPI document is:

```text
/openapi/control-plane-v1.json
```

Configuration services expose a provider-owned runtime API under
`/api/configuration`. That API is separate from the Control Plane API because it
is consumed directly by resource processes and uses configuration-service access
tokens instead of user/session authentication. See
[Configuration services](configuration-services.md).

Resource-type API groups can exist when a domain operation needs a specialized
contract surface. For example, container app image deployment is exposed as a
Container Apps revision operation, and container app replica count updates use
the same resource-type API group:

```text
POST /api/container-apps/v1/{containerAppId}/revisions
PUT /api/container-apps/v1/{containerAppId}/replicas
```

That endpoint still uses the Control Plane authentication boundary,
authorization checks, OpenAPI document, domain manager implementation, and
normal ProblemDetails error contract. Image update preflight failures use
`resourceImageUpdateUnavailable`, and replica update preflight failures use
`resourceReplicasUpdateUnavailable`, preserving the provider's readiness
message in `detail`. It is separate from `/api/control-plane/v1/resources` so
the core Resource Manager route group does not accumulate
resource-type-specific commands. See [Container apps](resources/container-apps.md).

Load balancers currently use the core Resource Manager API. Routes are projected
on the resource response, and provider application runs through the advertised
`applyLoadBalancerConfiguration` resource action. See
[Load balancers](resources/load-balancers.md).

Resource monitoring uses resource-scoped Control Plane API routes because it is
provider-observed management data for a selected resource, not application
telemetry ingestion:

```text
GET /api/control-plane/v1/resources/{resourceId}/monitoring/availability
GET /api/control-plane/v1/resources/{resourceId}/monitoring
```

The first route lets Resource Manager decide whether to show the generated
Management > Monitoring tab. The second route returns the current provider
snapshot when one is available. See
[Resource monitoring](proposals/core/resource-monitoring.md).

Resource procedure responses return a primary `message`, optional restart
metadata, and a `signals` collection. Signals use the shared Resource Manager
severity vocabulary (`Success`, `Information`, `Warning`, `Error`) and carry
secondary procedure outcomes that should remain distinct from the main
completion message. For example, a start action can complete while dependency
auto-start runs in warn-and-continue mode and returns a warning signal for the
dependency that failed to start.

Application telemetry remains resource-owned. `ResourceResponse.observability`
projects the stable signal declarations, provider-declared telemetry sources,
and selectable telemetry scopes for a resource. Consumers can list retained
traces and metrics by resource and optionally pass `scopeResourceId`,
`scopeName`, `scopeKind`, or `deploymentRevision` to narrow the result set.
Source metadata is advisory collection metadata: providers use it to describe
provider-owned streams, OpenTelemetry exporters such as OTLP, or
Prometheus/OpenMetrics-style endpoints. The retained telemetry APIs stay
query-oriented so shell views do not need to know which standards-based
transport or provider path produced the data.

The current API surface remains snapshot/list based while CloudShell proves
basic monitoring support across resource providers. Future live telemetry and
resource monitoring updates for split-hosted UIs should use Control
Plane-owned streaming subscriptions rather than WebUI-to-provider
connections. That future streaming contract should be designed as a versioned
Control Plane API capability with the same authentication and authorization
boundary as the HTTP routes. SignalR over WebSockets is a likely ASP.NET Core
transport, while the domain shape should stay transport-neutral enough to
support polling fallbacks or other streaming transports later.

Keep breaking changes behind a new route and document, such as
`/api/control-plane/v2` and `/openapi/control-plane-v2.json`. Remote
control-plane adapters should pin the generated client to the major API version
they support.

## Shell integrations

Shell integrations should depend on the `IControlPlane` facade, or
one of its narrower resource, template, log, trace, metric, or monitoring
manager facets, instead of
in-process control-plane stores, providers, or generated Web API clients. The
facade is intentionally domain-shaped: consumers ask to list resources, execute
resource actions, read logs, inspect resource monitoring, or import templates
without caring whether those operations are local service calls or HTTP
requests.

In combined hosts, CloudShell registers `InProcessControlPlane`, which
adapts the existing services. In split hosts, an integration registers a remote
implementation that maps the same interfaces to generated OpenAPI clients.

```csharp
builder.Services.AddRemoteControlPlane(
    new Uri("https://control-plane.example.com"));
```

A typical remote adapter owns:

- a generated `v1` control-plane client
- configuration for the target control-plane service base URL
- authentication/token forwarding for calls from the shell to the control plane
- mapping between Web API DTOs and the CloudShell domain abstraction

This keeps the WebUI deployable and versionable separately from the service
while preserving the extension model described in
[shell customization design goals](shell-customization.md).

Provider packages that run inside the Control Plane can keep using lower-level
interfaces such as `IResourceManagerStore`, `IResourceRegistrationStore`,
`ILogStore`, and provider contracts. Those interfaces are implementation
interfaces for the service process; `IControlPlane` is the shell and
integration boundary.

## Authentication

Implemented today, the Control Plane API uses the configured ASP.NET Core
authentication pipeline and CloudShell authorization checks. It is protected
whenever CloudShell authentication is enabled. Disable that boundary only for
isolated local development by setting `Authentication:Enabled` to `false`; in
that mode, the Control Plane API is intentionally unauthenticated and
authorization allows all operations, including resource inspection, resource
actions, and observability queries. Local permission-boundary tests can still
exercise claim evaluation while ASP.NET Core authentication is disabled by
setting `Authentication:EvaluateClaimsWhenDisabled` to `true` and supplying a
mock authenticated principal.

The Control Plane API should use the same authorization decisions regardless of
whether it is called in-process or over HTTP. The transport is the part that
changes:

- Combined hosts use the current ASP.NET Core authenticated user directly.
- Split hosts call the API over HTTP with authentication material for the
  Control Plane protected API resource, supplied by the remote adapter's
  configured Control Plane credential.

The remote adapter supports a provider-neutral credential abstraction for
Control Plane calls. Built-in modes cover no credentials, static bearer tokens,
client-credentials tokens issued by the Control Plane token authority, and
CloudShell resource credentials.

Authored services that run as CloudShell resources should use the same
Azure-like SDK pattern as built-in services: create a
`DefaultCloudShellResourceCredential` and pass it to the Control Plane client
instead of moving raw tokens, client IDs, or secrets through domain method
calls. See [SDK clients](sdk-clients.md) for package boundaries and the
shared credential contract.

```csharp
var credential = new DefaultCloudShellResourceCredential();
var controlPlane = new RemoteControlPlane(
    new Uri("https://control-plane.example.com"),
    credential,
    ["ControlPlane.Access"]);

var resources = await controlPlane.ListResourcesAsync();
```

DI registration supports the same object-based credential flow:

```csharp
builder.Services.AddRemoteControlPlane(
    new Uri("https://control-plane.example.com"),
    new DefaultCloudShellResourceCredential(),
    ["ControlPlane.Access"]);
```

This is the default platform SDK shape for the MVP. Service-specific SDK
clients, such as future configuration-store or secrets-vault clients, should
layer over the same `CloudShellResourceCredential` contract instead of defining
independent credential mechanisms.

For OAuth-based deployments, remote adapters should request a token for the
Control Plane API resource and required scope, then attach it to each request:

```http
Authorization: Bearer <control-plane-access-token>
```

The Control Plane service validates the credential, builds the
`ClaimsPrincipal`, and then applies the CloudShell permission and resource-scope
checks described in [Authentication and authorization](authentication-and-authorization.md).
For OAuth, validation includes the issuer and the Control Plane API
audience/resource.

In browser-based split deployments, prefer a server-side UI/BFF boundary. The
browser authenticates to the UI host with its normal cookie, and the UI host
configures a Control Plane credential for the remote `IControlPlane` adapter.
This keeps Control Plane credentials out of browser storage unless the
deployment explicitly chooses a public-client flow.

Model this after Azure SDK credentials: callers configure a credential object,
the client requests authentication material for the Control Plane protected API
resource, and the HTTP pipeline attaches the required headers or metadata.
Application and extension code should not pass raw tokens, API keys, or
provider-specific credentials through `IControlPlane` method calls.

In Azure-style OAuth configuration, each separately hosted service is an API
resource. The Control Plane API should have its own resource identifier, such
as `api://cloudshell-control-plane`, with delegated scopes or app-only
permissions defined on that resource. Other authentication providers can expose
the Control Plane as a service identity, gateway route, mTLS identity, signed
request target, or another protected API abstraction.

This does not mean every CloudShell inventory resource is automatically an
independently protected API resource. Register separate provider-specific
authentication metadata only for hosted services that accept direct protected
calls. Resources accessed only through the Control Plane use the Control Plane
authentication boundary and CloudShell's resource authorization checks.

For direct calls to a resource service API, the resource service is the policy
enforcement point. A service running in its own process or container must
validate the credential itself, even when CloudShell registered the resource,
started the container, or provided endpoint and credential metadata.

The protected resource metadata described here is also directional. Today,
CloudShell protects the Control Plane API boundary and leaves direct service API
protection to the service implementation.

## OpenAPI Client Generation

Use generated C# clients inside remote `IControlPlane`
implementations. The control-plane service should own the OpenAPI document; the
remote adapter consumes a pinned document and generates typed clients during
build or as part of package generation. UI and extension code should depend on
the domain managers, not the generated client.

The OpenAPI document must describe the domain-shaped resource projection. The
`GET /api/control-plane/v1/resources` response schema references
`ResourceResponse`, and `ResourceResponse` includes `resourceClass`,
`attributes`, `identity`, and `resourceActions`. `identity` is an optional
resource identity binding with kind, provider ID when resolved, subject, scopes,
and non-secret claim metadata. `resourceActions` is a dictionary keyed by
action ID whose values include required permission, method, and href
affordances. Creation requests include `startAfterCreate` as an explicit
lifecycle option.

Resource permission grants are exposed through the same domain-shaped Control
Plane API:

```text
GET /api/control-plane/v1/resource-principals
GET /api/control-plane/v1/resource-permission-grants
POST /api/control-plane/v1/resource-permission-grants
POST /api/control-plane/v1/resource-permission-grants/revoke
POST /api/control-plane/v1/resource-permission-grants/evaluate
POST /api/control-plane/v1/identity-providers/{providerId}/setup
POST /api/control-plane/v1/resources/{resourceId}/identity/provision
GET /api/control-plane/v1/resources/{resourceId}/identity/provisioning-status
```

These endpoints list principals, list declared grants, assign or revoke grant
intent, and evaluate whether the declared model contains a matching grant.
Principal lookup combines resource identities from the CloudShell resource
model with provider-backed directory data from identity provider integrations.
The built-in provider can also expose in-memory test users for local
development.
Assigning or revoking a grant updates CloudShell's desired access model;
applying that change to provider-owned identity systems is part of identity
provider provisioning or reconciliation. Resource action execution can also
include `actingIdentityResourceId` and optional `actingIdentityName` query
values; when present, the Control Plane evaluates declared grants for that
resource identity instead of using the current user's resource permissions.
This is model-level enforcement for declared resource identities. The API does
not yet prove the acting identity with a token or register grants with an
external identity authority.

Grant list filters use principal terminology:

```text
GET /api/control-plane/v1/resource-permission-grants?principalKind=1&principalId=alice
GET /api/control-plane/v1/resource-permission-grants?principalKind=0&principalId=application%3Aapi%2Fidentities%2Fapi-service
GET /api/control-plane/v1/resource-permission-grants?targetResourceId=configuration%3Aapp
```

Grant and revoke request bodies carry a `principal` object:

```json
{
  "principal": {
    "kind": 1,
    "id": "alice",
    "displayName": "Alice Local Developer",
    "providerId": "identity:built-in"
  },
  "targetResourceId": "configuration:app",
  "permission": "resources.manage"
}
```

For resource identity principals, `principal.id` is the stable principal ID
derived from the resource identity, while `sourceResourceId` and
`sourceIdentityName` preserve the resource binding that produced it.

Identity provider setup asks the provider to reconcile provider-level
configuration such as OIDC client mappers, admin API reachability, trust
metadata, or other provider-owned bootstrap that is not specific to one
resource identity. Identity provisioning asks the resolved provider to
materialize a declared resource identity and matching grants. Provisioning
status asks the provider for observed state; CloudShell treats that state as
provider-owned rather than persisting it in resource metadata.

The `CloudShell.ControlPlane.Client` package provides the default remote adapter
for the current API shape. A generated OpenAPI client can replace its internal
HTTP calls later without changing shell integrations.

For .NET projects that still use the OpenAPI MSBuild reference flow, keep the
reference in the adapter `.csproj` instead of running ad hoc generator commands:

```xml
<ItemGroup>
  <OpenApiReference
    Include="OpenAPIs/control-plane-v1.json"
    CodeGenerator="NSwagCSharp"
    Namespace="CloudShell.Integrations.ControlPlane.V1"
    ClassName="ControlPlaneApiClient">
    <Options>/GenerateClientInterfaces:true /InjectHttpClient:true /DisposeHttpClient:false</Options>
  </OpenApiReference>
</ItemGroup>
```

For .NET 10 and later, do not depend on
`Microsoft.Extensions.ApiDescription.Client` for new adapter packages. Microsoft
deprecated that package and its `OpenApiReference`/`dotnet openapi` MSBuild
flow. Use generator-specific tooling instead, usually an explicit NSwag MSBuild
target or a committed generated client refreshed by CI.

Recommended NSwag configuration:

```json
{
  "runtime": "Net110",
  "documentGenerator": {
    "fromDocument": {
      "url": "https://control-plane.example.com/openapi/control-plane-v1.json"
    }
  },
  "codeGenerators": {
    "openApiToCSharpClient": {
      "className": "ControlPlaneApiClient",
      "namespace": "CloudShell.Integrations.ControlPlane.V1",
      "injectHttpClient": true,
      "disposeHttpClient": false,
      "generateClientInterfaces": true
    }
  }
}
```

Prefer generating clients into the remote adapter package, not into the shell
host. That keeps shell integrations free from transport details and allows
different adapters to support different control-plane service versions. If an
adapter supports multiple API versions, generate separate clients and namespaces
per version, for example `CloudShell.Integrations.ControlPlane.V1` and
`CloudShell.Integrations.ControlPlane.V2`.
