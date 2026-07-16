# Container Application Built-in Provider

## Overview

- Resource type: `application.container-app`
- Provider id: `applications.container-app`
- Purpose: declares a containerized application workload in the Resource model.

## Ported

- Image, registry, build, and replica attributes with root authored paths.
- Endpoint request attributes using the shared networking endpoint request shape.
- Type-level endpoint-source and monitoring expectations, with log-source
  declarations authored as `logs.sources` attributes on graph container-app
  resources.
- Optional typed generic/Docker container-host reference validation and projection.
- Shared volume-consumer capability.
- Start, stop, restart, image-update, and replica-update operation seams with
  an injected provider-owned runtime handler seam. Image and replica seams are
  migration bridge hooks for accepted graph changes and should be revisited before
  deciding which non-lifecycle operations are exposed as resource actions.
- Typed wrapper plus Resource Manager bridge projection, endpoint projection, and execution.
- ContainerAppDeployment and ReplicatedContainerHealth sample-inspired graph coverage,
  including ContainerAppDeployment image/replica update delegation and
  ReplicatedContainerHealth smoke coverage where graph start, stop, restart,
  image-update, and replica-update actions delegate to provider-owned runtime
  adapters and stop verifies Docker runtime container cleanup.
- Manual `ResourceGraphBuilder.AddContainerApplication(...)`
  builder for code-first container app definition authoring with typed host
  dependencies, endpoint requests, replicas, and volume mount attributes.
- Provider-owned Resource Manager UI registration for Resource model samples.
  The UI uses `application.container-app` as the type id trigger for container-app
  deployment, revision, scale, monitoring, and endpoint-action UI instead of
  depending on the legacy application provider's concrete resource model.
- Resource Manager projection parity for replica-mode UI triggers. Graph state
  keeps the provider-owned `container.replicas` attribute as the declarative
  value while authored definitions use `replicas`; the Resource Manager bridge
  derives compatibility facts such as
  `container.replicas.enabled` and `deployment.replicas.requestedSlots` so
  existing deployment and monitoring views render replicated apps correctly.
  Runtime-projected replica children declare their operational monitoring and
  log-source capabilities directly because they are derived runtime resources,
  not authored graph definitions.

## Switch-over status

Ready to start integration for the container app scenarios covered by
ContainerAppDeployment and ReplicatedContainerHealth. The Resource model path can
start, stop, restart, update image/replica intent, project endpoints, drive the
container-app UI tabs by type id, and expose runtime replica logs, health,
traces, metrics, and monitoring through the current runtime adapters. Full old-provider
parity is not expected yet: rich revision history, richer startup-state
projection, and old edit surfaces are
explicitly deferred.

## Runtime Integration

The provider declares container app operations in the graph, but does not own
container orchestration itself. Runtime behavior is supplied by registering an
`IContainerApplicationRuntimeHandler` implementation in the host or Control
Plane integration layer.

```csharp
services.AddSingleton<IContainerApplicationRuntimeHandler, DockerContainerApplicationRuntimeHandler>();
services.AddContainerApplicationResourceType();
```

The default `NoopContainerApplicationRuntimeHandler` keeps the reference
provider usable for graph/projection tests and reports unknown runtime state.
A real handler is expected to interpret the resolved
`ContainerApplicationResource`/`Resource` state, project runtime status through
the Resource Manager bridge when needed, apply the operation through the
runtime it owns, and return diagnostics instead of throwing for expected
runtime outcomes.

For local development and migration samples, the provider includes an opt-in
process-backed runtime adapter. It maps a container app resource id to a local
.NET project, starts one process per requested replica slot, exposes the
declared endpoint through a local HTTP/WebSocket proxy, and preserves sticky
SignalR routing by keeping negotiated connection tokens on the selected
replica.

```csharp
services
    .AddLocalContainerApplicationResourceTypes()
    .AddLocalContainerApplicationProcessRuntime(options =>
        options.AddProject(
            "application.container-app:api",
            "/workspace/samples/MyApp/Api/MyApp.Api.csproj"));
```

This adapter is intentionally a local runtime bridge, not the durable
orchestrator. It removes the need for each sample or migrated provider to
implement its own `IContainerApplicationRuntimeHandler` while the container app
orchestration path is completed.

The provider also owns the local container-app command runner abstraction used
by runtime adapters that still shell out to `dotnet` or local container tools.
Samples can replace `ILocalContainerApplicationCommandRunner` for deterministic
tests, but normal hosts get `ProcessLocalContainerApplicationCommandRunner`
from `AddContainerApplicationResourceType()`.
Docker-compatible commands flow through the shared container-host command
platform, so Docker, Podman, host-configured executable metadata, and missing
runtime diagnostics are resolved before process invocation. Non-container
commands such as `dotnet` and Java build tools continue to use direct process
start information.
The same command-runner path is used by runtime lifecycle, status, log,
monitoring, and replica-slot materialization helpers, so missing container
runtime prerequisites produce consistent unavailable or unknown-runtime
outcomes across those surfaces.

