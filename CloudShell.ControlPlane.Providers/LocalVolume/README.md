# Local Volume Built-in Provider

## Overview

- Resource type: `storage.volume`
- Provider id: `storage.localVolume`
- Purpose: declares a simple local volume resource in the Resource Graph.

## Ported

- Storage class/type defaults.
- Medium validation.
- Provision operation with an injected provider-owned provisioner seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection.
- Manual `ResourceDefinitionGraphBuilder.AddLocalVolume(...)` builder for
  code-first local volume definition authoring.

## Switch-over status

Ready only as a simple graph/storage modeling support type. It is not a current
sample switch root; the main storage-backed sample paths use
`cloudshell.volume` with runtime handler materialization. Provider-backed local
volume materialization, usage, health, and monitoring remain deferred.

## Remaining

- Provider-backed storage materialization.
- Usage tracking, health, and monitoring.
