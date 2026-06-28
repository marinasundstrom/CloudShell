# Docker Container Reference Provider

## Overview

- Resource type: `docker.container`
- Provider id: `docker`
- Purpose: declares a provider-projected Docker container artifact in the Resource Graph.

## Ported

- Container class/type defaults.
- Workload, image, registry, replica, and read-only endpoint-count attributes.
- Programmatic builder activation for runtime monitoring and log-source
  capability markers. Raw Docker container definitions do not implicitly
  activate observability capabilities.
- Lifecycle operations with an injected provider-owned runtime handler seam and
  status-aware action availability.
- Resource Manager bridge state projection through the runtime handler seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.
- Manual `ResourceDefinitionGraphBuilder.AddDockerContainer(...)` builder for
  code-first Docker container definition authoring with typed Docker host
  dependencies.

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
The same sample now uses the Docker host, Docker container, and container app
builders for its side-by-side graph declarations instead of raw graph state
dictionaries.

## Switch-over status

Partially ready behind the opt-in ContainerAppDeployment graph Docker runtime
seam. It is useful for validating the runtime boundary for standalone Docker
containers, but it should not block the broader provider switch because normal
container-app sample workflows are covered by `application.container-app`.
Durable Docker API integration, runtime discovery, endpoint projection, log
streaming, hidden/runtime-managed Resource Manager behavior, and lifecycle
smoke coverage remain post-switch work.

## Remaining

- Real Docker API integration behind the runtime handler.
- Runtime discovery, concrete runtime-backed state projection, log streaming,
  endpoint projection, lifecycle smoke coverage, and hidden/runtime-managed
  Resource Manager behavior.
