# Secrets Vault Built-in Provider

## Overview

- Resource type: `secrets.vault`
- Provider id: `secrets-vault`
- Purpose: declares a graph-backed Secrets Vault service while keeping secret
  values out of persisted/exported graph state by default.

## Ported

- Secrets Vault class/type defaults, endpoint attribute, and read-only secret-count summary attribute.
- Health and liveness declarations for the `/healthz` endpoint.
- Start, stop, and restart operations backed by a provider-local process controller that runs the existing service web app.
- Type-level runtime monitoring support, with Resource Manager process metric
  snapshots provided by the runtime bridge when the backing service is running.
- Provider-owned runtime secret seed options.
- Inspect operation with a runtime-backed inspector that reports configured counts without exposing values.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Resource Manager Secrets tab for managing provider-owned runtime secrets.
  Existing values are masked in the UI and preserved unless replaced; secret
  values stay in provider runtime state and sidecar definition files, not
  Resource graph attributes.
- Manual `ResourceGraphBuilder.AddSecretsVault(...)` builder for
  code-first resource and endpoint declaration, including create-only
  `seed.secrets` attributes for development templates. Seeded secrets
  materialize into provider-owned runtime state and are stripped from accepted
  graph state before normal template export.
- SettingsAndSecrets smoke coverage for endpoint projection, inspect execution, authorized secret reads, and API consumption through the graph-backed endpoint.

## Example ResourceDefinition

This is the persisted/exported interchange shape for a graph-backed Secrets
Vault resource. Create-only templates may additionally include
`seed.secrets`; accepted graph state and default template export omit those
secret values.

```json
{
  "name": "sample-app",
  "typeId": "secrets.vault",
  "resourceId": "secrets.vault:sample-app",
  "providerId": "secrets-vault",
  "displayName": "Sample App Secrets",
  "kind": "local",
  "endpoint": "http://localhost:5102"
}
```

Create-only seed example:

```json
{
  "name": "sample-app",
  "typeId": "secrets.vault",
  "providerId": "secrets-vault",
  "endpoint": "http://localhost:5102",
  "seed": {
    "secrets": [
      {
        "name": "Sample--ApiKey",
        "value": "local-development-secret",
        "version": "v1"
      }
    ]
  }
}
```

## Switch-over status

Ready to integrate for graph-declared Secrets Vault resources in the selected
samples. The graph path starts the backing service, projects endpoint/count,
supports inspect, monitoring, health/liveness, and authorized reads without
placing secret values in exported graph attributes. Runtime secrets can be managed
through Resource Manager when the UI host has access to the provider runtime
manager. Durable secret storage, log streaming, permission-protected
import/export, secret versioning, and full registration/update flows remain
outside the switch gate.

## Remaining

- Durable secret storage.
- Logs, permission-protected secret import/export, and versioning.
- Full UI registration/update flow.
