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

## Switch-over status

Not a switch target for the current POC. `cloudshell.service` remains a future
logical service-boundary shape and is not required by the selected samples.
Keep the provider available for graph/model experimentation, but do not use it
to block or justify the provider switch.

## Remaining

- A clear product/runtime use case. A `cloudshell.service` resource is a
  potential service boundary that may later map to an orchestration service,
  but it is not used by current samples.
- Port, endpoint, and health-check payloads.
- Provider-specific reference modeling if needed.
- Endpoint projection through Resource Manager, runtime routing/materialization,
  richer target eligibility validation, orchestration integration, and UI
  registration/update flow.
