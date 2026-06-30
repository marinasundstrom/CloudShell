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
- Orchestration descriptor projection to `ContainerHostDescriptor`, allowing
  graph-backed generic container-host resources to satisfy explicit or default
  runtime host resolution.
- Manual `ResourceGraphBuilder.AddContainerHost(...)` builder for
  code-first generic container-host definition authoring. The built-in
  `ResourceGraphBuilder.DefaultContainerHost()` helper authors the
  default local docker-compatible host resource.

## Switch-over status

Ready as a supporting graph resource for samples that need a generic container
host boundary. Graph-backed generic container-host resources now participate in
runtime host resolution through the orchestration descriptor provider. It is not
a standalone switch target yet; current runtime behavior is still supplied by
sample/control-plane bridges and Docker-specific providers. Credentials,
placement, diagnostics, and durable runtime ownership remain post-switch seams.

## Remaining

- Real container host runtime integration.
- Placement behavior, credentials, and runtime diagnostics.
