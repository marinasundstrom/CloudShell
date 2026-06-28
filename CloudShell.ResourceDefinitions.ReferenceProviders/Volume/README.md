# CloudShell Volume Reference Provider

## Overview

- Resource type: `cloudshell.volume`
- Provider id: `cloudshell.storage`
- Purpose: declares a CloudShell storage-backed volume resource in the Resource Graph.

## Ported

- Storage class/type defaults.
- Provider, medium, location, subpath, access-mode, and persistence attributes.
- Passive storage-volume capability marker.
- Typed `ResourceReference` storage dependencies and storage-reference graph validation.
- Type-specific `storage.volume.provision` operation provider with an injected provider-owned provisioner seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.
- Manual `ResourceDefinitionGraphBuilder.AddCloudShellVolume(...)` builder for
  code-first storage-backed volume authoring, including typed storage
  dependencies for tests and deployment definitions.

## Switch-over status

Ready as a supporting graph resource for the current storage and SQL-backed
sample paths. Volume declarations can be authored and projected through the
Resource Manager bridge, and current sample coverage validates storage-backed
mount materialization through runtime handlers. Provider-owned durable volume
materialization, usage, health, monitoring, and UI flows remain outside the
initial switch gate.

## Remaining

- Runtime filesystem availability as capability members or operation plans.
- Provider-backed volume materialization, health, monitoring, and UI registration/update flow.
