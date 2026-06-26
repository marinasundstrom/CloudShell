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
- Resource Manager bridge projection for state, endpoints, observability links, and process-output logs.
- ProjectReference and SettingsAndSecrets sample coverage for runtime startup, graph-to-graph calls, logs, metrics, traces, health, and ResourceDefinition apply/update.

## Remaining

- Launch settings parsing and richer process diagnostics.
- Reusable service-discovery/environment-variable conventions if more providers prove the need.
- First-class graph identity/provisioning projection if the POC proves it belongs in the graph model.
- Container build behavior and UI registration/update flow.
