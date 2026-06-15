# CloudShell Agent Guide

CloudShell is an extensible, self-hosted cloud portal for local development,
team-owned platform tooling, and on-premise environments. It uses Blazor,
Fluent UI, and .NET 11 preview.

Its goal is to let teams register, group, inspect, and operate resources
through a consistent shell while keeping the WebUI deployable independently
from Control Plane services.

## Start Here

Before making product or architecture changes, read:

- CloudShell goal
- Development workflow
- Domain model
- System design guidelines
- Architecture decision log
- Changelog

For focused areas, read the relevant documentation:

- Control Plane API
- Hosting model
- Authentication and authorization
- Extensions
- Programmatic resources
- Resource templates
- Configuration services
- Application resources
- Persistence

## Solution Shape

- CloudShell.Abstractions — public domain abstractions, extension SDK,
  shell contribution contracts, and resource model contracts.
- CloudShell.ControlPlane — Control Plane services, APIs, authorization,
  orchestration, persistence integration, and operational data management.
- CloudShell.ControlPlane.Client — remote Control Plane adapter that maps
  HTTP APIs back to domain managers.
- CloudShell.Hosting — Blazor shell UI, Resource Manager UI, shell layout,
  and extension-hosted views.
- CloudShell.Host — combined development host.
- CloudShell.Persistence — EF Core persistence for registrations, groups,
  identity, and platform state.
- CloudShell.Providers.* — built-in and reference provider extensions.
- samples/ — focused hosting scenarios.
- CloudShell.*.Tests — abstraction, Control Plane, client/API, and sample
  test projects.

## Architecture Rules

### Ownership Boundaries

The WebUI is a shell surface. The Control Plane owns resource inventory,
registrations, lifecycle procedures, templates, logs, traces, and
provider-backed operational data.

Consumers should depend on domain managers such as:

- IResourceManager
- IResourceTemplateManager
- ILogManager
- ITraceManager

Avoid depending directly on internal stores or generated HTTP clients.

Internal Control Plane services may depend on lower-level contracts such as:

- IResourceManagerStore
- IResourceRegistrationStore
- ILogStore

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
CONTRIBUTIONS.md.

That document defines:

- slice ownership
- implementation vs documentation slices
- verification requirements
- documentation updates
- ADR and changelog updates
- commit and push expectations

## Skills

For new product features:

- .codex/skills/cloudshell-feature-development/SKILL.md

For stabilization and MVP hardening:

- .codex/skills/cloudshell-stabilization/SKILL.md

Keep skills concise and update referenced documentation instead of duplicating
project guidance.

## Verification Baseline

Use targeted tests while developing.

For changes affecting the resource model, Control Plane, API/client layer, or
samples, run:

bash dotnet build CloudShell.sln --no-restore  dotnet test CloudShell.ControlPlane.Tests/CloudShell.ControlPlane.Tests.csproj --no-restore  dotnet test CloudShell.ControlPlane.Client.Tests/CloudShell.ControlPlane.Client.Tests.csproj --no-restore  dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore  dotnet test CloudShell.Sample.Tests/CloudShell.Sample.Tests.csproj --no-restore 