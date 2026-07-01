# DNS Zone Built-in Provider

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
- Manual `ResourceGraphBuilder.AddDnsZone(...)` builder for
  code-first DNS zone definition authoring.
- `UseLocalHostNames()`, child `AddNameMapping(...)`, and `MapHost(...)`
  convenience builders. Name mappings remain explicit graph resources but are
  authored through the DNS zone when they are zone-owned entries.
- Opt-in `ResourceModelGraphDnsZoneNameMappingReconciler` that projects graph
  DNS zone and name-mapping resources to the Resource Manager name-publishing
  provider contract.
- Opt-in `CoreDnsZoneFilePublishingProvider` that writes CoreDNS zone files
  from Resource model name mappings for local virtual-network samples.
- LoadBalancer sample graph DNS zone coverage beside the legacy
  local-hostnames zone.
- LoadBalancer and HostVirtualNetwork coverage that delegates graph name
  mappings through the provider-owned graph DNS reconciler.

## Switch-over status

Ready as a supporting graph resource for the LoadBalancer and HostVirtualNetwork
graph-default sample paths. The switch scope covers DNS zone declaration,
graph name-mapping reconciliation through the provider-owned graph reconciler,
local-host and CoreDNS file publishing adapters, and Resource Manager
projection without old DNS zone records. Materialization views, conflict
diagnostics, and UI flows remain post-switch work.

## Remaining

- Record/conflict/materialization views as capability members or operation plans.
- DNS UI registration/update flow.
