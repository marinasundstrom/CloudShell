# Refactoring Tracker

This is the living task list for active CloudShell refactoring work. Keep it
focused on boundary, ownership, and implementation slices that are underway or
queued next. Durable product decisions still belong in `ADR.md`; landed changes
belong in `CHANGELOG.md`; feature shape belongs in the relevant proposal.

## Current Refactoring Goal

Clarify the Resource Manager, orchestration/deployment, resource provider, and
shared application-service boundaries so container apps prove the MVP flow
without forcing provider-specific logic into shared helpers.

## Boundary Decisions

- Resource Manager owns lifecycle orchestration, deployment apply,
  environment revision recording, dependency policy, authorization gates,
  resource graph state, and cross-provider diagnostics.
- Resource providers are the Resource Manager integration boundary for a
  resource type. A provider owns type-specific validation, lifecycle actions,
  action availability, resource projection, attribute-to-runtime mapping, and
  provider-specific commands.
- Shared application services are implementation support for application-like
  resources: process/container spawning, runtime state tracking, logs,
  environment-variable resolution, reusable projection helpers, and app-owned
  stores. They should not own Resource Manager deployment, revision, lifecycle,
  or replica-management semantics.
- Container app configuration revisions and Resource Manager environment
  revisions are separate concepts. Container app revisions track app
  configuration snapshots; environment revisions track materialized hosting
  environment outcomes.
- Replica groups and replica slots are orchestration concepts. Container apps
  define requested runtime state; Resource Manager deployment/orchestration
  reconciles replica groups and records outcomes.

## Active Slice

- [x] Investigate `ApplicationResourceService` responsibilities and current
  deployment/orchestration paths.
- [x] Route deployment-capable `Start` actions through Resource Manager
  deployment apply so first materialization creates deployment history and an
  environment revision baseline.
- [x] Remove provider-owned live replica scaling from the shared application
  service. Scaling updates container app intent; Resource Manager deployment
  apply reconciles replica groups.
- [x] Document the provider/application-service boundary in the container app
  and deployment proposals.
- [x] Commit the deployment-backed start and scaling boundary slice.
- [x] Split resource procedure follow-up signals so runtime reconciliation is
  distinct from restart-required UI prompts.
- [x] Extract container app revision numbering/history behavior into a
  dedicated revision unit with direct unit tests, while keeping
  `ApplicationResourceService` call sites stable for this slice.
- [x] Extract application workload configuration mapping into a dedicated
  factory with direct tests for workload kind selection, common runtime
  attributes, and replica-mode behavior.
- [x] Extract deterministic container app orchestrator deployment shape into a
  dedicated factory with direct tests for service identity, deployment inputs,
  revision scoping, and status mapping.
- [x] Extract application runtime state projection/transient lifecycle tracking
  into a dedicated tracker with direct tests for fresh/expired transient state,
  running fallback, and clear-starting/clear-stopping behavior.
- [x] Document local Docker daemon crash handling for Docker-backed
  verification runs.

## Next Slices

- [ ] Split `ApplicationResourceService` into narrower collaborators:
  application definition/query facade, projection service, container app
  deployment/revision service, runtime process/container runner support, and
  provider operation adapters.
- [ ] Move provider UI pages off direct `ApplicationResourceService` injection
  where they only need an authoring/query facade or Resource Manager managers.
- [ ] Move container-app-specific Resource Manager semantics into
  `ContainerApplicationResourceProvider` instead of delegating all behavior to
  the shared application service.
- [ ] Extract the container app orchestrator deployment factory from the shared
  service so deployment description is independently testable.
- [ ] Revisit post-apply teardown ownership. Prefer Resource Manager
  deployment/orchestration outcome data over provider-specific predecessor
  inference.
- [ ] Define a provider-facing change-application contract for applying
  attribute/configuration changes to materialized resources without requiring
  every provider to invent one-off update methods.

## Environment and UI Follow-Ups

- [ ] Keep the Environment page as a diagnostic projection over Resource
  Manager deployment/orchestration state. Do not let it become a second source
  of truth.
- [ ] Move reusable Environment read-model/projection logic out of `.razor`
  pages when it grows beyond simple UI composition.
- [ ] Keep container app UI app-centric. Link to Environment diagnostics when
  orchestration detail is useful, but do not require users to understand
  deployment/environment-revision internals for the default workflow.

## Verification Expectations

- Run focused tests for the owning layer first.
- For cross-boundary Resource Manager/provider changes, run:
  `dotnet test CloudShell.ControlPlane.Tests/CloudShell.ControlPlane.Tests.csproj --no-restore`
  `dotnet test CloudShell.ControlPlane.Client.Tests/CloudShell.ControlPlane.Client.Tests.csproj --no-restore`
  `dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore`
  `dotnet test CloudShell.Sample.Tests/CloudShell.Sample.Tests.csproj --no-restore`
  `dotnet build CloudShell.sln --no-restore`
- If Docker-backed sample tests fail before reaching CloudShell behavior
  because the Docker daemon is unavailable, follow `CONTRIBUTIONS.md`: verify
  with `docker info`, restart or unblock Docker, and record the blocked
  Docker-dependent verification instead of treating it as a product regression.
