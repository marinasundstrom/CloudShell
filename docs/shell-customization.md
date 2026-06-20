# Shell customization design goals

CloudShell's core value proposition is a configurable cloud-portal shell. The
shell should provide controlled extension points for UI integration while
letting teams decide which product areas they want to install, remove, or
replace.

The long-term direction is to make the CloudShell UI a standalone extensible
shell platform. Resource Manager, Logs, settings, provider workspaces, and
future product areas should plug into the same composition model instead of
being hard-coded assumptions in the shell chrome.

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

The current implementation supports programmatic customization:

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

CloudShell also has an experimental UI composition engine documented in
[UI composition](ui-composition.md). It is currently proven through a
standalone Blazor sample rather than the CloudShell extension model. That
engine provides typed IDs, a registry, page/menu/section registrations, link
resolution, and plain Blazor rendering components. CloudShell extension
integration will be layered on later after the standalone composition shape is
stable.

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
builder
    .RegisterView<SampleWorkspaceOverview>()
    .AddNavigationItem<SampleWorkspaceOverview>(
        text: "Sample workspace",
        icon: "pulse",
        order: 10);
```

CloudShell does not currently support per-user customization. Start-route and view/menu contributions are global for the installed extension set.

CloudShell does support per-user persisted environment preferences that are not
part of any Control Plane workload or resource domain model. The built-in theme
selector and collapsed navigation state use `ICloudShellUserSettingsProvider`
instead of browser local storage. Settings are keyed by the authenticated
user's stable claim (`NameIdentifier`, `sub`, or name). When authentication is
not enabled, or a host has no authenticated principal, the provider uses a
local profile so settings still persist predictably.

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
- Add a shell notification system with durable notification records,
  off-canvas notification history, and toast-style transient presentation.
- Define named extension areas so shell and Resource Manager pages can accept
  provider-owned content without replacing the whole page.
- Store configurable shell layout state, such as enabled areas, navigation ordering, and selected start route.
- Decide which configurable shell layout settings are global, role-scoped, tenant-scoped, or user-scoped.
- Consider a widget system for overview/dashboard pages once the shell-hosted view model is stable.
- Provide an admin UI for enabling/disabling extension-contributed views and choosing the start experience.
- Keep extension-declared capabilities as the source of truth, even when UI configuration hides or reorders contributed content.

See the [shell composition proposal](proposals/core/shell-composition.md) for
the planned post-MVP direction.
