# CloudShell

> **Disclaimer:** Project is in an early phase. This is not a committed product.

CloudShell is a language-neutral, resource-oriented control plane where multiple programming ecosystems project into a shared resource model that can be executed by interchangeable providers. It is an extensible, self-hosted cloud-portal shell for local development and on-premise environments. It uses Blazor, Fluent UI, and .NET 11 preview, with an operational experience inspired by the .NET Aspire Dashboard.

The goal is to make it possible to build your own cloud-platform shell: a place where teams can register resources, group them by project, inspect endpoints and state, and let extensions add focused operational tools. The CloudShell UI and Control Plane are separate application surfaces; shell integrations connect the UI to those services through domain-shaped APIs.

**Resources view:**

<a href="images/resources.png"><img src="images/resources.png" style="max-height: 300px" alt="Resources view" /></a>

**Graphs (from UI):**

<p align="center">
  <a href="images/resource-graph.png"><img src="images/resource-graph.png" width="45%" alt="Resource graph" /></a>
  &nbsp;
  <a href="images/runtime-graph.png"><img src="images/runtime-graph.png" width="45%" alt="Runtime graph" /></a>
</p>

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

### CloudShell Environments

A CloudShell environment is the managed cloud-like environment for a local,
team-owned, or on-premise deployment. It is made up of a Control Plane,
installed capability packages, resource state, and one or more UI hosts.

### Host Applications

A CloudShell host application is the ASP.NET Core app that composes one
deployment. It can host the CloudShell UI, the Control Plane, or both. In local
development, a combined host can run programmatically declared resources
through the same local Control Plane that manages them.

### Capability Packages

CloudShell capability packages add environment capabilities. They can
contribute Control Plane resource providers, resource type definitions,
programmatic declaration helpers, Resource Manager UI support, shell views, and
client helpers. The intended distribution model is NuGet packages that expose
CloudShell extension registrations for host applications to install.

### Resources

Resources represent things CloudShell can manage, such as applications,
containers, databases, networks, storage, identities, configuration services,
and infrastructure components.

See [CloudShell Terminology](docs/terminology.md) for canonical vocabulary and
[Resource model](docs/resource-model.md) for the projected object model,
endpoint mappings, and ownership rules.

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

For normal local development, the simplest host-launcher shape is a small app
that declares the resources, then asks the CLI to start
`CloudShell.LocalDevelopmentHost` and apply the generated template:

```csharp
using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;

var app = CloudShellDistributedApplication.CreateBuilder("frontend-dev", args);

app.DefineResources(resources =>
{
    var settings = resources
        .AddConfigurationStore("settings")
        .WithEndpoint("http://localhost:5101");

    resources
        .AddJavaScriptApp("frontend", "../App")
        .WithPackageManager("npm")
        .WithScript("dev")
        .WithReference(settings)
        .WithEnvironmentVariable(
            "CLOUDSHELL_SETTINGS_ENDPOINT",
            "http://localhost:5101/api/configuration/stores/configuration.store%3Asettings/entries")
        .WithHttpEndpoint(host: "localhost", port: 5173, targetPort: 5173);
});

return (await app.RunAsync(new()
{
    CliProjectPath = "../../CloudShell.Cli/CloudShell.Cli.csproj",
    HostProjectPath = "../../CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj",
    HostUrl = new Uri("http://127.0.0.1:5097"),
    ControlPlaneUrl = new Uri("http://127.0.0.1:5097")
})).ExitCode;
```

Use `CLOUDSHELL_HOST_PROJECT` or `HostProjectPath` to target a custom
CloudShell host profile only when the Control Plane/UI process itself needs
additional extensions, authentication, persistence, or host-specific services.

## Projects

