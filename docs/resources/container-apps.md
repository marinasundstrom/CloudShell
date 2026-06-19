# Container Apps

Use the container app resource type for a deployable container workload. These
resources project as `application.container-app`. The container app is the
stable deployment target. It is not the same thing as a Docker container
resource, even when the current host is Local Docker.

Docker or another container host provider may expose runtime containers or
replicas as separate resources for inspection and low-level operations, but
deployment automation should target the container app.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [Executable applications](executable-applications.md) and
[ASP.NET Core applications](aspnet-core-applications.md). SQL Server is a
container-backed authored resource with its own storage guidance; see
[SQL Server resources](sql-server.md). To route traffic to container apps
through a stable provider-neutral routing resource, see [Load balancers](load-balancers.md).
For the tracked product proposal and remaining MVP work, see
[Container applications](../proposals/containers/container-applications.md).

## Host Binding

Container apps can be bound to a specific container host resource, such as Local
Docker, or can rely on the configured default container host. That binding is
host plumbing. Build systems, shell integrations, and users should not need to
know which runtime container instance currently backs the app.

Declare a top-level container app with `AddContainerApplication(...)`:

```csharp
resources
    .AddContainerApplication(
        "application:api",
        "API",
        "team/api:dev",
        registry: "https://registry.example.com")
    .WithContainerHost("docker:dev");
```

`AddContainer(...)` remains available as the Aspire-compatible shorthand for the
same top-level `application.container-app` resource:

```csharp
resources
    .AddContainer("api", "team/api:dev")
    .WithRegistry("https://registry.example.com");
```

This is intentionally different from `resources.AddDocker().AddContainer(...)`,
which creates a Docker container sub-resource parented under a Docker resource.

ASP.NET Core projects can be converted into container apps with
`AsContainer(...)`:

```csharp
resources
    .AddAspNetCoreProject(
        "application:api",
        "API",
        "src/API/API.csproj")
    .AsContainer()
    .WithContainerHost("docker:dev");
```

The converted resource projects as `application.container-app`, but its
workload descriptor retains `ProjectPath`. The default local runner uses that
shape to build the container image with the .NET SDK when no Dockerfile is
supplied, or with the project's Dockerfile when one is specified.

## Lifetime

Programmatic container app declarations default to `ControlPlaneScoped` for
local development. CloudShell starts the container with host-scoped cleanup
semantics where the selected container host supports them, and callers can opt
into a longer-lived container app with `.WithLifetime(ResourceLifetime.Detached)`.

The Resource Manager UI defaults container app registrations to `Detached`
because UI-created resources usually model manually managed or production-like
services that should keep running if the shell restarts.

Detached container recovery is host-specific. A container app should be
rediscovered through the selected container host and the stable container or
replica identity, not through the `docker run` or other host CLI process used to
launch it. Restarting a crashed container app is an orchestrator policy concern
and should be modeled separately from host restart recovery.

## Registry

Container apps can specify a container registry separately from the image name.
The registry defaults to Docker Hub (`docker.io`). Custom registries can be
specified as a host name or URI string. Runtime orchestrators use the URI
authority when they need a pullable image reference, for example
`http://localhost:5000` becomes `localhost:5000/team/api:dev`.

The registry is projected as the non-secret `container.registry` resource
attribute and is included in workload descriptors. Registry credentials are
provider-owned configuration, not resource attributes. Container app and Docker
declarations can specify credentials with
`WithRegistryCredentialsFromEnvironment(username, passwordEnvironmentVariable)`.
The provider reads the password from the named environment variable at
execution time and uses Docker `login --password-stdin` before launching the
container image. Before Start or Restart dispatches, Resource Manager verifies
that the configured password environment variable is present when registry
credentials are configured, and reports an action-unavailable reason without
exposing the password value.

Start and Restart readiness also validate that the selected container host
resource is available, that host credentials are available, and that the host
advertises the capability needed by the workload. Image-backed apps require
`container.image`; project-container builds additionally require
`container.build`.

