# Storage Reference Provider

## Overview

- Resource type: `cloudshell.storage`
- Provider id: `cloudshell.storage`
- Purpose: declares a storage resource in the Resource Graph.

## Ported

- Storage class/type defaults.
- Provider, medium, and location attributes.
- Passive storage-provider and mount-provider capability markers.
- Inspect operation with an injected provider-owned inspector seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.
- Manual `ResourceDefinitionGraphBuilder.AddStorage(...)` builder for
  code-first storage definition authoring and test setup.

## Switch-over status

Ready as a supporting graph resource for sample paths that need local storage
metadata and storage-backed volume declarations. The current switch gate covers
declaration, projection, inspect wiring, and volume/materialization behavior
performed by the runtime handler using graph state. Provider-backed storage
materialization, richer usage payloads, health, monitoring, and UI flows remain
deferred.

## Remaining

- Volume collection payloads.
- Runtime filesystem availability and volume counts as capability members or operation plans.
- Provider-backed storage materialization, health, monitoring, and UI registration/update flow.