- `CloudShell.Hosting`: Razor class library for the Blazor shell, layout, static assets, built-in Resource Manager, Extensions, and Observability views.
- `CloudShell.AppHost`: combined-host composition helpers that wire the CloudShell UI and Control Plane into one ASP.NET Core process.
- `Launchers/`: language-specific app-host launcher packages that emit ResourceTemplates and ask the CLI or Control Plane API to apply them.
- `CloudShell.Host`: development sample host that wires CloudShell UI, Control Plane, and local provider extensions together.
- `CloudShell.LocalDevelopmentHost`: stable local Control Plane and UI host profile with the built-in provider presets for launcher-based samples.
- `CloudShell.ControlPlane`: control-plane services, authorization adapters, resource/log stores, and the versioned OpenAPI endpoint module.
- `CloudShell.Abstractions`: extension SDK, shell contributions, and resource contracts.
- `CloudShell.Client`: shared SDK client credential primitives.
- `CloudShell.ControlPlane.Client`: remote domain client for the Control Plane API.
- `CloudShell.Configuration.Client`: SDK client and `IConfiguration` integration for Configuration Store service APIs.
- `CloudShell.Configuration`: service-discovery configuration helpers.
- `CloudShell.ConfigurationService`: standalone ASP.NET Core configuration service application.
- `CloudShell.Secrets.Client`: SDK client and `IConfiguration` integration for Secrets Vault service APIs.
- `CloudShell.Persistence`: EF Core SQLite or SQL Server persistence for resources and local Identity.
- `CloudShell.ResourceModel`: Resource model and ResourceDefinition graph contracts.
- `CloudShell.ControlPlane.ResourceModel`: Control Plane Resource Manager integration for graph-backed Resource model state.
- `CloudShell.ControlPlane.Providers`: built-in Resource model providers and their Control Plane/runtime adapter integrations.
- `CloudShell.ControlPlane.Providers.UI`: Resource Manager UI integration for built-in Resource model providers.
- `CloudShell.Abstractions.Tests`: extension registration and validation tests.

## Contributing

See the project workflow and tracking docs:

- [Development workflow](CONTRIBUTIONS.md): how to make changes in focused,
  verified slices.
- [Changelog](CHANGELOG.md): dated implementation, stabilization, sample, and
  documentation history.
- [Architecture decision log](ADR.md): durable product and architecture
  decisions.

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

Local-development host and launcher samples are also available:

- `CloudShell.LocalDevelopmentHost`: reusable Control Plane/UI host profile used by launcher-based samples.
- `samples/CSharpAppHost`: declares a JavaScript app and Configuration Store resource from a C# launcher app, then applies the template through the CLI.
- `samples/TypeScriptAppHost`: declares the same style of graph from TypeScript using the experimental `@cloudshell/local-development` package.
- `samples/JavaScriptApp`: runs a Node.js app resource as a local process managed by CloudShell.
- `samples/JavaApp`: runs a Java app resource as a local JVM process managed by CloudShell.
- `samples/JavaAppHost`: declares a Java app, Configuration Store, and Secrets Vault from a Java launcher source file, then applies the template through the CLI.
- `samples/JavaScriptContainerApp`: wraps a JavaScript app as a Dockerfile-backed container app with replica scaling.
- `samples/CloudShell.UiExtensionHost`: hosts only the CloudShell UI and a custom UI extension.
- `samples/CloudShell.ResourceHost`: hosts CloudShell UI and Control Plane together with a sample resource provider.
- `samples/ProjectReference/Host`: declares two ASP.NET Core project resources where one references the other in an Aspire-style dev loop.

## Test

```bash
dotnet build
dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore
```
## Documentation

- [CloudShell goal](docs/goal.md)
- [Why CloudShell](docs/why-cloudshell.md)
- [CloudShell and Aspire](docs/cloudshell-and-aspire.md)
- [Domain model](docs/domain-model.md)
- [Launchers and app hosts](docs/launchers-and-app-hosts.md)
- [CloudShell Terminology](docs/terminology.md)
- [System design guidelines](docs/system-design-guidelines.md)
- [Roadmap](docs/roadmap.md)
- [Architecture decision log](ADR.md)
- [Changelog](CHANGELOG.md)
- [Control plane API and generated clients](docs/control-plane-api.md)
- [Authentication and authorization](docs/authentication-and-authorization.md)
- [Hosting model](docs/hosting-model.md)
- [Localization](docs/localization.md)
- [Persistence](docs/persistence.md)
- [Programmatic resources](docs/programmatic-resources.md)
- [Resource templates](docs/resource-templates.md)
- [Configuration services](docs/configuration-services.md)
- [Executable applications](docs/executable-applications.md)
