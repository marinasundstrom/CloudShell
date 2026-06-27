# Load Balancer Reference Provider

## Overview

- Resource type: `cloudshell.loadBalancer`
- Provider id: `cloudshell.load-balancer`
- Purpose: declares a load balancer resource in the Resource Graph.

## Ported

- Network class/type defaults.
- Provider and host attributes.
- Read-only count attributes.
- Entrypoint and route payload attributes with provider-owned complex shapes.
- Passive networking capability markers.
- Temporary typed host/backend `ResourceReference` dependencies and backend-target graph validation.
- Apply configuration operation with an injected provider-owned applier seam.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Manual `ResourceDefinitionGraphBuilder.AddLoadBalancer(...)` builder for
  code-first load balancer definition authoring with typed host dependencies,
  backend dependencies, entrypoints, host/path HTTP routes, and TCP routes.
- LoadBalancer sample coverage that executes the graph load balancer action
  through Resource Manager and writes Traefik dynamic configuration from the
  graph-declared routes.

## Remaining

- Provider-specific reference modeling if needed.
- Runtime container materialization, endpoint mappings, and UI registration/update flow.

## Resource Definition Example

```json
{
  "name": "graph-public",
  "type": "cloudshell.loadBalancer",
  "resourceId": "load-balancer:graph-public",
  "provider": "cloudshell.load-balancer",
  "attributes": {
    "loadBalancer.provider": "traefik",
    "loadBalancer.hostResourceId": "docker:graph-sample-host",
    "loadBalancer.entrypointDefinitions": [
      {
        "name": "http",
        "protocol": "Http",
        "port": 80,
        "exposure": "Public"
      },
      {
        "name": "tcp-5432",
        "protocol": "Tcp",
        "port": 5432,
        "exposure": "Public"
      }
    ],
    "loadBalancer.routeDefinitions": [
      {
        "id": "load-balancer:graph-public:route:app.cloudshell.local:application.container-app:graph-web:80",
        "name": "app.cloudshell.local to application.container-app:graph-web:80",
        "kind": "Http",
        "entrypointName": "http",
        "match": {
          "host": "app.cloudshell.local"
        },
        "target": {
          "resource": {
            "value": "application.container-app:graph-web",
            "relationship": "reference",
            "addressingMode": "resourceId"
          },
          "port": 80
        }
      }
    ]
  },
  "dependsOn": [
    {
      "value": "docker:graph-sample-host",
      "relationship": "dependsOn",
      "addressingMode": "resourceId",
      "typeId": "docker.host"
    },
    {
      "value": "application.container-app:graph-web",
      "relationship": "dependsOn",
      "addressingMode": "resourceId"
    }
  ]
}
```
