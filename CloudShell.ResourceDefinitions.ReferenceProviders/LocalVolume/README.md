# Local Volume Reference Provider

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

## Remaining

- Provider-backed storage materialization.
- Usage tracking, health, and monitoring.
