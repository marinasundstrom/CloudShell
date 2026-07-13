# Shell customization design goals

CloudShell's core value proposition is a configurable cloud-portal shell built
on CoreShell. CoreShell provides the common shell building blocks: pages,
menus, navigation targets, sections, section outlets, route resolution, and
extension modules. CloudShell composes those blocks with its own product
services, Fluent UI presenters, Resource Manager, Observability, Usage, and
Control Plane-backed integrations.

The direction is to keep CoreShell product-neutral while letting CloudShell
customize its UI and add CloudShell-specific extension points. Resource
Manager, Logs, settings, provider workspaces, and future product areas should
plug into CoreShell-backed shell surfaces instead of being hard-coded
assumptions in the shell chrome.

## Objectives

- Make shell content opt-in. Resource Manager, Logs, provider-specific workspaces, and future product areas should be extensions, not assumptions baked into the shell chrome.
- Let an extension set choose its own start experience. A deployment should be able to provide its own overview page, dashboard, or hosted workspace and make it the root landing experience.
- Support CMS-like composition without giving extensions unchecked control of the app. Extensions contribute typed views, view menu items, services, and capabilities; the host owns validation, layout, routing conventions, and ordering.
- Grow from simple navigation contribution into a layout contribution system:
  menu groups, menu items, child items, pages, settings pages, notification
  surfaces, and named content areas should all have stable contribution
  contracts.
- Keep Resource-style experiences reusable. Extension-authored views should be able to use the same left-menu and content-host pattern as resource configuration views without depending on Resource Manager.
- Treat Resource Manager as the main consumer of the shell composition model.
  Resource Manager can fully exploit common shell primitives, but it should not
  be the only way to use them.
- Preserve a controlled customization model. Duplicate routes, duplicate contribution IDs, missing dependencies, and conflicting start-route choices should fail fast during startup.
- Keep customization available programmatically first. Extension authors can configure the shell through `ICloudShellExtensionBuilder`, package-level registration methods, and DI.
- Design toward permissioned UI customization. Administrators or users with the right permissions should eventually be able to configure shell content through the UI.

## Current scope

The current implementation supports programmatic customization. New shell
navigation, settings, and page contributions should prefer CoreShell modules
through `AddCoreShellModule(...)`:

- `CoreShellModule` for extension-owned pages, menus, menu groups, menu items,
  section outlets, and sections.
- `CoreShellTarget` for page, section, or href navigation.
- `ICoreShellNavigationService`, `ICoreShellRouteService`,
  `ICoreShellSectionService`, and `ICoreShellSectionAddressService` for
  shell-owned rendering and linking.
- `ICoreShellNotificationService` for UI presenters that need to query the
  current user's notification instances, react to notification change signals,
  and acknowledge or dismiss notification instances without depending on the
  backing store or transport. CoreShell hosts can back this with in-memory,
  UI-local, remote, or custom notification sources.
- `ICoreShellNotificationProducer` for hosts or extension code that publish
  notification instances and need the returned instance ID to update or dismiss
  operation feedback later. The producer does not have to run inside the UI
  app. CoreShell hosts can implement this locally or remotely. In CloudShell,
  this should be backed by the Control Plane notification API so UI-host code,
  workers, and separate apps publish to the same Control Plane-owned
  notification store.
- `ICoreShellToastService` for transient toast-only signals that should not
  create notification-center history. `PublishAsync` returns the created toast
  so the caller can update, dismiss, or replace it by ID.
- A CoreShell settings service contract for settings consumed or managed
  through the shell UI. CoreShell should own the UI-facing read/write contract
  and extension-owned settings surface; hosts decide whether the backing store
  is local, Control Plane-backed, remote, or custom. The current CloudShell
  environment settings provider is the implementation to migrate behind that
  CoreShell boundary.
- `CoreShell.Blazor` helpers such as `CoreShellBlazorContent.For<TComponent>()`
  and `AddSection<TComponent>(...)` for Blazor-backed content.

CloudShell also keeps product-specific extension points:

