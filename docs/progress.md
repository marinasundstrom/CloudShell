# Progress

This is the living CloudShell progress tracker. Update it when a feature,
stabilization pass, or design decision changes the current direction.

See also: [TODO](../TODO.md) for the current task queue that turns these
priorities into concrete next tasks.

## Current MVP focus

Make CloudShell functional and stable for the common-hosted scenario while
preserving the path to split hosting.

The MVP should prove:

- Combined UI and Control Plane hosting works reliably.
- Split UI and Control Plane samples build and smoke-test.
- The Control Plane exposes a stable domain-shaped client abstraction.
- The Control Plane API has a clear OpenAPI contract.
- Resource Manager behavior is predictable across states, validation failures,
  permissions, and provider capability differences.
- Samples demonstrate the intended hosting and resource declaration patterns.

## Recent decisions

- The WebUI is the shell surface; the Control Plane is a separately deployable
  service boundary.
- Consumers should use domain managers, not generated HTTP clients directly.
- Internal Control Plane stores/providers remain internal implementation
  contracts.
- Resource actions are domain operations on resources, not UI actions.
- Resource API responses expose resource actions as keyed hypermedia
  affordances.
- Resource action capabilities are separate signals that describe current
  executability and reasons.
- The domain model should be documented across product concepts, public
  abstractions, internal Control Plane services, provider contracts, API
  projection, and UI projection.
- Provider-owned resource configuration stays separate from platform-owned
  registration/group state.
- Projected resources use one uniform `Resource` shape. Broad behavior is
  modeled with `ResourceClass`, precise identity with `TypeId`, non-secret
  structural facts with `Attributes`, and runtime behavior through
  provider-owned descriptors instead of resource subclasses.
- Programmatic resource builders are declaration-time abstractions that create
  uniform resources and provider-owned configuration; executable, project, and
  container builders expose different authoring conveniences without becoming
  runtime resource types.
- Common executable, project, and container workload builder contracts live in
  `CloudShell.Abstractions`; provider packages own the concrete factory methods
  and implementations that populate provider-specific configuration.
- ASP.NET Core project resources are project-shaped resources with a
  provider-owned process runner; they do not project executable command
  attributes even though the provider starts them through `dotnet`.
- Resource declaration builder APIs use concise resource-oriented names such as
  `IResourceDeclarationBuilder` and `IResourceBuilder` instead of repeating the
  CloudShell product prefix.
- CloudShell environment preferences are user-scoped, workload-agnostic, and
  use one configured storage backend: local UI-host storage or Control
  Plane-backed storage.
- Top-level container app resources own deployment operations such as image
  updates. Container-engine providers such as Docker may project runtime
  container resources for inspection, but consumers should not need those
  runtime resource IDs to deploy a new app image.
- Resource-scoped events are the platform traceability stream for operations
  performed on resources, including who or what triggered the operation.
  Resource-type logs remain available for operational detail such as container
  console output.

## Completed recently

- Added a remote `IControlPlane` implementation for split hosting.
- Added split-hosting and sample smoke tests.
- Added remote Control Plane authentication coverage.
- Added API boundary validation and invalid-payload contract tests.
- Added internal Control Plane resource-state tests.
- Added resource action capability modeling.
- Added hypermedia resource actions to API resource responses.
- Removed legacy `actions` API compatibility from resource responses.
- Added direct `IResourceManager` validation for resource creation,
  registration, group assignment, and dependency updates.
- Added Resource Manager projection coverage for registered roots, dynamic
  children, declaration-assigned parents, group inheritance, and parent graph
  cycle safety.
- Added contract-level Control Plane errors with API `ProblemDetails` code
  projection and remote client mapping.
- Added delete/action contract-error coverage for missing resources, missing
  actions, unsupported providers, permission denial, dependent warnings, and
  delete capability alignment.
- Clarified that `CloudShell.Abstractions` is the cloud-plane client API and
  that projected resources expose action discovery while managers execute
  commands.
- Added client API helpers for canonical resource action IDs, resource action
  lookup, capability lookup, and manager-driven lifecycle action execution.
