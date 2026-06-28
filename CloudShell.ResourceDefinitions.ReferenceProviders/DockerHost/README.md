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
- Manual `ResourceDefinitionGraphBuilder.AddDockerHost(...)` builder for
  code-first Docker host definition authoring.

## Example ResourceDefinition

This is the interchange shape for a graph-backed Docker host declaration. Other
resource definitions can reference it through `ResourceReference` values in
`dependsOn` or provider-owned attributes. The example is also a check that the
interchange API remains readable for deployment, template, import, and export
flows rather than only being convenient for provider implementation code.

```json
{
  "name": "sample",
  "typeId": "docker.host",
  "resourceId": "docker.host:sample",
  "providerId": "docker",
  "displayName": "Docker Host",
  "attributes": {
    "docker.host.kind": "local",
    "docker.host.endpoint": "unix:///var/run/docker.sock",
    "container.registry": "docker.io",
    "docker.host.default": true
  }
}
```

## Switch-over status

Ready as a supporting graph resource for Docker-backed sample declarations.
The current switch scope covers graph declaration, Resource Manager projection,
inspect operation shape, and use as a target reference for runtime handlers.
Real Docker runtime integration, child container discovery, credentials,
health, logs, and UI flows remain outside the initial switch gate.

## Remaining

- Real Docker runtime integration.
- Discovery, health, logs, container child projections, credentials, and UI registration/update flow.
