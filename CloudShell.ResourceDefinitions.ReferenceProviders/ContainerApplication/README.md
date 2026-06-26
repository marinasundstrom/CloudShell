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
- Start, restart, and image-update operations.
- Typed wrapper plus Resource Manager bridge projection, endpoint projection, and execution.
- ContainerAppDeployment and ReplicatedContainerHealth sample-inspired graph coverage.

## Remaining

- Actual container host orchestration.
- Revisions, replica runtime state, monitoring, and UI operations.
