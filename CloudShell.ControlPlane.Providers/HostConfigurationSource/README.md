# Host Configuration Source Built-in Provider

## Overview

- Resource type: `configuration.host`
- Provider id: `host-configuration`
- Purpose: declares a host configuration source that can be inspected without placing host configuration values directly in the graph.

## Ported

- Configuration class/type defaults.
- Source and read-only entry-count attributes.
- Inspect operation with an injected provider-owned inspector seam.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Manual `ResourceDefinitionGraphBuilder.AddHostConfigurationSource(...)`
  builder for code-first host configuration source definition authoring.
- ApplicationTopology sample coverage declares
  `configuration.host:application-topology-host-settings` as host
  configuration source metadata. The sample verifies Resource Manager
  projection, `configuration.kind`, `configuration.source`, provider-managed
  `configuration.entries.count`, and the `configuration.host.inspect` action
  shape without exposing host configuration values in graph state.

## Switch-over status

Ready as a supporting Resource model provider in ApplicationTopology. The switch gate is
limited to safe metadata projection and inspect action shape; runtime lookup of
host configuration values, entry-name payloads, authorization, and UI flows are
deferred because the graph must not become a host-configuration value store.

## Remaining

- Runtime host configuration lookup.
- Entry-name payloads, authorization, templates, and UI registration/update flow.
