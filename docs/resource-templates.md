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
      "resourceType": "application.executable",
      "dependsOn": [],
      "providerConfigurationVersion": "1.0",
      "configuration": {
        "executablePath": "dotnet",
        "arguments": "run --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --no-launch-profile",
        "workingDirectory": null,
        "endpoint": "http://localhost:5127",
        "environmentVariables": [],
        "lifetime": "Detached"
      }
    }
  ]
}
```

The `configuration` object is provider-owned JSON. CloudShell carries it in the
template document, but does not interpret the provider-specific fields.

Configuration service templates include non-secret entry values. Secret entries
are exported as empty placeholders so templates do not leak secrets by default.

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

## UI

Open `/resources/templates` or use the **Templates** button on `/resources`.

The page can:

- Export a selected resource group to formatted JSON.
- Paste a resource group template and import it as a new group.
- Show warnings and errors for unsupported providers, invalid payloads, or
  dependency-order fallbacks.

Import creates a new resource group. Providers create new resource definitions
using their own storage and registration flow.

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
`CloudShell.Host/Data/configuration-stores.json`.