The Container app registration and configuration tabs expose the registry next
to the image setting. Docker host registration/configuration exposes a registry
setting for Docker child-container resources; that setting also defaults to
`docker.io`.

## Resource Manager Deployment

Container app resources expose image rollout controls on the Deployment tab.
Enter a new image tag and choose whether CloudShell should restart a running app
after the update. The tab calls the same domain
`UpdateResourceImageAsync` operation used by remote clients, then refreshes the
projected container image and revision.

The Deployment tab also shows update readiness before enabling the deploy
command. It reports missing manage permission, missing image input, no-op image
updates, and restart blockers such as a missing or unavailable restart action.
The provider runs the same restart readiness checks before saving a new image
or replica count when automatic restart is requested for a running app, so a
known restart blocker does not leave the app on a partially applied deployment
change.

The same tab shows the app's current internal deployment projection: deployment
status, orchestrator service id, scaling mode, desired replicas, and projected
runtime replicas. This is an inspection surface over CloudShell's internal
orchestrator deployment model, not a public rollout-history or rollback API.

## Service Discovery

Container apps can reference other resources with `WithReference(...)` and opt
into the current Aspire-compatible developer service discovery mapping with
`WithServiceDiscovery()`. Descriptor-based orchestrators receive the same
`services__<resource-name-or-id>__<endpoint-name-or-scheme>__0` environment
variables as local executable resources, so Docker Compose and future
descriptor-driven orchestrators can pass those values into the workload
container. This is the local/programmatic flow; managed on-premise
network-level discovery is a separate future provider capability.

See [Service discovery](../service-discovery.md) for the current Microsoft
service discovery package requirements for applications that consume logical
service URIs.

Developer service discovery remains separate from resource identity. Use
references and developer service discovery to locate another resource's
endpoint in the local/programmatic flow, then use resource identity and grants
when the container app needs authorized access to the service provided by that
resource.

## Replicas

Container apps default to single-instance mode. In that mode the app binds its
own endpoint directly and does not need a load balancer just because it is a
container app.

Replicas are an explicit scaling mode. Resource Manager exposes this on the
Application > Scale and replicas tab, where users enable replicas and set the
desired count.
Programmatic declarations opt in with `.WithReplicas(...)` or by passing a
replica count greater than one to the container app declaration helpers.

Container apps project replica intent through `container.replicas.enabled` and
`container.replicas`. The current MVP supports updating that explicit count;
autoscaling policy, traffic splitting, and replica health are future
resource-model work. The Scale and replicas tab is also diagnostic: it lists
projected runtime replica artifacts only after scaling is enabled.
When updating replicas with automatic restart for a running app, the provider
preflights restart readiness before saving the new desired count.

When a container app has inbound endpoints and replicas are enabled,
CloudShell needs ingress or a load balancer so traffic can be distributed
across instances. The endpoint is still owned by the container app: a single
container binds it in single-instance mode, and an ingress or load balancer
binds it on behalf of the app in replicated mode.
Worker-style replicated apps without inbound endpoints do not require a load
balancer. A later Resource Manager flow should prompt users to assign or
create a load balancer/ingress provider when they enable replicas for an
endpoint-bearing app.

Update the replica count through the Container Apps API:

```http
PUT /api/container-apps/v1/{containerAppId}/replicas
Authorization: Bearer <control-plane-access-token>
Content-Type: application/json

{
  "replicas": 3,
  "restartIfRunning": true,
  "triggeredBy": "load-balancer"
}
```

The API targets the stable container app resource and opts the app into replica
mode. Everything below that resource is provider-owned implementation detail:
a default local container group, a Docker Compose service, a Kubernetes Service
or Deployment, or another runtime-specific management shape. The provider
configures that implementation with the app's current image or revision and
desired replica count, then creates, updates, inspects, or replaces individual
runtime containers as needed.

