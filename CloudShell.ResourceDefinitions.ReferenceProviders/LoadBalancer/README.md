# Load Balancer Reference Provider

## Overview

- Resource type: `cloudshell.loadBalancer`
- Provider id: `cloudshell.load-balancer`
- Purpose: declares a load balancer resource in the Resource Graph.

## Ported

- Network class/type defaults.
- Provider and host attributes.
- Read-only count attributes.
- Passive networking capability markers.
- Temporary typed host/backend `ResourceReference` dependencies and backend-target graph validation.
- Apply configuration operation with an injected provider-owned applier seam.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Manual `ResourceDefinitionGraphBuilder.AddLoadBalancer(...)` builder for
  code-first load balancer definition authoring with typed host and backend
  dependencies.

## Remaining

- Route and entrypoint payloads.
- Provider-specific reference modeling if needed.
- Traefik/materialization runtime integration, endpoint mappings, and UI registration/update flow.
