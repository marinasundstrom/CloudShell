# Resource Provider Integration Workflow

User-facing resource providers usually need two extension surfaces:

- a non-UI [Control Plane resource provider](control-plane-resource-providers.md)
  that projects and operates resources
- a [Resource Manager UI integration](resource-manager-ui.md) that lets users
  register, update, inspect, and operate those resources in the CloudShell UI

These surfaces are logically connected but not the same registration contract.
The CloudShell UI and the Control Plane are distinct apps, even when the
development host runs both in one ASP.NET Core process.

Resource Manager is often used as shorthand for the whole product area that
includes both its CloudShell UI shell extension and its Control Plane backend
services. When writing provider code or documentation, name the side that is
being extended: Resource Manager UI integration for shell-facing work, and
Control Plane resource provider for backend resource behavior.

## Recommended Path

When creating a new resource provider:

1. Define the resource-domain behavior in the Control Plane provider.
2. Add programmatic declaration helpers when the resource should be authored in
   code.
3. Project resource actions, logs, attributes, endpoints, dependencies, health,
   and provider-owned state through the domain model.
4. Add Resource Manager UI integration for the user-facing experience.
5. Register Add Resource forms, update components, tabs, detail routes, and UI
   actions through the Resource Manager UI extension framework.

A provider can intentionally be programmatic-only or target deployments that do
not use CloudShell UI. In that case, it should still project a complete
resource shape so generated Resource Manager views can inspect it if the UI is
later added. If the resource is expected to be managed by interactive users,
shipping only the Control Plane provider is incomplete.

## Layering

The layers build upward:

1. The base UI extension architecture contributes shell views, navigation,
   shell-hosted workspaces, and activation.
2. Resource Manager UI extensions use that UI architecture to contribute
   resource-specific presentation and workflows.
3. Control Plane resource providers contribute non-UI resource behavior and
   provider-owned operations.
4. A complete user-facing provider usually includes both the Control Plane
   provider and the Resource Manager UI integration.

Do not put UI-only concepts into Control Plane contracts. Do not model UI
actions as resource actions. Resource actions are domain operations that can be
guarded by resource operation permissions. UI actions are Resource Manager
presentation behaviors registered by the UI integration.
