# Shell composition

CloudShell UI should become an independently useful extensible shell, not only
the host for Resource Manager. Resource Manager remains the most important
first consumer, but the shell composition model should be general enough for
provider workspaces, settings, notifications, dashboards, and future product
areas.

## Status

Proposed. This is post-MVP platform direction. MVP work should keep Resource
Manager and supported local-development samples stable while avoiding
short-term UI decisions that would prevent this model.

## Goals

- Let extensions contribute menu groups, menu items, child items, shell pages,
  shell-hosted workspaces, settings pages, notifications, and named content
  areas through stable contracts.
- Keep the CloudShell UI deployable without the Control Plane. Resource
  Manager is an extension over the shell, not the definition of the shell.
- Let Resource Manager fully exploit the same shell primitives for resource
  pages, grouped resource menus, common settings, notifications, and extension
  areas.
- Keep host-owned validation for duplicate IDs, duplicate routes, missing
  dependencies, conflicting start routes, permission requirements, and invalid
  ordering.
- Preserve split hosting: UI contributions run in the UI host and call public
  domain managers or remote adapters, while Control Plane providers remain in
  the Control Plane host.
- Start with programmatic composition through capability packages and grow
  toward permissioned UI configuration later.

## Non-goals

- Do not make extensions own arbitrary shell layout markup or bypass host
  validation.
- Do not make Resource Manager-specific concepts the generic shell model.
- Do not introduce a persisted layout editor before the programmatic contracts
  are stable.
- Do not move resource lifecycle, provider configuration, authorization, or
  Control Plane behavior into UI contributions.

## Contribution model

The target shell model should include these contribution categories:

| Category | Purpose |
| --- | --- |
| Menu group | A top-level navigation grouping with stable ID, title, icon, order, permissions, and optional visibility rules. |
| Menu item | A navigation item targeting a registered page, hosted view, route, external link, or command. |
| Child item | A nested navigation item for grouped experiences such as Observability, Resource Manager, or provider workspaces. |
| Page | A routable or shell-hosted UI surface contributed by an extension. |
| Settings page | A standardized settings contribution rendered inside the common settings surface. |
| Notification source | A provider of durable or transient shell notifications. |
| Content area | A named slot where extensions can contribute supplemental content to an existing shell or Resource Manager page. |

The host should own route mapping, menu rendering, ordering, validation,
accessibility, localization boundaries, and permission-aware visibility.
Extensions own their components, labels, icons, capability metadata, and calls
to public domain managers.

## Reusable composition engine investigation

CloudShell already has two similar UI composition paths:

- Shell-hosted views use `CustomShellViewContribution` and
  `CustomShellViewMenuItemContribution`. They provide one hosted route, an
  ordered local menu, selected-item routing through the `item` query string,
  and dynamic component rendering.
- Resource Manager uses `ResourceTabContribution`,
  `ResourcePredefinedViewSectionContribution`, `ResourceViewId`, generated
  fallback tabs, predefined view validation, resource-tab grouping, selected
  tab routing through the `tab` query string, and dynamic component rendering
  with resource context parameters.

This means the reusable shape exists, but it is currently split between shell
and Resource Manager concepts. The shared part is not "resource tabs"; it is a
general grouped page/layout engine:

- a stable page or view ID
- optional route ownership
- ordered menu groups
- ordered menu items or child pages
- selected item state in the URL
- dynamic component hosting
- optional component parameter/context injection
- named content areas with ordered sections
- host validation for duplicate IDs, unknown targets, invalid replacement, and
  unsupported extension areas

Resource Manager should become an adapter over that engine rather than the
source model for it. `ResourceTabContribution` and predefined resource views
should remain resource-specific contracts because they carry resource type,
resource visibility, generated fallback, predefined concern, apply-button, and
resource context semantics. The reusable engine should provide the layout and
composition primitives underneath those contracts.

## Proposed layering

The future shell composition stack should be layered like this:

| Layer | Owns | Examples |
| --- | --- | --- |
| Shell composition primitives | Generic IDs, groups, pages, child items, content areas, ordering, routing mode, selection state, and component host metadata. | `ShellPageContribution`, `ShellPageGroupContribution`, `ShellPageItemContribution`, `ShellContentAreaContribution` |
| Shell composition renderer | Common layout, grouped local navigation, URL selection, dynamic component rendering, empty/not-found states, permission-aware visibility, and content-area rendering. | Hosted workspace layout, settings layout, Resource Manager detail layout |
| Product adapters | Domain-specific contribution APIs that project into shell composition primitives while preserving their own vocabulary and validation. | Resource Manager tabs, settings pages, notification center pages, provider workspaces |
| Domain services | Control Plane or provider behavior behind the UI. | `IResourceManager`, `ITraceManager`, identity provider hooks, provider settings contracts |