Inside the orchestration layer, CloudShell represents this management group as
a `ResourceOrchestratorService` descriptor. Container apps produce this
descriptor today. It is built from the container app's workload configuration,
ports, dependencies, networks, and replica count, and it is the
orchestrator-facing descriptor used to group the service contained by the
resource: replicas, endpoint bindings, dependency ordering, network membership,
and related provider-owned runtime services such as app ingress. Docker Compose
maps this descriptor to a Compose service where `deploy.replicas` can be
declared. The descriptor is consumed by orchestrator providers and is not
projected as a Resource Manager resource by default. It is also distinct from
the `cloudshell.service` resource type at the CloudShell model/API layer.

Runtime replica child resources carry the app deployment id, orchestrator
service id, and deployment revision they implement. The app-scoped Scale and
replicas tab shows those identifiers after scaling is enabled so operators can
correlate expected runtime artifacts with the current Deployment tab
projection without enabling global hidden runtime-managed inventory.

Revision management is a separate future Application view. The current
Deployment tab projects the latest revision and image update operation, but
CloudShell does not yet expose rollout history, rollback, activation, or
traffic splitting.

A `cloudshell.service` resource can still be
declared when a stable CloudShell Service resource or facade should expose
non-application targets, multiple targets, imported provider-native services,
or advanced routing. A normal container app does not require a
`cloudshell.service` resource to expose its app-owned endpoint, but a future
orchestrator may materialize an explicitly modeled `cloudshell.service` as its
provider-native service primitive when that resource represents the service
unit.

Runtime replica containers are not normal Resource Manager management targets.
CloudShell may project them as hidden runtime-managed child resources of the
container app for diagnostics and relationship inspection. The current
application provider projects desired replica/container children from the
orchestrator service descriptor with replica ordinal, replica count, container
name, and revision metadata. Provider-observed container IDs, placement, health,
and materialization state are future enrichment. Hidden replica resources are
not automatically internal artifacts: they can remain part of the resource
graph for the container app while staying out of the top-level inventory by
default. Resource Manager decides whether to present them on app-owned views.
Resource Manager only shows them in global inventory when both hidden resources
and runtime-managed resources are enabled for the current user, and
runtime-managed inspection requires the
`resources.runtime-managed.read` permission. Provider-owned helper containers
or other pure implementation details should stay internal and are not part of
the default user-facing graph. A future runtime-managed resource that is part of
the public application surface can use normal visibility and remain visible
without being treated as an internal artifact.

When multiple local containers are materialized, they are named by convention
from the parent container app, for example with a `-replica-{n}` suffix. Docker
Compose maps the same desired count to `deploy.replicas`; future orchestrators
should map it to their native service and replica abstractions without changing
the CloudShell API shape.

## Ingress

Container app endpoints are app-owned. In single-instance mode the one
container binds the endpoint directly. When a replicated container app exposes
an HTTP or TCP endpoint, CloudShell needs an ingress/load-balancing strategy
for that app so traffic can reach all instances. The projected container app
endpoint remains the URL or address users call; an ingress or load balancer
binds that endpoint on behalf of the app and maps traffic to the replicated
runtime instances. Callers do not address individual replica containers
directly.

For MVP, ingress is not a separate top-level resource type. Treat app ingress
as provider-managed exposure for a container app endpoint. The container app
remains the resource that users configure and operate; the provider decides
whether that endpoint is backed by a directly published container port,
provider-owned ingress infrastructure, or an explicit load balancer selected by
the user.

For the default Docker runner, that ingress is currently implemented as a
provider-owned Traefik container attached to the same Docker network as the app
replicas. It owns the host-published app port and balances to the convention
named replica containers. Single-replica apps keep the direct published-port
path.

Docker Compose follows the same app-owned model when CloudShell generates the
Compose file: the application remains one Compose service with
`deploy.replicas`, and replicated services with published HTTP or TCP ports get
a generated Traefik sidecar plus labels so traffic is routed to the Compose
service replicas. This keeps Compose service DNS and replica management as the
runtime implementation detail instead of exposing individual containers as
CloudShell resources.

