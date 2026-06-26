# Resource Definition Structure

This document describes the common `ResourceDefinition` structure used by the
Resource Graph POC. It focuses on the interchange shape: the JSON/YAML/XML
friendly model used by deployments, templates, imports, exports, debug views,
and apply operations.

The Resource Graph still owns the durable graph/configuration state. The
Control Plane and Resource Manager own runtime state, operational records,
live endpoint observations, logs, traces, metrics, orchestration, and provider
runtime caches.

## Core Envelope

A `ResourceDefinition` describes a resource state snapshot or an incremental
change that can be validated and applied to the graph.

```json
{
  "name": "api",
  "typeId": "application.container-app",
  "resourceId": "application.container-app:api",
  "providerId": "applications.container-app",
  "displayName": "API",
  "version": "1",
  "dependsOn": [],
  "attributes": {},
  "configuration": {},
  "capabilities": {},
  "operations": {},
  "metadata": {}
}
```

Common fields:

| Field | Purpose |
| --- | --- |
| `name` | Scoped authored name. |
| `typeId` | Resource type, such as `application.container-app` or `docker.host`. |
| `resourceId` | Optional canonical resource ID. If omitted, the model derives one from type and name. |
| `providerId` | Optional default provider or implementation owner. |
| `displayName` | Optional presentation label. It does not affect addressing. |
| `version` | Optional resource revision/version string for graph state. |
| `dependsOn` | Optional startup dependency references. This is not general service discovery. |
| `attributes` | Resource-owned graph/configuration state, keyed by attribute ID. |
| `configuration` | Provider-owned structured configuration payloads when attributes are not the right shape. |
| `capabilities` | Capability declarations and capability-owned graph state. |
| `operations` | Operation declarations and operation-owned graph state. |
| `metadata` | Non-runtime metadata about the definition. |

## References

`ResourceReference` is the graph-native way to reference another resource. A
reference is not the same thing as a relationship. It may be used by
`dependsOn`, by resource attributes, or inside provider-owned complex values.

```json
{
  "value": "docker:graph-sample",
  "relationship": "dependsOn",
  "addressingMode": "resourceId",
  "typeId": "docker.host",
  "providerId": "docker"
}
```

Current relationship values:

| Value | Meaning |
| --- | --- |
| `dependsOn` | Startup ordering dependency. |
| `belongsTo` | Ownership or containment-style reference. |
| `reference` | General reference without stronger semantics. |

Current addressing modes:

| Value | Meaning |
| --- | --- |
| `resourceId` | Reference a resource already known by graph/resource ID. |
| `projectedResource` | Future-facing mode for provider-projected resources. |
| `providerNative` | Future-facing mode for provider-native identity. |

## Attributes

Attributes are graph/configuration state for a resource. Attribute IDs are
owned by the resource type, resource class, or a deliberately shared
definition. They can be scalar values, `ResourceReference` values, complex
objects, or collections when the attribute definition allows it.

```json
{
  "attributes": {
    "container.image": "cloudshell-application-api:20260622.2",
    "container.replicas": 3,
    "database.server": {
      "value": "application.sql-server:graph-sql",
      "relationship": "belongsTo",
      "addressingMode": "resourceId",
      "typeId": "application.sql-server"
    }
  }
}
```

Attribute definitions declare expected value type, default value, required
state, read-only/provider-managed mutability, collection shape, and optional
complex shape. A resource may still carry custom attributes without a
definition, but defined attributes are the portable contract for validation,
authoring, and provider integration.

## Endpoint Requests

Endpoint requests are graph configuration. They describe what endpoint a
resource is asking the runtime/provider to make available. They are not the
same as observed endpoint mappings or live addresses.

Common endpoint request shape:

```json
{
  "name": "http",
  "protocol": "http",
  "targetPort": 8080,
  "host": "localhost",
  "port": 5092,
  "ipAddress": null,
  "exposure": "Local",
  "assignment": null,
  "network": null,
  "providerEndpointId": null
}
```

Current providers declare endpoint requests as provider-owned attributes, for
example:

```json
{
  "attributes": {
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
  }
}
```

`project.endpointRequests` and `container.endpointRequests` currently use the
same shared `networking.endpointRequest` shape. This keeps the model flexible
without making endpoints a graph-native primitive.

## Endpoint Mappings

Endpoint mappings and endpoint network mappings are usually Resource Manager
runtime projections, not generic graph state. They describe configured or
observed reachability after providers and networks have acted on endpoint
requests.

Shared shapes exist for providers that need to declare mapping intent in graph
configuration, but they should be used deliberately:

```json
{
  "source": {
    "resource": {
      "value": "network:public",
      "relationship": "reference",
      "addressingMode": "resourceId",
      "typeId": "cloudshell.virtualNetwork"
    },
    "endpointName": "api-public"
  },
  "target": {
    "resource": {
      "value": "application.container-app:api",
      "relationship": "reference",
      "addressingMode": "resourceId",
      "typeId": "application.container-app"
    },
    "endpointName": "http"
  }
}
```

When a provider projects a concrete address, that belongs to the Resource
Manager projection unless the provider has an explicit graph configuration
attribute for it.

## Health Checks And Liveness

Health checks and liveness are declared as capability-owned graph payloads
under `health.checks`. The graph declares which checks exist. The Control
Plane evaluates probes, stores observations, and decides health/liveness
state.

```json
{
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
              "endpointName": "http",
              "timeoutMilliseconds": 1000
            }
          },
          "intervalSeconds": 10
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

The derived `liveness` capability may appear on resolved or Resource Manager
projections, but the persisted interchange input should focus on the
`health.checks` declaration unless a provider has a stronger reason to declare
something else.

## Volumes

Volume attachments are capability-owned graph payloads under
`storage.volumeConsumer`. The capability declares mount intent on the resource
that consumes volumes. Provider/runtime code decides how mounts are
materialized.

```json
{
  "capabilities": {
    "storage.volumeConsumer": {
      "mounts": [
        {
          "volume": "storage.volume:data",
          "targetPath": "/data",
          "readOnly": false
        }
      ]
    }
  }
}
```

The volume reference is currently stored as a string in the volume-consumer
payload and projected into graph dependencies by the capability dependency
provider. A future cleanup may move this to `ResourceReference` if provider
ports show that the current shape is too weak for interchange authoring.

## Log Sources

Log sources are declared as capability-owned graph payloads under
`logs.sources` when a provider can describe stable log sources. The graph
declares source metadata; read and stream sessions remain Control Plane
runtime concerns.

```json
{
  "capabilities": {
    "logs.sources": {
      "sources": [
        {
          "id": "console",
          "name": "Console logs",
          "kind": "processOutput",
          "format": "plainText",
          "capabilities": ["read", "stream"],
          "origin": "providerDefault",
          "purpose": "default",
          "availability": "resourceRunning"
        }
      ]
    }
  }
}
```

## Operations

Operations declare named behavior available for a resource. Their
implementation belongs to runtime integrations, operation providers, or
Control Plane handlers.

```json
{
  "operations": {
    "start": {},
    "restart": {},
    "container.image.update": {}
  }
}
```

Most current operation declarations come from `ResourceTypeDefinition` rather
than explicit resource definitions. Explicit operation payloads should be used
only when the operation needs resource-owned configuration or when a provider
has made that part of its interchange contract.

## Interchange Feedback

Provider README examples should use the actual `ResourceDefinition` shape.
When an example is hard to author, hard to read, or awkward to round-trip,
record that as feedback against the interchange API. Provider porting should
prefer the simplest shape that captures graph configuration without leaking
runtime implementation details into the document.