This lets Resource Manager reuse the same layout engine as other shell pages
without leaking resource-specific vocabulary into general shell APIs.

## Resource Manager extraction path

The practical path should be incremental:

1. Extract a generic ordered-section renderer from
   `GeneratedResourceViewLayout` and `ResourcePredefinedViewSections`, keeping
   the current Resource Manager section contracts as adapters.
2. Extract a generic grouped local-navigation model from the Resource Manager
   tab grouping and the shell-hosted view menu item model.
3. Let `CustomShellViewContribution` and Resource Manager detail pages both
   render through the same hosted-page layout component.
4. Add generic content-area contributions and map
   `ResourcePredefinedViewSectionContribution` into them for predefined
   resource views.
5. Add settings-page and notification-center adapters over the same primitives.
6. Only after the generic renderer is proven, consider renaming or replacing
   `CustomShellView` APIs with clearer shell composition names.

During the extraction, Resource Manager behavior should not regress:

- resource tab IDs remain `ResourceViewId`
- existing `tab=<group>:<view>` links continue to work
- predefined resource view visibility remains resource-shape and capability
  driven
- generated fallback tabs remain Resource Manager-owned
- apply-button behavior remains tied to resource configuration context
- provider-owned predefined view sections continue to receive resource
  context parameters

## Initial API sketch

The generic contracts should be small and preview-marked at first. A possible
shape:

```csharp
builder.AddShellPage(
    id: "acme.workspace",
    title: "Acme workspace",
    route: "/acme/workspace",
    icon: "server",
    order: 50,
    groupId: "workspace");

builder.AddShellPageItem<AcmeOverview>(
    pageId: "acme.workspace",
    id: "overview",
    title: "Overview",
    order: 10);

builder.AddShellContentAreaSection<AcmeSummary>(
    areaId: "resource-manager.resource.overview",
    id: "acme.summary",
    title: "Acme summary",
    order: 50);
```

Resource Manager can then keep its existing API while internally adapting to
the shell primitives:

```csharp
builder.AddResourcePredefinedViewSection<AcmeEndpointPolicy>(
    "acme.gateway",
    ResourcePredefinedViewIds.Endpoints,
    "acme.endpoint-policy",
    "Endpoint policy",
    50);
```

The second call remains the right public API for resource-provider authors
because it expresses Resource Manager intent. The first set is for generic
shell pages and content areas.

## Settings

CloudShell should provide a standardized settings page with extension-owned
sections or pages. This gives providers a home for shell or capability
configuration without each provider inventing its own settings route.

Settings contributions must declare whether their state is UI-local,
Control Plane-backed, provider-backed, or external-service-backed. The shell
should not persist provider configuration directly unless the provider exposes
a domain-shaped settings contract.

## Notifications

CloudShell should provide a notification system with two presentation modes:

- Toasts for transient, immediate feedback.
- An off-canvas notification center for durable notification history,
  unread state, and operator review.

Notification records should identify their source extension, severity,
timestamp, optional resource or route target, and optional action affordances.
Resource lifecycle events, permission denials, provider diagnostics, and
long-running operation results can feed this system without every feature
building its own notification UI.

## Extension areas

Named content areas let extensions add content to existing pages without
replacing the page. Resource Manager already has a version of this pattern with
predefined resource view sections. The broader shell should generalize it for
overview pages, settings pages, navigation chrome, notification details, and
provider workspaces.

Content areas should be explicit and documented. They are not arbitrary DOM
injection points; each area should define its expected context, ordering rules,
permission behavior, and lifecycle.

## Relationship to Resource Manager

Resource Manager should be treated as a large shell extension that uses the
same contribution model as other shell areas. It may contribute menu groups,
resource pages, settings entries, notifications, and content areas. Resource
providers can then extend Resource Manager through resource-specific contracts
while still relying on generic shell primitives for navigation, layout,
settings, and notifications.

For the local-development MVP, Resource Manager remains the primary proof:
application topology, settings, secrets, identity, storage, networking,
exposure, telemetry, monitoring, and diagnostics must be understandable there
before broad new shell surfaces become release blockers.

## Open questions

- Which shell composition settings are environment-global, tenant-scoped,
  role-scoped, or user-scoped?
- How should an extension declare permission requirements for shell groups,
  pages, settings sections, and notification actions?
- Should the generic page-selection query parameter be standardized, or should
  adapters keep domain-specific query names such as Resource Manager's `tab`?
- Which notification records belong in the Control Plane event stream, and
  which are UI-local shell state?
- Should shell layout configuration be stored in the UI host, the Control
  Plane, or a separate shell configuration service for split hosting?
- What is the minimum content-area API that supports useful extension points
  without creating brittle page internals?
