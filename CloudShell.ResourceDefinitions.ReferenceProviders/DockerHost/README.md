# Docker Host Reference Provider

## Overview

- Resource type: `docker.host`
- Provider id: `docker`
- Purpose: declares a Docker-specific container host boundary in the Resource Graph.

## Ported

- Infrastructure class/type defaults.
- Docker host kind, endpoint, registry, and default-host attributes.
- Passive container image/build/filesystem-mount capability markers.
- Inspect operation with an injected provider-owned inspector seam.
- Typed wrapper plus Resource Manager bridge projection and execution.

## Remaining

- Real Docker runtime integration.
- Discovery, health, logs, container child projections, credentials, and UI registration/update flow.
