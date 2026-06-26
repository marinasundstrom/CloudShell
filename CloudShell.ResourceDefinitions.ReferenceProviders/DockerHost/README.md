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

## Example ResourceDefinition

This is the interchange shape for a graph-backed Docker host declaration. Other
resource definitions can reference it through `ResourceReference` values in
`dependsOn` or provider-owned attributes.

```json
{
  "name": "graph-sample",
  "typeId": "docker.host",
  "resourceId": "docker:graph-sample",
  "providerId": "docker",
  "displayName": "Graph Docker Host",
  "attributes": {
    "docker.host.kind": "local",
    "docker.host.endpoint": "unix:///var/run/docker.sock",
    "container.registry": "docker.io",
    "docker.host.default": true
  }
}
```

## Remaining

- Real Docker runtime integration.
- Discovery, health, logs, container child projections, credentials, and UI registration/update flow.
