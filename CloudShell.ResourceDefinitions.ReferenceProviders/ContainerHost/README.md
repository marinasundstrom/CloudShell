# Container Host Reference Provider

## Overview

- Resource type: `cloudshell.container-host`
- Provider id: `container-host.reference`
- Purpose: declares a generic container host boundary that resource providers can reference without binding directly to Docker.

## Ported

- Infrastructure class/type defaults.
- Host kind, endpoint, registry, and default-host attributes.
- Passive container image/build/filesystem-mount capability markers.
- Inspect operation with an injected provider-owned inspector seam.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Manual `ResourceDefinitionGraphBuilder.AddContainerHost(...)` builder for
  code-first generic container-host definition authoring.

## Remaining

- Real container host runtime integration.
- Host resolution, placement behavior, credentials, and runtime diagnostics.
