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

## Remaining

- Volume collection payloads.
- Runtime filesystem availability and volume counts as capability members or operation plans.
- Provider-backed storage materialization, health, monitoring, and UI registration/update flow.