- `RegisterView<TComponent>()` for extension-owned routable pages keyed by component type.
- `AddNavigationItem` for navigation menu items with explicit view or href targets.
- `ReplaceNavigationItem` for replacing a named navigation item.
- `AddCustomView` for shell-hosted views that use CloudShell's common layout.
- `AddCustomViewMenuItem<TComponent>` for menu items inside shell-hosted views.
- `UseStartView` and `UseStartRoute` for selecting the shell start experience.
- `ICloudShellNavigator` for optional strongly typed or view ID-based navigation.
- User-managed extension activation through the Extensions UI, guarded by
  `shell.configure`.
- User-scoped CloudShell environment preferences through
  `ICloudShellUserSettingsProvider`.

CoreShell and the lower-level composition engine are documented in
[UI composition](ui-composition.md). `samples/CoreShell.FluentUiSample` is the
reference CoreShell-only Fluent UI app and future CoreShell development
testbed. It shows that an app can own its own Blazor layout and presenters
while using CoreShell for navigation, targets, section outlets, and extension
modules. It also proves the first CoreShell notification UI reference surface:
a topbar notification center, toast-style transient status cards, and a
sample-owned in-memory provider behind `ICoreShellNotificationService`.
Notification actions are optional, but when present the toast and notification
center should both expose them so a user can act immediately or return to the
item later. When no actions are present, a toast or notification can fall back
to a whole-body link when it has a target, or to click-to-dismiss behavior for
purely transient feedback. Visibility, time-to-live, and auto-dismiss behavior
are part of the notification or toast data: the reference sample uses CoreShell
defaults for short-lived plain toasts, keeps in-progress feedback visible until
the producer updates or dismisses it, and lets terminal toast presentation
expire without removing the notification-center item. The reference sample uses
the same shared toast-stack cap as CloudShell, with notification-backed toasts
taking priority and toast-only items filling the remaining space. Future hosts
can also register different notification or toast renderers for template keys
such as operation progress, approval, provider diagnostics, or deployment summaries
while keeping the common CoreShell behaviors for actions, links,
acknowledgement, dismissal, visibility, and lifetime. In CloudShell, persisted
notification records should be Control Plane-owned domain data; the UI owns how
those records are adapted into Fluent UI notification-center rows, toasts,
templates, icons, and action placement. The current CloudShell local-development
path renders a topbar notification center and notification-backed toast cards
over the in-memory Control Plane notification store. The same toast stack also
renders toast-only items from `ICoreShellToastService` for transient UI-local
signals that should not create notification history. CloudShell registers a
scoped in-memory CoreShell toast service for those UI-local signals, while
hosts can replace the service when they need a different source. Resource
Manager settings and template export confirmations use this path, with stable
toast IDs, because those facts do not need notification-center history.
Resource mutations such as template apply should continue through the Control
Plane notification path when they need durable or operation-oriented feedback.
When a producer publishes a toast with a stable ID, the in-memory CoreShell
toast service replaces the existing toast for that ID so repeated UI-local
operations and progress updates do not stack duplicate cards. Passive facts
can remain in the center without contributing to the unread count; in-progress
toast feedback remains visible while the backing operation is in progress. If
an in-progress toast was pinned with `AutoDismiss = Never`, updating it to a
terminal state without an explicit auto-dismiss override returns it to the
normal toast lifetime.
Resource recovery notifications describe recovery for the stable resource,
while replica-slot crash and replacement progress is projected as replica
repair so runtime-managed replica concerns do not masquerade as resource
recovery.
Warnings, failures, needs-attention items, and notifications with actions are
treated as attention-worthy. Toast-only signals use `ICoreShellToastService`
and do not create notification instances.
Scenario ownership and producer rules are documented in
[Notifications and toasts](notifications-and-toasts.md).
`samples/CompositionSandbox` remains the lower-level composition sandbox for
graph and renderer experiments below CoreShell.

The built-in Overview item has the special navigation ID `overview`. A
replacement changes the sidebar contribution and points to either a registered
view or a direct href. Registered views still own routing through their
component's `@page` directive.

