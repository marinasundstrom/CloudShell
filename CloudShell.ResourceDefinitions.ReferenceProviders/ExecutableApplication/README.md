# Executable Application Reference Provider

## Overview

- Resource type: `application.executable`
- Provider id: `applications.executable`
- Purpose: declares a local executable application resource in the Resource Graph.

## Ported

- Type and class defaults.
- Executable path validation and configuration.
- Shared volume-consumer capability.
- Provider-declared default console log source.
- Start operation with an injected provider-owned process runtime controller.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Manual `ResourceDefinitionGraphBuilder.AddExecutableApplication(...)`
  builder for code-first executable definition authoring with command
  configuration and volume mount capability setup.

## Remaining

- Command-shape attributes such as arguments and working directory.
- Control Plane log read/stream integration.
- Endpoints, templates, and UI registration/update flow.
