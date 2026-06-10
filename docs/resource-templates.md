# Resource Templates

Resource templates export and import an entire resource group without moving
provider-owned runtime configuration into the CloudShell database.

CloudShell owns the group-level template envelope, dependency ordering, and
import/export orchestration. Providers decide whether they support templates for
their resource types, and providers own the schema and validation of their
configuration payloads.

## Template Shape

A resource group template uses a common envelope:

```json
{
  "templateVersion": "1.0",
  "kind": "resourceGroup",
  "name": "Local Development",
  "description": "Frontend, API, and supporting services",
  "resources": [
    {
      "name": "Example Web API",
      "providerId": "applications",
      "resourceType": "application.aspnet-core-project",
      "dependsOn": [],
      "providerConfigurationVersion": "1.0",
      "resourceId": "application:example-web-api",
      "configuration": {
        "executablePath": "dotnet",
        "arguments": "watch --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj run --no-launch-profile",
        "workingDirectory": null,
        "endpoint": null,
        "environmentVariables": [],
        "lifetime": "Detached",
        "references": [],
        "useServiceDiscovery": false,
        "endpointPorts": [
          {
            "name": "http",
            "targetPort": 80,
            "port": null,
            "protocol": "http",
            "exposure": "Local"
          }
        ]
      }
    }
  ]
}
```

The `configuration` object is provider-owned JSON. CloudShell carries it in the
template document, but does not interpret the provider-specific fields.

The application provider preserves specific application-backed resource types,
including `application.executable`, `application.aspnet-core-project`,
`application.container-app`, and `application.sql-server`.

Application provider templates also preserve app settings and environment
variables, including literal values, configuration-entry references, and
Secrets Vault references. Reference-backed entries keep the referenced store or
vault resource ID, entry or secret name, and optional version. Secret values are
not embedded in application templates; only the `SecretReference` is preserved.
Templates should include the referenced configuration store or Secrets Vault
resources, or the imported application should keep dependencies pointing to
equivalent resources in the target environment.

`name` is the friendly display name. `resourceId` is the stable resource
identifier used by registrations, links, logs, configuration endpoints, and
authorization. New exports include `resourceId`; older templates without it are
still accepted and providers allocate a unique ID from the friendly name.
Explicit `resourceId` values must not already exist in the target CloudShell
instance.

Configuration service templates include non-secret entry values. Secret entries
are exported as empty placeholders so templates do not leak secrets by default.
Secrets Vault templates export secret names and empty placeholders only; secret
material stays provider-owned and must be supplied after import.

## Provider Contract

Providers opt in by implementing `IResourceTemplateProvider`.

- `CanExport` determines whether a current resource can be written into a
  template.
- `ExportAsync` returns a `ResourceTemplateDefinition` with a provider-owned
  configuration payload.
- `CanImport` determines whether a resource template entry is supported.
- `ImportAsync` creates the provider-owned configuration and registers the
  imported resource in the target group.

Unsupported resources are skipped and reported as diagnostics. This lets a
group template preserve every supported resource without requiring all providers
to implement import/export at the same time.

Template envelope validation is also reported through import diagnostics. An
unsupported `kind` or `templateVersion` does not create a resource group and
does not throw from the domain import API.

## UI

Open `/resources/templates` or use the **Templates** button on `/resources`.

The page can:

- Export a selected resource group to formatted JSON.
- Paste a resource group template and import it as a new group.
- Show warnings and errors for unsupported providers, invalid payloads, or
  dependency-order fallbacks.

Import creates a new resource group only after the template envelope is valid.
Providers create new resource definitions using their own storage and
registration flow.

Resource dependencies are treated as resource communication boundaries by
default. In the built-in application forms, dependency candidates are limited to
the selected resource group; resources in the default group only see other
default-group resources.

## Persistence Boundary

Templates do not add a resource configuration column to the core database.

The core database remains responsible for platform metadata:

- Explicitly registered root resources.
- Resource group definitions.
- Resource-to-group assignments.

Provider configuration remains provider-owned. For example, the application
provider continues to store executable application definitions in
`CloudShell.Host/Data/application-resources.json`.

The configuration provider stores configuration service definitions in
`CloudShell.Host/Data/configuration-stores.json`. Secrets Vault definitions are
stored separately in `CloudShell.Host/Data/secrets-vaults.json`.
