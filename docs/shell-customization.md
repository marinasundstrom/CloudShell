# Shell customization design goals

CloudShell's core value proposition is a configurable cloud-portal shell. The shell should provide controlled extension points for UI integration while letting teams decide which product areas they want to install, remove, or replace.

## Objectives

- Make shell content opt-in. Resource Manager, Logs, provider-specific workspaces, and future product areas should be extensions, not assumptions baked into the shell chrome.
- Let an extension set choose its own start experience. A deployment should be able to provide its own overview page, dashboard, or hosted workspace and make it the root landing experience.
- Support CMS-like composition without giving extensions unchecked control of the app. Extensions contribute typed views, view menu items, services, and capabilities; the host owns validation, layout, routing conventions, and ordering.
- Keep Resource-style experiences reusable. Extension-authored views should be able to use the same left-menu and content-host pattern as resource configuration views without depending on Resource Manager.
- Preserve a controlled customization model. Duplicate routes, duplicate contribution IDs, missing dependencies, and conflicting start-route choices should fail fast during startup.
- Keep customization available programmatically first. Extension authors can configure the shell through `ICloudShellExtensionBuilder`, package-level registration methods, and DI.
- Design toward permissioned UI customization. Administrators or users with the right permissions should eventually be able to configure shell content through the UI.

## Current scope

The current implementation supports programmatic customization:

- `AddView<TComponent>` for extension-owned routable pages.
- `AddNavigation` for navigation links.
- `AddCustomView` for shell-hosted views that use CloudShell's common layout.
- `AddCustomViewMenuItem<TComponent>` for menu items inside shell-hosted views.
- `UseStartRoute` for selecting the shell start experience.

The `AddCustomView` API name describes the implementation path: the extension is
adding a composed view hosted by the shell instead of a standalone `@page`
component. In the product experience, these are still ordinary shell views and
should be named, ordered, and navigated like any other view.

An extension can add a small shell-hosted view to demonstrate this pattern:

```csharp
builder
    .AddCustomView(
        id: "sample.workspace",
        title: "Sample workspace",
        route: "/sample-workspace",
        icon: "pulse",
        order: 10,
        description: "A simple shell view contributed through the CloudShell extension model.")
    .AddCustomViewMenuItem<SampleWorkspaceOverview>(
        viewId: "sample.workspace",
        id: "overview",
        title: "Overview",
        order: 10,
        description: "Show the sample workspace overview.");
```

CloudShell does not currently support per-user customization. Start-route and view/menu contributions are global for the installed extension set.

## Future considerations

- Store configurable shell layout state, such as enabled areas, navigation ordering, and selected start route.
- Add authorization checks around editing shell configuration.
- Decide which customization settings are global, role-scoped, tenant-scoped, or user-scoped.
- Consider a widget system for overview/dashboard pages once the shell-hosted view model is stable.
- Provide an admin UI for enabling/disabling extension-contributed views and choosing the start experience.
- Keep extension-declared capabilities as the source of truth, even when UI configuration hides or reorders contributed content.
