# Service Reference Provider

## Overview

- Resource type: `cloudshell.service`
- Provider id: `cloudshell.service`
- Purpose: declares a logical service boundary in the Resource Graph.

## Ported

- Service class/type defaults.
- Service kind and routing-mode attributes.
- Passive endpoint-source capability marker.
- Temporary typed target/network `ResourceReference` dependencies and target/network graph validation.
- Reconcile operation with an injected provider-owned reconciler seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.
- Manual `ResourceDefinitionGraphBuilder.AddService(...)` builder for
  code-first service definition authoring with typed target and network
  dependencies.

## Remaining

- Port, endpoint, and health-check payloads.
- Provider-specific reference modeling if needed.
- Endpoint projection through Resource Manager, richer target eligibility validation, orchestration integration, and UI registration/update flow.
