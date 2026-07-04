# Configuration Store Built-in Provider

## Overview

- Resource type: `configuration.store`
- Provider id: `configuration`
- Purpose: declares a graph-backed configuration store service while keeping
  setting values out of persisted/exported graph state.

## Ported

- Configuration class/type defaults, endpoint attribute, and read-only entry-count summary attribute.
- Health and liveness declarations for the `/healthz` endpoint.
- Start, stop, and restart operations backed by a provider-local process controller that runs the existing service web app.
- Type-level runtime monitoring support, with Resource Manager process metric
  snapshots provided by the runtime bridge when the backing service is running.
- Provider-owned runtime setting seed options.
- Optional provider-owned runtime authentication options for external
  `Authentication:ServiceBearer` validation when a host wants the graph-backed
  service to accept tokens from a non-built-in identity provider.
- Inspect operation with a runtime-backed inspector that reports configured counts without exposing values.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Resource Manager Settings tab for managing provider-owned runtime settings.
  Setting values stay in provider runtime state and sidecar definition files, not
  Resource graph attributes.
- Manual `ResourceGraphBuilder.AddConfigurationStore(...)` builder
  for code-first resource and endpoint declaration, including create-only
  `seed.entries` attributes for development templates. Seeded
  settings materialize into provider-owned runtime state and are stripped from
  accepted graph state before normal template export.
- SettingsAndSecrets smoke coverage for endpoint projection, inspect execution, authorized setting reads, and API consumption through the graph-backed endpoint.
- ThirdPartyIdentity Docker smoke coverage for a Keycloak-protected
  graph-backed Configuration Store consumed by a graph-backed ASP.NET Core API.

## Example ResourceDefinition

This is the persisted/exported interchange shape for a graph-backed
Configuration Store resource. Create-only templates may additionally include
`seed.entries`; accepted graph state and default template export omit
those seeded values.

```json
{
  "name": "sample-app",
  "typeId": "configuration.store",
  "resourceId": "configuration.store:sample-app",
  "providerId": "configuration",
  "displayName": "Sample App Settings",
  "kind": "local",
  "endpoint": "http://localhost:5101"
}
```

Create-only seed example:

```json
{
  "name": "sample-app",
  "typeId": "configuration.store",
  "providerId": "configuration",
  "endpoint": "http://localhost:5101",
  "seed": {
    "entries": [
      {
        "name": "Sample--Message",
        "value": "Hello from template"
      }
    ]
  }
}
```

## Switch-over status

Ready to integrate for graph-declared configuration stores in the selected
samples. The graph path starts the backing service, projects endpoint/count,
supports inspect, monitoring, health/liveness, built-in authorization, and
external bearer validation for the Keycloak sample. Runtime entries can be
managed through Resource Manager when the UI host has access to the provider
runtime manager. Durable setting storage, log streaming, permission-protected
import/export, setting versioning, and full registration/update flows remain
outside the switch gate.

## Remaining

- Durable setting storage.
- Logs and richer diagnostics.
- Permission-protected setting import/export and versioning.
- Full UI registration/update flow.
