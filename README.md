# CloudShell

> **Disclaimer:** Project is in an early phase. This is not a committed product.

CloudShell is an extensible, self-hosted cloud-portal shell for local development and on-premise environments. It uses Blazor, Fluent UI, and .NET 11 preview, with an operational experience inspired by the .NET Aspire Dashboard.

The goal is to make it possible to build your own cloud-platform shell: a place where teams can register resources, group them by project, inspect endpoints and state, and let extensions add focused operational tools. Control-plane services are separate versioned services; shell integrations connect the WebUI to those services.

<a href="images/resources.png"><img src="images/resources.png" style="max-height: 300px" /></a>

## Why CloudShell?

Modern software is increasingly built as distributed applications consisting of
multiple services, databases, APIs, containers, and infrastructure resources.
CloudShell provides a resource-oriented model for describing, managing, and
operating those systems.

CloudShell is designed to feel familiar to developers who use .NET Aspire while
also introducing concepts commonly found in cloud platforms such as networks,
endpoints, storage, identities, deployments, and operational workflows.

The goal is to make cloud-inspired architecture approachable without requiring a
public cloud account. Developers can experiment locally, share environments
with a team, and gradually evolve toward self-hosted or cloud-connected
infrastructure using the same resource model.

## Features

This repository is an early shell prototype. It currently includes:

- A Blazor shell with Fluent UI styling and Aspire-like density.
- Extension registration through the .NET service container.
- A Resource Manager surface with resource groups, nested resources, endpoints, state, and details.
- Programmatic Control Plane resource declarations through checked-in `Resources` code.
- Resource-bound actions for standard lifecycle commands and provider-specific commands.
- Resource group templates for provider-owned import/export of grouped resources.
- Configuration service resources for sharing settings and secrets between dependent resources.
- A Logs section where providers and extensions can expose resource or artifact logs.
- Aspire-compatible resource observability metadata and OTLP environment injection for local executable, ASP.NET Core project, and container resources.
- Resource type registration, where extensions provide the UI used to add resources.
- EF Core persistence for explicitly registered root resources and resource groups.
- Configurable ASP.NET Core Identity, dashboard-secret, OIDC, or external-scheme authentication.
- Role, permission, resource-group, and resource-scoped authorization.
- Host UI localization with a persisted language picker.
- SQLite or SQL Server persistence selected through configuration.
- A Docker reference extension that registers a local Docker Engine resource and shows containers as sub-resources.
- An executable application extension for local dev services, with configurable process lifetime and environment variables.

## Core Concepts

### Resources

Resources represent things CloudShell can manage, such as applications,
containers, databases, networks, storage, identities, configuration services,
and infrastructure components.

### Resource Providers

Providers connect CloudShell resources to underlying implementations such as
local processes, Docker, networking systems, or external platforms.

### Resource Groups

Resource groups organize related resources into project boundaries for
management, filtering, and authorization.

CloudShell uses the same resource model through code, the Resource Manager UI,
and the Control Plane API.

## Example

CloudShell resources can be declared programmatically:

```csharp
resources
    .AddAspNetCoreProject(...)
    .AddContainerApplication(...)
    .AddVirtualNetwork(...);
```

Applications, infrastructure, networking, and operational capabilities are all
represented through the same resource model.

## Projects

- `CloudShell.Hosting`: Razor class library for the Blazor shell, layout, static assets, built-in Resource Manager, Extensions, and Observability views.
- `CloudShell.Host`: development sample host that wires CloudShell UI, Control Plane, and local provider extensions together.
- `CloudShell.ControlPlane`: control-plane services, authorization adapters, resource/log stores, and the versioned OpenAPI endpoint module.
- `CloudShell.Abstractions`: extension SDK, shell contributions, and resource contracts.
- `CloudShell.Client`: shared SDK client credential primitives.
- `CloudShell.ControlPlane.Client`: remote domain client for the Control Plane API.
- `CloudShell.Configuration.Client`: SDK client for Configuration Store service APIs.
- `CloudShell.Configuration`: Microsoft `IConfiguration` provider for CloudShell configuration services.
- `CloudShell.ConfigurationService`: standalone ASP.NET Core configuration service application.
- `CloudShell.Secrets.Client`: SDK client for Secrets Vault service APIs.
- `CloudShell.Persistence`: EF Core SQLite or SQL Server persistence for resources and local Identity.
- `CloudShell.Providers.Applications`: extension for executable application resources on a local development machine.
- `CloudShell.Providers.Configuration`: extension for local configuration service resources.
- `CloudShell.Providers.Docker`: reference extension for local Docker Engine and containers.
- `CloudShell.Abstractions.Tests`: extension registration and validation tests.

## Prerequisites

- .NET 11 preview SDK.
- Docker Desktop or a local Docker daemon if you want to use the Docker sample.

The repository includes `global.json` to pin the preview SDK expected by the project.

## Run

From the repository root:

```bash
dotnet restore
dotnet run --project CloudShell.Host --urls http://localhost:5088
```

Then open:

```text
http://localhost:5088
```

Useful routes:

- `/resources`: resource inventory and resource groups.
- `/resources/add`: add a resource by choosing a registered resource type.
- `/resources/templates`: export and import resource group templates.
- `/resources/docker-engine`: Docker Engine detail view.
- `/extensions`: installed extensions and contributed resource types.
- `/api/control-plane/v1`: versioned Control Plane API.
- `<configuration-service-endpoint>/api/configuration/entries?resourceId=...`: token-authenticated configuration service API.
- `/openapi/control-plane-v1.json`: OpenAPI document for generated clients.

Minimal host samples are also available:

- `samples/CloudShell.UiExtensionHost`: hosts only the CloudShell UI and a custom UI extension.
- `samples/CloudShell.ResourceHost`: hosts CloudShell UI and Control Plane together with a sample resource provider.
- `samples/ProjectReference/Host`: declares two ASP.NET Core project resources where one references the other in an Aspire-style dev loop.

## Test

```bash
dotnet build
dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore
```
## Documentation

- [Why CloudShell](docs/why-cloudshell.md)
- [CloudShell and Aspire](docs/cloudshell-and-aspire.md)
- [Domain model](docs/domain-model.md)
- [System design guidelines](docs/system-design-guidelines.md)
- [Roadmap](docs/roadmap.md)
- [Progress and MVP tracker](docs/progress.md)
- [Control plane API and generated clients](docs/control-plane-api.md)
- [Authentication and authorization](docs/authentication-and-authorization.md)
- [Hosting model](docs/hosting-model.md)
- [Localization](docs/localization.md)
- [Persistence](docs/persistence.md)
- [Programmatic resources](docs/programmatic-resources.md)
- [Resource templates](docs/resource-templates.md)
- [Configuration services](docs/configuration-services.md)
- [Executable applications](docs/executable-applications.md)