Load balancers should target the stable container app or another stable
Resource Manager artifact when the user wants gateway-level control beyond a
single app's ingress. That is the path for shared host/path/TCP rules, public
front doors, custom domains, TLS policy, or routing across more than one stable
target. Optional `cloudshell.service` resources can be used as logical facades
for scenarios that need that extra indirection. They can also represent a
manually composed service unit or replica set, for example several web
application instance resources behind one shared Service resource frontend that
a load balancer targets. The replica containers themselves still remain runtime
artifacts, not separate Resource Manager resources.

Resource Manager should expose ingress through the Container App experience:

- Overview shows the best reachable address.
- Networking > Endpoints shows the app-owned endpoint contract.
- Scaling warns that endpoint-bearing replicated apps need provider-managed
  ingress or an explicit load balancer.
- Future exposure sections can show whether the endpoint is directly bound,
  provider-ingressed, virtual-network mapped, or load-balancer routed.

## Image Deployment Procedure

The proposed deployment flow for CloudShell-hosted dev environments is:

1. The build server builds the application image.
2. The build server tags the image with an immutable value, usually the commit
   SHA, build number, or release version.
3. The build server pushes the image to the configured registry.
4. The build action calls the authenticated Container Apps API to create a new
   container app revision that points at the pushed image tag.
5. The Control Plane updates the container app, records a resource event with
   the actor/trigger, creates a new revision value, and asks the provider to
   restart the app when requested.

The API call targets the container app, not an underlying Docker container:

```http
POST /api/container-apps/v1/{containerAppId}/revisions
Authorization: Bearer <control-plane-access-token>
Content-Type: application/json

{
  "image": "team/api:20260608.42",
  "restartIfRunning": true,
  "triggeredBy": "build:20260608.42"
}
```

Authentication is required whenever CloudShell authentication is enabled. A
build action should use a Control Plane credential intended for
service-to-service automation, such as a client secret, client certificate, or
equivalent static credential for local/dev-only environments. The credential
must authorize the build identity to manage the target container app or its
resource group.

## Revisions

The image tag update creates a new app-owned revision. The revision is projected
on the container app resource through `container.revision`; runtime containers
or replicas implement that revision but do not define it.

The Resource Manager overview shows the latest projected revision. A richer
revision history needs a dedicated design because revisions represent commits
of changes to the container app configuration, not just image tags.

This is intentionally similar to Azure Container Apps at the basic concept
level: a deployment produces a revision of the app. CloudShell's MVP keeps the
revision model simple and does not yet model traffic splitting, activation
state, or rollout history as first-class concepts.

## Logs And Events

The `triggeredBy` value from the deployment request is written to the resource
event stream so deployments can be traced back to the build action, user, or
external system that requested them.

Container apps should also expose console logs from the underlying workload as
resource-type-specific logs. Those logs show stdout/stderr from the running
container. They complement, but do not replace, the platform-owned `Resource
events` stream that records who or what changed the resource.

## Telemetry Scope

Container app Telemetry views are app-scoped by default. Logs, Traces, and
Telemetry Metrics should open on the stable container app resource even when
the app is implemented by multiple runtime replicas. Users should not need to
open hidden runtime-managed replica resources for normal telemetry
investigation.

When only one runtime instance is observed, the Telemetry views should not
show a scope selector. When multiple runtime instances are observed, the views
should default to `All instances` and expose a compact scope or instance
selector for individual replicas or containers. Logs can use that scope to
filter source output. Traces stay trace-first and service-aware, so a scope
filter narrows spans rather than redefining trace ownership. Telemetry Metrics
should default to app-level aggregate data with optional per-scope filtering
or breakdowns.

Telemetry records need the stable app `resourceId` plus optional runtime
dimensions such as runtime resource ID, replica ordinal, replica count,
container name, and deployment revision before Resource Manager can implement
that selector consistently across Logs, Traces, and Metrics. Provider-observed
CPU, memory, restart count, uptime, and container status remain Resource
Metrics under Management > Monitoring.
