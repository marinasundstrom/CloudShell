# ASP.NET Core Project Reference Provider

## Overview

- Resource type: `application.aspnet-core-project`
- Provider id: `applications.aspnet-core-project`
- Purpose: declares a local ASP.NET Core project resource in the Resource Graph and projects it into Resource Manager as a project resource.

## Ported

- Project path, arguments, hot reload, launch-settings, endpoint request, environment variable, service-discovery name, and graph reference attributes.
- Shared volume-consumer capability and default console log-source declaration.
- Start, stop, and restart operations backed by the provider-local process runtime controller.
- Service-discovery environment projection from explicit `project.references`, including project, configuration store, Secrets Vault, and SQL Server graph resources.
- CloudShell Configuration Store and Secrets Vault client environment projection from explicit graph references, using the same name/id alias conventions as the existing configuration providers.
- Resource Manager bridge projection for state, endpoints, observability links, and process-output logs.
- ProjectReference and SettingsAndSecrets sample coverage for runtime startup, graph-to-graph calls, logs, metrics, traces, health, and ResourceDefinition apply/update.
- ApplicationTopology-inspired graph coverage for explicit SQL Server, Configuration Store, and Secrets Vault service-discovery references without using `DependsOn` for discovery.
- ApplicationTopology graph API identity declaration and SQL read/write grant setup through Resource Manager declarations.

## Remaining

- Launch settings parsing and richer process diagnostics.
- Reusable service-discovery/environment-variable conventions if more providers prove the need.
- Graph SQL credential/grant reconciliation. ASP.NET Core graph resources can
  declare and consume a graph SQL Server service-discovery target, but the
  current brokered credential path still depends on the old SQL Server
  provider runtime model.
- First-class graph identity/provisioning projection if the POC proves it belongs in the graph model.
- Container build behavior and UI registration/update flow.
