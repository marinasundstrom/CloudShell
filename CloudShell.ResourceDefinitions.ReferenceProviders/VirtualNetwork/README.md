# Virtual Network Reference Provider

## Overview

- Resource type: `cloudshell.virtualNetwork`
- Provider id: `cloudshell.network`
- Purpose: declares a virtual network resource in the Resource Graph.

## Ported

- Network class/type defaults.
- Virtual, default, readiness, and provider attributes.
- Endpoint, endpoint-network-mapping, and endpoint-mapping payload attributes.
- Passive virtual-network and ingress capability markers.
- Type-specific reconcile endpoint mappings operation provider with a context-aware injected provider-owned reconciler seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.
- HostVirtualNetwork sample graph public-ingress mapping projection coverage.

## Remaining

- Observed mapping state as capability members or operation plans.
- Endpoint mapping provisioner integration and UI registration/update flow.
