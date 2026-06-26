# Docker Container Reference Provider

## Overview

- Resource type: `docker.container`
- Provider id: `docker`
- Purpose: declares a provider-projected Docker container artifact in the Resource Graph.

## Ported

- Container class/type defaults.
- Workload, image, registry, replica, and read-only endpoint-count attributes.
- Passive monitoring and log-source capability markers.
- Lifecycle operation projections.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.

## Remaining

- Real Docker API integration.
- Runtime discovery, container state, state-sensitive action availability, log streaming, endpoint projection, and hidden/runtime-managed Resource Manager behavior.
