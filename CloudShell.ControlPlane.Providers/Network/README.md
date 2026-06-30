# Network Built-in Provider

## Overview

- Resource type: `cloudshell.network`
- Provider id: `cloudshell.network`
- Purpose: declares a generic network resource in the Resource Graph.

## Ported

- Network class/type defaults.
- Kind, readiness, and provider attributes.
- Passive networking capability markers.
- Reconcile endpoint mappings operation with a context-aware injected provider-owned reconciler seam.
- Opt-in `ResourceModelGraphEndpointMappingReconciler` that projects graph
  resources to the Resource Manager endpoint-mapping provisioner contract and
  can serve network, virtual-network, and host-network reconcile operations.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Manual `ResourceGraphBuilder.AddNetwork(...)` builder for
  code-first `ResourceDefinition` and deployment authoring.

## Switch-over status

Ready as a generic graph modeling type, but not a direct switch target for the
current samples. Use specialized VirtualNetwork/HostNetworking paths when a
sample needs endpoint-mapping behavior. Endpoint payloads, observed mapping
state, specialization boundaries, and UI flows remain deferred.

## Remaining

- Endpoint and mapping payloads.
- Observed mapping state as capability members.
- Host/virtual network specialization and UI registration/update flow.
