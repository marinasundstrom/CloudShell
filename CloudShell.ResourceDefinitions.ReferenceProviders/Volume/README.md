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

## Remaining

- Runtime filesystem availability as capability members or operation plans.
- Provider-backed volume materialization, health, monitoring, and UI registration/update flow.
