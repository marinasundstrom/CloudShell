# UI Extension Architecture

UI extensions add shell views, navigation, shell-hosted workspaces, and start
page behavior. They run in the CloudShell UI app and should depend on the
domain-shaped shell integration APIs instead of internal Control Plane stores
or provider implementations.

The long-term shell direction is broader than the current APIs. CloudShell UI
should act as an extensible shell platform where extensions can eventually add
menu groups, menu items, child items, pages, settings pages, notifications, and
named content areas. The current view and navigation APIs are the first
programmatic layer of that model.

This is the base UI extension architecture. Resource Manager UI extensions
build on these same Blazor, routing, navigation, and activation mechanisms for
resource-specific presentation. Resource-specific UI such as Add Resource
forms, update components, detail tabs, and resource UI actions is covered by
[Resource Manager UI extensions](resource-manager-ui.md).

## Views

Views are ordinary routable Blazor components in the extension assembly.
`RegisterView<TComponent>()` records the component by type, discovers the route
declared by the component's `@page` directive, and records the component
assembly so the host can include it in both Blazor routing and server endpoint
mapping. Use `RegisterView<TComponent>("stable.id")` when a string ID is part
of a public integration contract or when you want an extension-namespaced key.

Registering a view does not add it to the sidebar. Use view registration for
pages that need to be addressable by ID, for hidden detail/workflow pages, and
for pages that should be targets for navigation items or start routing.

CloudShell supports two implementation styles for views:

- `RegisterView<TComponent>` contributes a standalone routable component by ID.
- `AddCustomView` contributes a shell-hosted view with the standard CloudShell
  layout and extension-owned menu item components.

Both styles are first-class shell views in the product UI. The `CustomView`
name is an API-level distinction for adding composed views through the shell
host.

## Navigation Items

Use `AddNavigationItem` to add a sidebar item. Every item has an explicit
target: `AddNavigationItem<TView>` points to a registered view by type, while
`NavItemTarget.ForHref("/internal/path")` or
`NavItemTarget.ForHref("https://...")` points directly to an internal or
external href without requiring view registration. `NavItemTarget.ForView`
remains available for the rare case where a view was registered with an
explicit string ID.

```csharp
builder
    .RegisterView<Pages.AcmeOverview>()
    .AddNavigationItem<Pages.AcmeOverview>(
        text: "Acme Overview",
        icon: "grid",
        order: 20)
    .AddNavigationItem(
        id: "acme.docs",
        text: "Acme Docs",
        target: NavItemTarget.ForHref("https://docs.example.com/acme"),
        icon: "document",
        order: 25);
```

The built-in Overview navigation item uses the reserved ID `overview`. An
extension can replace that sidebar item with `ReplaceNavigationItem`. The
replacement target may be a registered view or a direct href.

```csharp
builder
    .RegisterView<Pages.AcmeOverview>()
    .ReplaceNavigationItem<Pages.AcmeOverview>(
        id: "overview",
        text: "Acme Overview",
        icon: "grid",
        order: 0);
```

Future menu-group and child-item APIs should keep the same shape: stable
contribution IDs, explicit targets, host-owned ordering, permission-aware
visibility, and startup validation. Existing navigation items should be viewed
as the flat form of that future contribution model rather than a competing
system.

## Navigation

Components can still use Blazor's `NavigationManager` directly for ordinary
URL navigation. CloudShell also registers `ICloudShellNavigator` as an
optional helper for product navigation where strongly typed views or stable
view IDs are preferable to scattered route strings.

```razor
@inject ICloudShellNavigator Navigator

<FluentButton OnClick="OpenCluster">Open</FluentButton>

@code {
    private void OpenCluster()
    {
        Navigator.NavigateTo<Pages.AcmeCluster>(
            new { ClusterId = "west-eu" });
    }
}
```

The navigator validates route values against the registered view's `@page`
template. Required route parameters must be supplied, route values are encoded,
and extra values are added as query string parameters. The same behavior is
available by explicit view ID:

```csharp
var href = Navigator.GetHref(
    "acme.cluster",
    new { ClusterId = "west-eu", tab = "logs" });
```

