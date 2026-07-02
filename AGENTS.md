# CloudShell Agent Guide

CloudShell is an extensible, self-hosted cloud portal for local development,
team-owned platform tooling, and on-premise environments. It uses Blazor,
Fluent UI, and .NET 11 preview.

Its goal is to let teams register, group, inspect, and operate resources
through a consistent shell while keeping the WebUI deployable independently
from Control Plane services.

## Start Here

Before making product or architecture changes, read:

- [CloudShell goal](docs/goal.md)
- [Development workflow](CONTRIBUTIONS.md)
- [Architecture](docs/architecture.md)
- [Domain model](docs/domain-model.md)
- [Feature and specification index](docs/features.md)
- [Resource model](docs/resource-model.md)
- [Resource model providers](docs/resource-model-providers.md)
- [Provider-created and runtime-managed resources](docs/runtime-managed-resources.md)
- [Naming conventions](docs/naming-conventions.md)
- [System design guidelines](docs/system-design-guidelines.md)
- [Architecture decision log](ADR.md)
- [Changelog](CHANGELOG.md)

For focused areas, read the relevant documentation:

- [Control Plane API](docs/control-plane-api.md)
- [Hosting model](docs/hosting-model.md)
- [Authentication and authorization](docs/authentication-and-authorization.md)
- [Extensions](docs/extensions.md)
- [Programmatic resources](docs/programmatic-resources.md)
- [Resource templates](docs/resource-templates.md)
- [Orchestration and deployments](docs/orchestration-and-deployments.md)
- [Observability](docs/observability.md)
- [Container Hosts](docs/resources/container-hosts.md)
- [Storage and Volumes](docs/resources/storage-and-volumes.md)
- [Configuration services](docs/configuration-services.md)
- [Application resources](docs/resources/)
- [Persistence](docs/persistence.md)

## Solution Shape

- `CloudShell.Abstractions` — public domain abstractions, extension SDK,
  shell contribution contracts, and resource model contracts.
- `CloudShell.ControlPlane` — Control Plane services, APIs, authorization,
  orchestration, persistence integration, and operational data management.
- `CloudShell.ControlPlane.Client` — remote Control Plane adapter that maps
  HTTP APIs back to domain managers.
- `CloudShell.Hosting` — Blazor shell UI, Resource Manager UI, shell layout,
  and extension-hosted views.
- `CloudShell.Host` — combined development host.
- `CloudShell.Persistence` — EF Core persistence for registrations, groups,
  identity, and platform state.
- `CloudShell.Providers.*` — provider extension packages.
- `CloudShell.ControlPlane.Providers.*` — built-in Resource model provider
  packages and their UI/runtime adapter integrations.
- `samples/` — focused hosting scenarios.
- `CloudShell.*.Tests` — abstraction, Control Plane, client/API, and sample
  test projects.

## UI Component Guidance

CloudShell uses Fluent UI Blazor. When working on Blazor UI, check the
[Fluent UI Blazor documentation](https://fluentui-blazor.azurewebsites.net/)
for component-specific behavior. Prefer Fluent UI components, layout
primitives, and design tokens before adding custom shell markup or styling.

Use `FluentAnchor` for navigational links that need `Href`; use
`FluentButton` for button actions such as submit, command execution, and
`OnClick` handlers. Use Fluent icons where they clarify behavior, artifact
type, or navigation target, especially on primary commands and links to
resource-owned artifacts.

## Architecture Rules

### Ownership Boundaries

The WebUI is a shell surface. The Control Plane owns resource inventory,
registrations, lifecycle procedures, templates, logs, traces, and
provider-backed operational data.

Consumers should depend on domain managers such as:

- `IResourceManager`
- `IResourceTemplateManager`
- `ILogManager`
- `ITraceManager`

Avoid depending directly on internal stores or generated HTTP clients.

Internal Control Plane services may depend on lower-level contracts such as:

- `IResourceManagerStore`
- `IResourceRegistrationStore`
- `ILogStore`

These are implementation details of the Control Plane.

### API Design

The API should reflect the domain model.

API DTOs represent established domain concepts and relationships. Transport
affordances such as href and method may be added where useful, but avoid
creating parallel Web API concepts when a domain concept already exists.

If a requested API shape implies a new concept, determine whether it belongs in
the domain model first, then project it through the API.

### Resource Actions

Resource actions are domain operations on resources.

They are not UI actions. UI components may present or invoke resource actions,
but the domain action and UI representation are separate concerns.

### Provider Boundaries

Provider-owned configuration and runtime state remain behind provider
contracts.

Platform-owned concepts such as registration, grouping, dependencies,
authorization, and lifecycle orchestration belong to the Control Plane.

### Security

Never expose secrets through resources, logs, diagnostics, APIs, samples, or
documentation.

## Workflow

Follow the shared workflow defined in
`CONTRIBUTIONS.md`.

That document defines:

- slice ownership
- implementation vs documentation slices
- verification requirements
- documentation updates
- ADR and changelog updates
- commit and push expectations

When processing proposals, verify current behavior against the code where
needed. Move or port implemented behavior and concrete details into the
relevant feature/specification docs, then keep proposals focused on active
work, open decisions, migration tasks, or deferred ideas. If implementation
docs were written from the start, link to them from the proposal instead of
duplicating the details.

For extensible behavior, feature/specification docs must capture the parity
contract for future providers, UI extensions, shell extensions, launchers, and
language SDKs: owning contracts, resource model shape, authoring surfaces,
runtime boundaries, API/client projection, UI projection, security,
diagnostics, persistence/lifecycle behavior, and known gaps.

Review Mermaid diagrams when moving implemented proposal content. Valid current
diagrams belong in feature/specification docs; stale diagrams should be
updated before moving; proposal diagrams should remain only for active design
or deferred work.

## Skills

For new product features:

- `.codex/skills/cloudshell-feature-development/SKILL.md`

For stabilization and MVP hardening:

- `.codex/skills/cloudshell-stabilization/SKILL.md`

Keep skills concise and update referenced documentation instead of duplicating
project guidance.

## Verification Baseline

Use targeted tests while developing.

For changes affecting the resource model, Control Plane, API/client layer, or
samples, run:

```
bash dotnet build CloudShell.slnx --no-restore  dotnet test CloudShell.ControlPlane.Tests/CloudShell.ControlPlane.Tests.csproj --no-restore  dotnet test CloudShell.ControlPlane.Client.Tests/CloudShell.ControlPlane.Client.Tests.csproj --no-restore  dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore  dotnet test CloudShell.Sample.Tests/CloudShell.Sample.Tests.csproj --no-restore
```
