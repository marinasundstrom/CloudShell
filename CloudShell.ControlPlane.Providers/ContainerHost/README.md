# Container Host Built-in Provider

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

## Switch-over status

Ready as a supporting graph resource for samples that need a generic container
host boundary. It is not a standalone switch target yet; current runtime
behavior is still supplied by sample/control-plane bridges and Docker-specific
providers. Host resolution, credentials, placement, diagnostics, and durable
runtime ownership remain post-switch seams.

## Remaining

- Real container host runtime integration.
- Host resolution, placement behavior, credentials, and runtime diagnostics.
