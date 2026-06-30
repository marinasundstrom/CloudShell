# Executable Application Built-in Provider

## Overview

- Resource type: `application.executable`
- Provider id: `applications.executable`
- Purpose: declares a local executable application resource in the Resource Graph.

## Ported

- Type and class defaults.
- Executable path validation and command configuration.
- Shared volume-consumer capability.
- Provider-declared default console log source.
- Programmatic builder activation for runtime monitoring.
- Start operation with an injected provider-owned process runtime controller
  that honors configured arguments and working directory.
- Typed wrapper plus Resource Manager bridge projection, execution, and
  process monitoring snapshots.
- Manual `ResourceDefinitionGraphBuilder.AddExecutableApplication(...)`
  builder for code-first executable definition authoring with command
  configuration, volume mount capability setup, and runtime monitoring
  capability activation.

## Switch-over status

Partially ready for integration where a sample needs a graph-declared local
executable. The current path proves start execution, command configuration,
Resource Manager projection, and process monitoring snapshots through a
provider-owned runtime seam. Stop/restart lifecycle parity, log read/stream
integration, endpoints, templates, and UI flows remain outside the first switch
gate.

## Remaining

- Control Plane log read/stream integration.
- Stop/restart lifecycle operations and state-sensitive action availability.
- Endpoints, templates, and UI registration/update flow.
