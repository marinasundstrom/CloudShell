# CloudShell Agent Guide

This repository contains CloudShell, an extensible, self-hosted cloud-portal
shell for local development, team-owned platform tooling, and on-premise
environments. It uses Blazor, Fluent UI, and .NET 11 preview.

CloudShell's goal is to let teams register, group, inspect, and operate
resources through a consistent shell while keeping the WebUI shell deployable
separately from Control Plane services.

## Start Here

Before making product or architecture changes, read:

- [CloudShell goal](docs/goal.md)
- [Domain model](docs/domain-model.md)
- [System design guidelines](docs/system-design-guidelines.md)
- [Changelog](CHANGELOG.md)

For focused areas, read the relevant docs:

- [Control Plane API](docs/control-plane-api.md)
- [Hosting model](docs/hosting-model.md)
- [Authentication and authorization](docs/authentication-and-authorization.md)
- [Extensions](docs/extensions.md)
- [Programmatic resources](docs/programmatic-resources.md)
- [Resource templates](docs/resource-templates.md)
- [Configuration services](docs/configuration-services.md)
- [Application resources](docs/resources/application-resources.md)
- [Persistence](docs/persistence.md)

## Solution Shape

- `CloudShell.Abstractions`: public domain abstractions, extension SDK, shell
  contribution contracts, resource model contracts.
- `CloudShell.ControlPlane`: Control Plane services, API endpoints,
  authorization adapters, resource/log stores, orchestration, persistence
  integration.
- `CloudShell.ControlPlane.Client`: remote Control Plane adapter that maps HTTP
  calls back to the domain-shaped managers.
- `CloudShell.Hosting`: Blazor shell UI, Resource Manager UI, shell layout,
  extension-hosted views.
- `CloudShell.Host`: development combined host.
- `CloudShell.Persistence`: EF Core persistence for registrations, groups, and
  identity.
- `CloudShell.Providers.*`: built-in/reference provider extensions.
- `samples/`: focused hosting scenarios.
- `CloudShell.*.Tests`: abstraction, Control Plane service, client/API
  contract, and sample smoke tests.

## Architecture Rules

The WebUI is the shell surface. The Control Plane owns resource inventory,
registrations, lifecycle procedures, logs, templates, and provider-backed
operational data.

Consumers should depend on domain managers such as `IResourceManager`,
`IResourceTemplateManager`, `ILogManager`, and `ITraceManager`, not internal
stores or generated HTTP clients.

Internal Control Plane services may use lower-level contracts such as
`IResourceManagerStore`, `IResourceRegistrationStore`, `ILogStore`, and provider
interfaces. Those are implementation contracts for the service process.

The API contract should resemble the domain model. API DTOs are contract
entities for established domain entities and relationships. Add transport
affordances such as `href` and `method` where useful, but do not invent parallel
Web API concepts when a domain concept already exists.

Resource actions are domain operations on resources. They are not UI actions.
UI controls may render or invoke resource actions, but the domain action and UI
presentation are separate concepts.

Provider-owned configuration and runtime state should stay behind provider
contracts. Platform-owned registration, grouping, and dependency state belongs
to the Control Plane.

## Workflows

For new product features, use the repo-local skill:

- `.codex/skills/cloudshell-feature-development/SKILL.md`

For stabilization and MVP hardening, use:

- `.codex/skills/cloudshell-stabilization/SKILL.md`

Keep these skills concise and update their referenced docs instead of copying
large guidance into the skill files.

## Testing

Use targeted tests while developing. For changes touching the resource model,
Control Plane, API, remote client, or samples, run the verification baseline
from [Changelog](CHANGELOG.md):

```bash
dotnet build CloudShell.sln --no-restore
dotnet test CloudShell.ControlPlane.Tests/CloudShell.ControlPlane.Tests.csproj --no-restore
dotnet test CloudShell.ControlPlane.Client.Tests/CloudShell.ControlPlane.Client.Tests.csproj --no-restore
dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore
dotnet test CloudShell.Sample.Tests/CloudShell.Sample.Tests.csproj --no-restore
```

Docs-only changes do not require tests, but run `git diff --check` before
committing.

## Change Discipline

Keep changes scoped to the owning layer. If a requested API shape implies a new
concept, first decide whether the domain model needs that concept, then project
it through the API.

When stabilizing behavior, add tests at the layer that owns the behavior:

- Control Plane service tests for resource state and validation behavior.
- Client/API contract tests for HTTP shape, auth, errors, hypermedia, and
  remote mapping.
- Sample tests for hosted scenarios.
- Abstraction tests for public DSL and extension contracts.

Update [Changelog](CHANGELOG.md) when a decision, completed item, or next
priority changes.
