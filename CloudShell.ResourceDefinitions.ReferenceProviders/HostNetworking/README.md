# Host Networking Reference Providers

## Overview

- Resource types: `cloudshell.hostNetworking.local`, `cloudshell.hostNetworking.macos`
- Provider id: `cloudshell.hostNetworking`
- Purpose: declares host-networking provider resources that can reconcile endpoint mappings for local and macOS host networking.

## Ported

- Infrastructure class/type defaults.
- Host-readiness, OS, and mode attributes.
- Passive networking provider, endpoint mapper, gateway, ingress, and host-network capability markers.
- Type-specific reconcile endpoint mappings operation providers with context-aware injected provider-owned reconciler seams.
- Typed wrappers plus apply planning and Resource Manager bridge projection/execution.

## Remaining

- Platform support checks.
- Live mapping counts, host proxy runtime state, endpoint mapping provisioner integration, macOS-specific runtime isolation, diagnostics, and UI registration/update flow.
