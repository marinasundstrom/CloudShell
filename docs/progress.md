# Progress

This is the living CloudShell progress tracker. Update it when a feature,
stabilization pass, or design decision changes the current direction.

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

## Completed recently

- Added a remote `IControlPlane` implementation for split hosting.
- Added split-hosting and sample smoke tests.
- Added remote Control Plane authentication coverage.
- Added API boundary validation and invalid-payload contract tests.
- Added internal Control Plane resource-state tests.
- Added resource action capability modeling.
- Added hypermedia resource actions to API resource responses.
- Removed legacy `actions` API compatibility from resource responses.

## Active stabilization areas

- Resource Manager state behavior and capability signaling.
- API contract stability for projected resources, actions, and errors.
- Sample coverage for combined and split hosting.
- OpenAPI/client generation readiness.

## Next priorities

1. Continue tightening internal Resource Manager behavior:
   - registration validation
   - dependency validation
   - parent/child resource projection
   - group inheritance
   - delete/action authorization edge cases
2. Align OpenAPI output with the intended domain projection.
3. Expand sample tests to cover the hypermedia resource action path.
4. Document any remaining MVP gaps as concrete tests or issues.

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
