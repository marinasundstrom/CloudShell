# Secrets Vault Reference Provider

## Overview

- Resource type: `secrets.vault`
- Provider id: `secrets-vault`
- Purpose: declares a graph-backed Secrets Vault service without storing secret values in the Resource Graph.

## Ported

- Secrets Vault class/type defaults, endpoint attribute, and read-only secret-count summary attribute.
- Health and liveness declarations for the `/healthz` endpoint.
- Start, stop, and restart operations backed by a provider-local process controller that runs the existing service web app.
- Runtime monitoring capability activation through the programmatic builder,
  with Resource Manager process metric snapshots provided by the runtime
  bridge when the backing service is running.
- Provider-owned runtime secret seed options.
- Inspect operation with a runtime-backed inspector that reports configured counts without exposing values.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Manual `ResourceDefinitionGraphBuilder.AddSecretsVault(...)` builder for
  code-first resource and endpoint declaration. Secret values remain
  provider/runtime data and are not authored as graph attributes.
- SettingsAndSecrets smoke coverage for endpoint projection, inspect execution, authorized secret reads, and API consumption through the graph-backed endpoint.

## Example ResourceDefinition

This is the interchange shape for a graph-backed Secrets Vault resource. The
graph declares the service boundary and endpoint; secret values are
provider/runtime data and must not be stored as ordinary graph attributes.

```json
{
  "name": "sample-app",
  "typeId": "secrets.vault",
  "resourceId": "secrets.vault:sample-app",
  "providerId": "secrets-vault",
  "displayName": "Sample App Secrets",
  "attributes": {
    "secrets.kind": "local",
    "secrets.endpoint": "http://localhost:5102"
  },
  "capabilities": {
    "monitoring": {}
  }
}
```

## Switch-over status

Ready to integrate for graph-declared Secrets Vault resources in the selected
samples. The graph path starts the backing service, projects endpoint/count,
supports inspect, monitoring, health/liveness, and authorized reads without
placing secret values in graph attributes. Durable secret storage, log
streaming, templates, and editable UI remain outside the switch gate.

## Remaining

- Durable secret storage.
- Logs, templates, and UI registration/update flow.