The navigator is deliberately a small layer over Blazor navigation. It lets
CloudShell-owned pages navigate by component type or view ID, validates route
arguments against registered views, and still permits direct href navigation for
external or non-registered paths.

Extensions should expose stable view-key constants when other extensions need
to navigate without referencing the UI component assembly. Callers that already
reference the UI assembly should prefer component-type navigation. Use
`ShellViewKeys.For<TComponent>(extensionId)` when a published key should be
explicitly scoped to an extension namespace.

The `AddCustomView` API name describes the implementation path: the extension is
adding a composed view hosted by the shell instead of a standalone `@page`
component. In the product experience, these are still ordinary shell views and
should be named, ordered, and navigated like any other view.

An extension can add a small shell-hosted view to demonstrate this pattern:

```csharp
builder.AddCoreShellModule(
    CoreShellModuleId.Create("sample.workspace"),
    module =>
    {
        module.AddPage(SamplePages.Workspace, "Sample workspace", "/workspace")
            .AddSections(SampleOutlets.WorkspaceMain)
            .AddSection<SampleWorkspaceOverview>(
                SampleSections.Overview,
                "Overview",
                10);

        module.AddMenu(ShellIds.MainMenu, "Main")
            .AddGroup(ShellIds.WorkspaceMenuGroup, "Workspace", 10)
            .AddItem(SampleMenuItems.Workspace, "Sample workspace", 10)
            .WithAttribute(CoreShellAttributeNames.Icon, "pulse")
            .Target(SamplePages.Workspace);
    });
```

CloudShell does not currently support per-user customization. Start-route and view/menu contributions are global for the installed extension set.

CloudShell does support per-user persisted environment preferences that are not
part of any Control Plane workload or resource domain model. The built-in theme
selector and collapsed navigation state currently use
`ICloudShellUserSettingsProvider` instead of browser local storage. That
provider is CloudShell's current implementation path; the CoreShell boundary
should define the reusable UI settings interface so a CoreShell-only host can
consume and manage shell UI settings without depending on CloudShell. Settings
are keyed by the authenticated user's stable claim (`NameIdentifier`, `sub`,
or name). When authentication is not enabled, or a host has no authenticated
principal, the provider uses a local profile so settings still persist
predictably.

The host selects exactly one storage backend with `Shell:EnvironmentSettings:Storage`:

```json
{
  "Shell": {
    "EnvironmentSettings": {
      "Storage": "Local"
    }
  }
}
```

Supported values:

- `Local`: store settings in the UI host's `Data/environment-settings.json`.
- `ControlPlane`: store settings through the Control Plane settings endpoint.
  This is useful for split or shared environments that want environment
  preferences to follow the same central service as the shell's remote
  integration, while still keeping the settings model separate from
  `IControlPlane` workload operations.

Shell and extension components should depend on
`ICloudShellUserSettingsProvider` rather than reading browser storage directly.
The provider intentionally stores only CloudShell environment preferences;
extension
contributions such as start routes, view registrations, and navigation ordering
remain global programmatic configuration today.

## Future considerations

- Define shell menu groups, menu items, child items, page registrations, and
  hosted content areas as first-class contribution types.
- Add a standardized settings surface where extensions can contribute settings
  pages without owning the whole settings route or layout.
- Build out the shell notification system beyond the local-development
  in-memory path, including durable or fetched backing stores, configurable
  notification rules, richer CloudShell operation producers, custom templates,
  and split-hosting change delivery. See the
  [shell notifications and toasts proposal](proposals/core/shell-notifications.md).
- Define named extension areas so shell and Resource Manager pages can accept
  provider-owned content without replacing the whole page.
- Store configurable shell layout state, such as enabled areas, navigation ordering, and selected start route.
- Decide which configurable shell layout settings are global, role-scoped, tenant-scoped, or user-scoped.
- Consider a widget system for overview/dashboard pages once the shell-hosted view model is stable.
- Provide an admin UI for enabling/disabling extension-contributed views and choosing the start experience.
- Keep extension-declared capabilities as the source of truth, even when UI configuration hides or reorders contributed content.

See the [shell composition future direction](future/shell-composition.md) for
the planned post-MVP direction.
