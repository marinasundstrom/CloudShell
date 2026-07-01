# Name Mapping Built-in Provider

## Overview

- Resource type: `cloudshell.nameMapping`
- Provider id: `cloudshell.dns`
- Purpose: declares a host/name mapping in the Resource Graph.

## Ported

- Network class/type defaults.
- Host, endpoint, and exposure attributes.
- Provider-managed unset materialization-status attribute.
- Passive name-mapping capability marker.
- Temporary `ResourceReference` DNS-zone and target dependencies.
- Typed wrapper, apply planning, graph validation, and Resource Manager bridge projection.
- Manual `ResourceGraphBuilder.AddNameMapping(...)` builder for
  lower-level code-first name mapping definition authoring, plus preferred
  DNS-zone-owned `dnsZone.AddNameMapping(...)` and `dnsZone.MapHost(...)`
  convenience APIs.
- LoadBalancer sample graph name-mapping coverage targeting the graph-backed
  load balancer frontend.
- Provider-owned graph DNS reconciler coverage that materializes graph name
  mappings through the existing local-hostnames publisher contract.

## Switch-over status

Ready as a supporting graph resource for the LoadBalancer graph-default sample
path. The switch scope covers declared host/name mappings, target references,
Resource Manager projection, and provider-owned graph DNS reconciliation
through the existing local-hostnames publisher contract. General publisher ownership,
conflict/materialization views, target endpoint validation, and UI flows remain
post-switch work.

## Remaining

- Provider-specific reference modeling if needed.
- Target endpoint validation.
- Conflict/materialization views as capability members or operation plans.
- DNS publisher integration and UI registration/update flow.