Use direct href navigation when there is no registered view:

```csharp
Navigator.NavigateTo(NavItemTarget.ForHref("https://docs.example.com/acme"));
```

## Cross-Extension Navigation

Use component types whenever the caller intentionally depends on the UI
assembly that owns the view:

```csharp
Navigator.NavigateTo<Pages.AcmeCluster>(
    new { ClusterId = "west-eu" });
```

When another extension should not reference the UI assembly, expose stable
view keys from the extension itself or from a small abstractions package:

```csharp
public static class AcmeViews
{
    public const string Cluster = "acme.cluster";
}
```

If the component type itself is the public contract, use `ShellViewKeys` to
publish the same default key that `RegisterView<TComponent>()` uses:

```csharp
public static class AcmeViews
{
    public static readonly string Cluster = ShellViewKeys.For<Pages.AcmeCluster>();
}
```

If the key should be scoped to the extension rather than only to the component
type, use the namespace-scoped overload:

```csharp
public static class AcmeViews
{
    public static readonly string Cluster = ShellViewKeys.For<Pages.AcmeCluster>("acme.infrastructure");
}
```

Register the view with that key:

```csharp
builder.RegisterView<Pages.AcmeCluster>(AcmeViews.Cluster);
```

Consumers can then navigate by ID while staying decoupled from the component
type:

```csharp
Navigator.NavigateToView(
    AcmeViews.Cluster,
    new { ClusterId = "west-eu" });
```

This gives CloudShell extensions two predictable contracts: strongly typed
navigation for close dependencies, and stable view IDs for cross-extension or
abstractions-only dependencies. Direct href targets remain appropriate for
external links and routes that are not registered as shell views.

## Shell-Hosted Views

Use shell-hosted views for CMS-like integrations that should use CloudShell's
common workspace layout instead of owning an entire routable page. A
shell-hosted view contributes one sidebar navigation item and a set of
extension-owned menu items. The host owns the route, layout, ordering, and
validation; the extension owns the menu item components.

```csharp
builder
    .AddCustomView(
        id: "acme.workspace",
        title: "Acme Workspace",
        route: "/acme/workspace",
        icon: "server",
        order: 55,
        group: "Workspace",
        description: "A hosted integration workspace.")
    .AddCustomViewMenuItem<Pages.AcmeOverview>(
        viewId: "acme.workspace",
        id: "overview",
        title: "Overview",
        order: 10)
    .AddCustomViewMenuItem<Pages.AcmeSettings>(
        viewId: "acme.workspace",
        id: "settings",
        title: "Settings",
        order: 20);
```

Menu items are rendered in the left rail using the same interaction pattern as
resource configuration views. Shell-hosted views use a fragment for the active
menu item because their route is already owned by the hosted view. For example,
`/acme/workspace#settings` opens the Settings item directly.

A minimal workspace view is enough to prove the view contribution path:

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

## Start Page

CloudShell does not require Resource Manager to own the landing experience. An
extension set can choose its own start page with `UseStartView` or
`UseStartRoute`. Prefer `UseStartView` when the start page is a registered
view; it keeps startup configuration stable if the component route changes.

```csharp
builder
    .RegisterView<Pages.AcmeOverview>()
    .UseStartView<Pages.AcmeOverview>();
```

Only one installed extension can configure the start route. This keeps
customization explicit and prevents competing extensions from silently
changing the root experience.

## Future Shell Areas

The planned post-MVP composition model should generalize the layout pattern
that already exists in shell-hosted views and Resource Manager detail tabs,
then add standardized shell areas for:

- Settings pages contributed by extensions but rendered inside a common
  settings surface.
- Notifications contributed by extensions or Control Plane event sources and
  presented through toasts and an off-canvas notification center.
- Named content areas where extensions can add scoped content to an existing
  shell or Resource Manager page without replacing it.

Those areas should use the same principles as views and navigation: stable
extension-owned IDs, host validation, permission-aware visibility, grouped
local navigation where useful, dynamic component hosting, named content areas,
and a clear boundary between UI composition and Control Plane behavior.

See the [shell composition proposal](../proposals/core/shell-composition.md)
for the target direction.
