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

## Remaining

- Real Docker API integration behind the runtime handler.
- Runtime discovery, concrete container state projection, log streaming,
  endpoint projection, and hidden/runtime-managed Resource Manager behavior.
