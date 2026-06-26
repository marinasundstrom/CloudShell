# Container Application Reference Provider

## Overview

- Resource type: `application.container-app`
- Provider id: `applications.container-app`
- Purpose: declares a containerized application workload in the Resource Graph.

## Ported

- Image, registry, and replica attributes.
- Endpoint request attributes using the shared networking endpoint request shape.
- Optional typed generic/Docker container-host reference validation and projection.
- Shared volume-consumer capability.
- Start, stop, restart, and image-update operations with an injected
  provider-owned runtime handler seam.
- Typed wrapper plus Resource Manager bridge projection, endpoint projection, and execution.
- ContainerAppDeployment and ReplicatedContainerHealth sample-inspired graph coverage,
  including ContainerAppDeployment image/replica update delegation and
  ReplicatedContainerHealth smoke coverage where graph start, stop, restart,
  and image-update actions delegate to sample-local runtime adapters and stop
  verifies Docker runtime container cleanup.
- Manual `ResourceDefinitionGraphBuilder.AddContainerApplication(...)`
  builder for code-first container app definition authoring with typed host
  dependencies, endpoint requests, replicas, and volume mount capability setup.

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

The ReplicatedContainerHealth sample currently proves this seam with a
sample-local adapter that maps `application.container-app:graph-api` to the
existing `application:api` runtime resource. It covers start, stop, and
restart delegation, projects graph state from the runtime app through the
provider bridge so Resource Manager action availability can evaluate lifecycle
commands, and applies an accepted graph `container.image` change through the graph
`container.image.update` operation. That adapter is intentionally not a
reusable provider toolkit yet; it exists to validate the graph-to-runtime
boundary while the old application-provider runtime still owns replica
materialization. The Docker smoke verifies that graph restart recreates the
revision-scoped runtime containers and graph stop removes the containers that
graph start created.

The ContainerAppDeployment sample also wires this seam to a sample-local
adapter. It maps `application.container-app:graph-sample-api` to the existing
`application:sample-api` runtime resource so graph image and replica updates can
be applied through the existing Resource Manager deployment and replicas APIs
while the durable container runtime provider remains future work.

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
  "resourceId": "application.container-app:graph-api",
  "providerId": "applications.container-app",
  "displayName": "Graph Replicated API",
  "dependsOn": [
    {
      "value": "docker:graph-sample",
      "relationship": "dependsOn",
      "addressingMode": "resourceId",
      "typeId": "docker.host"
    }
  ],
  "attributes": {
    "container.image": "cloudshell-application-api:20260622.2",
    "container.registry": "docker.io",
    "container.replicas": 3,
    "container.endpointRequests": [
      {
        "name": "http",
        "protocol": "http",
        "targetPort": 8080,
        "host": "localhost",
        "port": 5092,
        "exposure": "Local"
      }
    ]
  },
  "capabilities": {
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
- Revisions, replica runtime state, monitoring, and UI operations.
