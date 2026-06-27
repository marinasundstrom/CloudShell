# DNS Zone Reference Provider

## Overview

- Resource type: `cloudshell.dnsZone`
- Provider id: `cloudshell.dns`
- Purpose: declares a DNS zone in the Resource Graph.

## Ported

- Network class/type defaults.
- Zone and provider attributes.
- Passive DNS-zone capability marker.
- Reconcile name mappings operation with a context-aware injected reconciler seam.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Manual `ResourceDefinitionGraphBuilder.AddDnsZone(...)` builder for
  code-first DNS zone definition authoring.
- LoadBalancer sample graph DNS zone coverage beside the legacy
  local-hostnames zone.
- LoadBalancer sample-local runtime bridge coverage that delegates graph name
  mappings to the existing Resource Manager name-publishing provider contract.

## Remaining

- Generalized name-mapping child resource runtime integration outside the
  sample bridge.
- Record/conflict/materialization views as capability members or operation plans.
- DNS publisher integration and UI registration/update flow.
