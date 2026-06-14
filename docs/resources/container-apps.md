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
container image.

The Container app registration and configuration tabs expose the registry next
to the image setting. Docker host registration/configuration exposes a registry
setting for Docker child-container resources; that setting also defaults to
`docker.io`.

## Resource Manager Deployment

The Resource Manager overview for a container app includes a deploy image
control. Enter a new image tag and choose whether CloudShell should restart a
running app after the update. The shell calls the same domain
`UpdateResourceImageAsync` operation used by remote clients, then refreshes the
projected container image and revision.

## Service Discovery

Container apps can reference other resources with `WithReference(...)` and opt
into the current application-level service discovery mapping with
`WithServiceDiscovery()`. Descriptor-based orchestrators receive the same
`services__<resource-name-or-id>__<endpoint-name-or-scheme>__0` environment
variables as local executable resources, so Docker Compose and future
descriptor-driven orchestrators can pass those values into the workload
container.

See [Service discovery](../service-discovery.md) for the current Microsoft
service discovery package requirements for applications that consume logical
service URIs.

Service discovery remains separate from resource identity. Use references and
service discovery to locate a service endpoint, then use resource identity and
grants when the container app needs authorized access to that service.

## Replicas

Container apps project their desired replica count through the
`container.replicas` attribute. The current MVP supports updating that explicit
count; autoscaling policy, traffic splitting, and replica health are future
resource-model work.

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

The API targets the stable container app resource. Everything below that
resource is provider-owned implementation detail: a default local container
group, a Docker Compose service, a Kubernetes Service or Deployment, or another
runtime-specific management shape. The provider configures that implementation
with the app's current image or revision and desired replica count, then
creates, updates, inspects, or replaces individual runtime containers as needed.

Inside the orchestration layer, CloudShell represents this management group as
a `ResourceOrchestratorService` descriptor. The descriptor is built from the
container app's workload configuration, ports, dependencies, networks, and
replica count. It is consumed by orchestrator providers and is not projected as
a separate Resource Manager resource. It is also distinct from the
`cloudshell.service` resource type, which can still be declared when a stable
platform endpoint should expose one or more target resources.

Runtime replica containers are not Resource Manager targets. When multiple
local containers are materialized, they are named by convention from the parent
container app, for example with a `-replica-{n}` suffix. Docker Compose maps
the same desired count to `deploy.replicas`; future orchestrators should map it
to their native service and replica abstractions without changing the
CloudShell API shape.

## Ingress

Container app endpoints are app-owned ingress by default. When a replicated
container app exposes an HTTP or TCP endpoint, the default Docker runner starts
provider-owned ingress infrastructure for that app as part of the normal
start/restart lifecycle. The projected container app endpoint remains the URL or
address users call; they do not need to create a separate load-balancer
resource or manually apply routing configuration for the common case.

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

Load balancers and `cloudshell.service` resources should target the stable
container app or another stable Resource Manager artifact when the user wants
gateway-level control beyond a single app's ingress. The replica containers
themselves still remain runtime artifacts, not separate Resource Manager
resources.

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
