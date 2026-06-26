# Docker Container Reference Provider

## Overview

- Resource type: `docker.container`
- Provider id: `docker`
- Purpose: declares a provider-projected Docker container artifact in the Resource Graph.

## Ported

- Container class/type defaults.
- Workload, image, registry, replica, and read-only endpoint-count attributes.
- Passive monitoring and log-source capability markers.
- Lifecycle operations with an injected provider-owned runtime handler seam and
  status-aware action availability.
- Resource Manager bridge state projection through the runtime handler seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.
- Manual `ResourceDefinitionGraphBuilder.AddDockerContainer(...)` builder for
  code-first Docker container definition authoring.

## Runtime Integration

The provider declares Docker container lifecycle operations in the graph, but
does not own Docker runtime materialization itself. Runtime behavior is
supplied by registering an `IDockerContainerRuntimeHandler` implementation in
the host or Control Plane integration layer.

```csharp
services.AddSingleton<IDockerContainerRuntimeHandler, DockerContainerRuntimeHandler>();
services.AddDockerContainerResourceType();
```

The default `NoopDockerContainerRuntimeHandler` keeps the provider usable for
graph/projection tests and reports unknown runtime state. A real handler is
expected to interpret the resolved `DockerContainerResource`/`Resource` state,
apply lifecycle operations through the runtime it owns, project runtime status
through the Resource Manager bridge when needed, and return diagnostics instead
of throwing for expected runtime outcomes.

The ContainerAppDeployment sample has an opt-in sample-local handler for the
graph registry container behind
`ContainerAppDeployment:EnableGraphDockerRuntime=true`. It exists to validate
the runtime boundary without making normal sample projection shell out to
Docker on every Resource Manager state projection. A durable Docker handler and
full lifecycle smoke coverage remain provider work.

## Remaining

- Real Docker API integration behind the runtime handler.
- Runtime discovery, concrete runtime-backed state projection, log streaming,
  endpoint projection, lifecycle smoke coverage, and hidden/runtime-managed
  Resource Manager behavior.