Projected runtime container replicas with `runtime.container` type and
`containerReplica` runtime metadata automatically get provider-owned Docker
log and stats support. The runtime projection provider still decides whether
to surface those hidden replica resources, but once it does, the container-app
provider supplies the `ILogProvider` and `IResourceMonitoringProvider`
implementation.

The provider also includes a deferred runtime adapter for migration scenarios
that need graph image/replica changes to be accepted without materializing a
real container app runtime. Deferred runtime handlers report unknown runtime
state and return a warning for lifecycle actions so hosts do not present them
as running app runtimes.

```csharp
services
    .AddLocalContainerApplicationResourceTypes()
    .AddDeferredContainerApplicationRuntime(options =>
        options.AddResource("application.container-app:api"));
```

For migration hosts that still need explicit runtime mapping, the provider
includes an opt-in delegating runtime handler. Targets implement
`IContainerApplicationRuntimeTarget`, declare whether they can handle a
resolved graph resource, and receive lifecycle, image, replica, and
orchestrator-service routing calls through the provider-owned
`DelegatingContainerApplicationRuntimeHandler`.

```csharp
services
    .AddLocalContainerApplicationResourceTypes()
    .AddLocalDockerContainerApplicationRuntime();
```

The local Docker runtime handles graph `application.container-app` resources
from their declared resource state. Image-only apps run the declared image
directly. Apps with authored `buildContext` build through Docker, and apps
with `project.path` publish through the .NET SDK container target when no
Dockerfile-backed build context is supplied. The runtime synthesizes local
container, network-alias, ingress, and hidden replica resource names from the
container app resource id, so a host does not need to repeat each app in
runtime configuration. It also derives Docker-reachable trace and metric
ingest endpoints from the host's configured CloudShell endpoint when explicit
observability endpoints are not set.

`AddApplication(...)` remains available as an override path for migration
hosts and samples that need stable local names, custom ingress directories,
probe ports, telemetry endpoints, or project mapping while older declarations
are still being moved into resource attributes. It is not required for normal
image-backed or project-backed container app declarations.

The ReplicatedContainerHealth sample currently proves this seam with the
provider-owned local Docker container-app runtime. That adapter builds the
image when project metadata is available, creates replica containers,
reconciles the local Traefik ingress container, and projects hidden runtime
replicas with the metadata consumed by the provider-owned log and monitoring
providers. The Docker smoke verifies that graph image updates recreate
revision-scoped runtime containers, replica updates retain active slots where
possible, and graph stop removes the containers that graph start created.

The ContainerAppDeployment sample uses the provider-owned deferred runtime
adapter for `application.container-app:sample-api`. It accepts image and
replica updates through the existing Resource Manager deployment and replicas
APIs without registering the old application-provider resource. Those
operations are migration adapter hooks; lifecycle actions report deferred
runtime materialization. In the current API path, deployment and scale remain
Control Plane workflows, and the durable container runtime provider remains
future work.

## Example ResourceDefinition

This is the interchange shape a deployment, template, or import can use to
declare a replicated container app. Runtime materialization is still handled by
the Control Plane/provider integration. The example should also be read as a
usability check for the `ResourceDefinition` interchange API: if this shape is
hard to author or round-trip, the provider port should feed that back into the
model cleanup work after switch-over.

```json
{
  "name": "api",
  "typeId": "application.container-app",
  "resourceId": "application.container-app:api",
  "providerId": "applications.container-app",
  "displayName": "Replicated API",
  "dependsOn": [
    {
      "value": "docker.host:sample",
      "relationship": "dependsOn",
      "addressingMode": "resourceId",
      "typeId": "docker.host",
      "providerId": "docker"
    }
  ],
  "image": "cloudshell-application-api:20260622.2",
  "registry": "docker.io",
  "replicas": 3,
  "endpoints": [
    {
      "name": "http",
      "protocol": "http",
      "targetPort": 8080,
      "host": "localhost",
      "port": 5092,
      "exposure": "Local"
    }
  ],
  "attributes": {
    "health.checks": {
      "checks": [
        {
          "name": "health",
          "type": "health",
          "source": {
            "kind": "http",
            "http": {
              "path": "/health",
              "endpointName": "http"
            }
          }
        },
        {
          "name": "alive",
          "type": "liveness",
          "source": {
            "kind": "http",
            "http": {
              "path": "/alive",
              "endpointName": "http"
            }
          }
        }
      ]
    }
  }
}
```

## Remaining

- Actual container host orchestration through a runtime handler implementation.
- Rich revision history, replica runtime state, and old edit surfaces such as
  configuration and storage updates.
- Continue auditing attribute and capability parity against the old provider
  and provider-specific UI. Future fixes should prefer stable resource
  type/class/capability/attribute triggers over concrete old provider classes.
