# ASP.NET Core Project Built-in Provider

## Overview

- Resource type: `application.aspnet-core-project`
- Provider id: `applications.aspnet-core-project`
- Purpose: declares a local ASP.NET Core project resource in the Resource model and projects it into Resource Manager as a project resource.

## Ported

- Project path, arguments, hot reload, launch-settings, endpoint request, environment variable, service-discovery name, and graph reference attributes.
- Type-level endpoint-source expectation, with programmatic builder activation
  for runtime monitoring and default console log-source capabilities on graph
  ASP.NET Core project resources.
- Shared volume-consumer capability for resources that declare volume mounts.
- Start, stop, and restart operations backed by the provider-local process runtime controller.
- Process startup parity with the old ASP.NET Core application provider for
  non-interactive `dotnet watch`, `DOTNET_WATCH_RESTART_ON_RUDE_EDIT`,
  build-before-run when hot reload is disabled, and `dotnet run --no-build`.
- Service-discovery environment projection from explicit `project.references`, including project, configuration store, Secrets Vault, and SQL Server graph resources.
- CloudShell Configuration Store and Secrets Vault client environment projection from explicit graph references, using the same name/id alias conventions as the existing configuration providers.
- Resource Manager bridge projection for state, endpoints, observability links,
  process-output logs, and process monitoring snapshots.
- Resource Manager details use generated Resource Manager views for Resource model bridge resources so they do not fall into old application-provider tabs that require old provider records.
- Resource Manager UI metadata registration for Resource model samples,
  including display name, icon, endpoint descriptor, and health/liveness probe
  defaults without registering old application-provider stores or update forms.
- ProjectReference and SettingsAndSecrets sample coverage for runtime startup, Resource model service-to-service calls, logs, metrics, traces, health, and ResourceDefinition apply/update.
- ApplicationTopology-inspired graph coverage for explicit SQL Server, Configuration Store, and Secrets Vault service-discovery references without using `DependsOn` for discovery.
- ApplicationTopology graph API identity declaration and SQL read/write grant setup through Resource Manager declarations.
- ApplicationTopology graph API `/database` coverage through a sample-local
  graph SQL credential endpoint.
- ApplicationTopology Docker-backed graph frontend `/upstream` coverage through
  the graph API, graph settings, and graph SQL credential flow.
- Manual `ResourceDefinitionGraphBuilder.AddAspNetCoreProject(...)` builder
  for code-first project definition authoring with endpoint requests,
  environment variables, service-discovery references, volume mounts, and
  health-check capability payloads. Environment variables are authored as a
  keyed map and may use literal values, configuration-entry references, or
  secret references; the Resource Manager bridge resolves references when the
  project resource starts.

## Example ResourceDefinition

This is the interchange shape for an ASP.NET Core project resource.
`project.references` is the service-discovery input; `dependsOn` remains an
explicit startup-order hint and should not be used as the discovery mechanism.

```json
{
  "name": "api",
  "typeId": "application.aspnet-core-project",
  "resourceId": "application.aspnet-core-project:api",
  "providerId": "applications.aspnet-core-project",
  "displayName": "API",
  "attributes": {
    "project.path": "./Api/CloudShell.Sample.Api.csproj",
    "project.hotReload": false,
    "project.useLaunchSettings": false,
    "project.serviceDiscoveryName": "api",
    "project.endpointRequests": [
      {
        "name": "http",
        "protocol": "http",
        "targetPort": 8080,
        "host": "localhost",
        "port": 5092,
        "exposure": "Local"
      }
    ],
    "project.environmentVariables": {
      "ASPNETCORE_ENVIRONMENT": {
        "value": "Development"
      },
      "SAMPLE_MESSAGE": {
        "configurationEntryRef": {
          "storeResourceId": "configuration.store:sample-app",
          "name": "Sample:Message"
        }
      },
      "SERVICE_APIKEY": {
        "secretRef": {
          "vaultResourceId": "secrets.vault:sample-app",
          "name": "application-topology:api-key"
        }
      }
    },
    "project.references": [
      {
        "value": "configuration.store:sample-app",
        "relationship": "reference",
        "addressingMode": "resourceId",
        "typeId": "configuration.store",
        "providerId": "configuration"
      },
      {
        "value": "secrets.vault:sample-app",
        "relationship": "reference",
        "addressingMode": "resourceId",
        "typeId": "secrets.vault",
        "providerId": "secrets-vault"
      }
    ]
  },
  "capabilities": {
    "monitoring": {}
  }
}
```

## Switch-over status

Ready to integrate for graph-declared ASP.NET Core project resources in the
selected samples. The graph path starts, stops, restarts, projects endpoints,
projects logs/monitoring/observability, and resolves explicit graph
`project.references` without old application-provider records. Full editable
registration/update UI parity is not required before switching; it remains a
documented post-switch cleanup item.

## Remaining

- Launch settings parsing and richer process diagnostics.
- Reusable service-discovery/environment-variable conventions if more providers prove the need.
- Reusable graph SQL credential/grant integration outside the
  ApplicationTopology sample-local endpoint.
- Graph-backed environment-variable configuration provider support for
  editable Resource Manager Environment tab projection.
- First-class graph identity/provisioning projection if the POC proves it belongs in the graph model.
- Container build behavior and editable UI registration/update flow.
