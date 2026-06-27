# Name Mapping Reference Provider

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
- Manual `ResourceDefinitionGraphBuilder.AddNameMapping(...)` builder for
  code-first name mapping definition authoring with typed DNS-zone and target
  dependencies.
- LoadBalancer sample graph name-mapping coverage targeting the graph-backed
  load balancer frontend.

## Remaining

- Provider-specific reference modeling if needed.
- Target endpoint validation.
- Conflict/materialization views as capability members or operation plans.
- DNS publisher integration and UI registration/update flow.