- Added a user-scoped CloudShell environment settings provider with selectable
  local or Control Plane-backed storage and theme/navigation preference
  integration.
- Renamed the projected domain entity from `CloudResource` to `Resource` and
  added `ResourceClass` projection through in-process resources, the Control
  Plane API, and the remote client.
- Added uniform resource attributes for class-defining, non-secret provider
  details such as workload kind, image, endpoint count, service port count, and
  configuration entry count.
- Added `ResourceClass` filtering to resource queries, the Control Plane API,
  and the remote client.
- Moved executable and project workload builder contracts into
  `CloudShell.Abstractions` alongside the existing container builder contract.
- Added generic declaration metadata for `ResourceClass` and non-secret
  attributes, and projected that metadata through Resource Manager overlays.
- Renamed the common programmatic resource builder contracts to
  `IResourceBuilder` and `IResourceDeclarationBuilder`.
- Added `ResourceClass` and non-secret attribute metadata to resource creation
  commands, HTTP requests, the remote client, and provider creation requests.
- Added generated Resource Manager detail views for resources without
  provider-owned detail routes, tabs, or update components.
- Added resource model class consistency validation for creation requests,
  provider projections, and declaration metadata, with result/diagnostic-based
  model validation.
- Separated ASP.NET Core project declaration and projection from executable
  command details, while preserving project app arguments, environment
  variables, endpoints, service discovery, and process-backed runtime behavior.
- Improved generated Resource Manager detail views with related-resource links,
  endpoint copy/open affordances, health metadata, logs, observability links,
  and action capability reasons.
- Defined resource attribute conventions: dotted lower-camel names,
  string-only non-secret values for MVP, invariant formatting, generated
  display behavior, and provider-specific prefix guidance.
- Aligned resource template import with the uniform resource validation model:
  invalid template envelopes now return diagnostics without creating resource
  groups or throwing from the domain API.
- Added first-class dependency auto-start failure details with a stable
  `dependencyAutoStartFailed` Control Plane error code, dependency path, blocked
  dependency, and concrete failure reason.
- Split declaration startup autostart from dependency autostart:
  programmatic declarations now use startup autostart semantics with provider
  defaults, while dependency startup uses `WithDependencyAutoStart(...)` and the
  same provider/default precedence.
- Added explicit start-after-create support for resource creation commands and
  runnable application registration UI, with provider policy carrying the
  default checkbox intent.
- Aligned OpenAPI output with the domain-shaped resource projection for
  resources, action affordance dictionaries, attributes, and creation options.
- Expanded the ResourceHost sample to exercise provider-backed resource
  actions through advertised hypermedia hrefs.
- Grouped sample projects in the solution by sample scenario so logical
  solution folders match the physical `samples/` layout.
- Added a domain/API image update command for top-level container app
  resources, with actor-attributed resource events for traceability,
  application-provider console logs for underlying container output, and
  split-host client mapping.

## Active stabilization areas

- Resource model consistency across provider overrides.
- Resource Manager state behavior and capability signaling.
- API contract stability for projected resources, provider-backed actions,
  OpenAPI output, and errors.
- Sample coverage for combined and split hosting.
- OpenAPI/client generation readiness.

## Next priorities

1. Continue tightening internal Resource Manager behavior as invalid-state gaps
   are found.
2. Document any remaining MVP gaps as concrete tests or issues.

## Verification baseline

For changes that touch the resource model, Control Plane, API, remote client, or
samples, run:

```bash
dotnet build CloudShell.sln --no-restore
dotnet test CloudShell.ControlPlane.Tests/CloudShell.ControlPlane.Tests.csproj --no-restore
dotnet test CloudShell.ControlPlane.Client.Tests/CloudShell.ControlPlane.Client.Tests.csproj --no-restore
dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore
dotnet test CloudShell.Sample.Tests/CloudShell.Sample.Tests.csproj --no-restore
```

Use narrower test runs first while developing, then run the baseline before
committing a cross-boundary change.
