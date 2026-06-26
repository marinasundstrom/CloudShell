# Configuration Store Reference Provider

## Overview

- Resource type: `configuration.store`
- Provider id: `configuration`
- Purpose: declares a graph-backed configuration store service without storing configuration entry values as ordinary graph attributes.

## Ported

- Configuration class/type defaults, endpoint attribute, and read-only entry-count summary attribute.
- Health and liveness declarations for the `/healthz` endpoint.
- Start, stop, and restart operations backed by a provider-local process controller that runs the existing service web app.
- Provider-owned runtime entry seed options.
- Inspect operation with a runtime-backed inspector that reports configured counts without exposing values.
- Typed wrapper plus Resource Manager bridge projection and execution.
- SettingsAndSecrets smoke coverage for endpoint projection, inspect execution, authorized entry reads, and API consumption through the graph-backed endpoint.

## Remaining

- Durable entry storage.
- Logs and richer diagnostics.
- Templates and UI registration/update flow.
