# Host Configuration Source Reference Provider

## Overview

- Resource type: `configuration.host`
- Provider id: `host-configuration`
- Purpose: declares a host configuration source that can be inspected without placing host configuration values directly in the graph.

## Ported

- Configuration class/type defaults.
- Source and read-only entry-count attributes.
- Inspect operation with an injected provider-owned inspector seam.
- Typed wrapper plus Resource Manager bridge projection and execution.

## Remaining

- Runtime host configuration lookup.
- Entry-name payloads, authorization, templates, and UI registration/update flow.
