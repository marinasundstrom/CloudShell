# Configuration Store Built-in Provider

## Overview

- Resource type: `configuration.store`
- Provider id: `configuration`
- Purpose: declares a graph-backed configuration store service without storing configuration entry values as ordinary graph attributes.

## Ported

- Configuration class/type defaults, endpoint attribute, and read-only entry-count summary attribute.
- Health and liveness declarations for the `/healthz` endpoint.
- Start, stop, and restart operations backed by a provider-local process controller that runs the existing service web app.
- Type-level runtime monitoring support, with Resource Manager process metric
  snapshots provided by the runtime bridge when the backing service is running.
- Provider-owned runtime entry seed options.
- Optional provider-owned runtime authentication options for external
  `Authentication:ServiceBearer` validation when a host wants the graph-backed
  service to accept tokens from a non-built-in identity provider.
- Inspect operation with a runtime-backed inspector that reports configured counts without exposing values.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Manual `ResourceGraphBuilder.AddConfigurationStore(...)` builder
  for code-first resource and endpoint declaration. Entry values remain
  provider/runtime data and are not authored as graph attributes.
- SettingsAndSecrets smoke coverage for endpoint projection, inspect execution, authorized entry reads, and API consumption through the graph-backed endpoint.
- ThirdPartyIdentity Docker smoke coverage for a Keycloak-protected
  graph-backed Configuration Store consumed by a graph-backed ASP.NET Core API.

## Example ResourceDefinition

This is the interchange shape for a graph-backed Configuration Store resource.
The graph declares the service boundary and endpoint; configuration entry values
are provider/runtime data and are not authored as ordinary graph attributes.

```json
{
  "name": "sample-app",
  "typeId": "configuration.store",
  "resourceId": "configuration.store:sample-app",
  "providerId": "configuration",
  "displayName": "Sample App Settings",
  "attributes": {
    "configuration.kind": "local",
    "configuration.endpoint": "http://localhost:5101"
  }
}
```

## Switch-over status

Ready to integrate for graph-declared configuration stores in the selected
samples. The graph path starts the backing service, projects endpoint/count,
supports inspect, monitoring, health/liveness, built-in authorization, and
external bearer validation for the Keycloak sample. Durable entry storage,
log streaming, templates, and editable UI remain outside the switch gate.

## Remaining

- Durable entry storage.
- Logs and richer diagnostics.
- Templates and UI registration/update flow.
