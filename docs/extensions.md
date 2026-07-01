# Extension Authoring

An extension is a Razor class library or .NET class library that references
`CloudShell.Abstractions`. UI extensions also reference Fluent UI Blazor.

CloudShell has separate extension surfaces for the shell UI and for
Control Plane or Resource Manager behavior. They are logically related for
most user-facing resource providers, but they target different apps. The
CloudShell UI and the Control Plane may be hosted together in one ASP.NET Core
process during development, or they may run as separate deployable services.

Resource Manager is itself a CloudShell integration. It is built-in and
central to the current product, but it should be treated as a peer of other
CloudShell extensions at the shell boundary rather than as the shell itself.
Resource Manager has a shell extension that plugs into CloudShell UI. The
backend for that shell extension is the Control Plane service. Resource
Manager then exposes its own extension model for resource providers,
resource-specific UI, resource actions, templates, and provider-backed
operational data.

Use the precise terms when the boundary matters. "Resource Manager shell
extension" means the CloudShell UI integration. "Resource Manager backend" or
"Control Plane side" means the backend services. "Resource Manager" by itself
may describe the whole product area or capability package that includes both
halves.

A CloudShell capability package is the product/package boundary that usually
groups those pieces. For example, a Docker capability package can include a
Control Plane provider, resource type definitions, declaration helpers, logs,
and Resource Manager UI. An identity capability package can be cross-cutting
and contribute provider behavior, permissions, diagnostics, shell views, and
SDK helpers. The extension is the in-process registration mechanism; the
capability package is the installable environment capability. The intended
distribution model is NuGet: a capability package can ship one or more
assemblies that expose `ICloudShellExtension` implementations, host
registration methods, UI components, provider services, and client helpers.

See [shell customization design goals](shell-customization.md),
[current UI composition](ui-composition.md), and the
[shell composition proposal](proposals/core/shell-composition.md) for the
broader product objectives behind these extension points.

## Specification Docs

| Area | Use |
| --- | --- |
| [UI extension architecture](extensions/ui.md) | Base shell UI extension model: shell views, sidebar navigation, shell-hosted views, cross-extension navigation, and start page customization. Future shell composition extends this into menu groups, child items, settings pages, notifications, and named content areas. |
| [UI composition](ui-composition.md) | Current experimental composition libraries and standalone Blazor sample: typed IDs, registry, menu/link/title/section components, normal Blazor routing, and current limitations. |
| [Resource Manager UI extensions](extensions/resource-manager-ui.md) | Resource-specific UI built on the base UI extension architecture: Add Resource forms, update components, generated details, tabs, detail routes, and resource UI actions. |
| [Control Plane resource providers](extensions/control-plane-resource-providers.md) | Non-UI resource-provider extensions: resource projection, creation, procedures, logs, templates, programmatic declarations, and provider examples. |
| [Resource provider integration workflow](extensions/control-plane-resource-manager.md) | How user-facing resource providers pair Control Plane behavior with Resource Manager UI integration. |

## Boundary

Control Plane resource provider registration is not the same thing as
CloudShell UI registration.

The base UI extension architecture is the shell-level Blazor contribution
model. Resource Manager UI extensions build on that model for resource-specific
presentation. Control Plane resource-provider extensions are non-UI services
that project and operate resources.

This creates two extension layers. A CloudShell extension can contribute a
top-level product area such as Resource Manager, observability, or an
extension-owned workspace. Resource Manager, as one of those product areas,
can also accept resource-provider contributions through Resource Manager
contracts that span UI and Control Plane behavior.
In this model, a resource provider is not extending the CloudShell shell
directly when it contributes a resource type or resource detail view. It is
extending Resource Manager, and Resource Manager projects the appropriate UI
side into CloudShell UI while using the Control Plane as its backend.

A Control Plane provider contributes resource behavior: projection, creation,
procedure execution, logs, templates, provider-owned runtime state, and
provider-owned configuration. Resource Manager UI contributions contribute
resource presentation: Add Resource forms, update components, tabs, detail
routes, and UI actions. Generic shell UI contributions contribute navigation,
custom views, and shell-hosted workspaces.

Most user-facing providers should ship both surfaces together. A provider that
only registers Control Plane behavior is programmatic-only unless another UI
extension contributes the Resource Manager experience. That may be intentional,
but it should be a product decision rather than an accidental omission.

When implementing any resource provider or other Control Plane extension,
consider the matching CloudShell UI integration. It is not technically required
for hosts that do not use CloudShell UI, but if the feature is intended for
interactive users, the Resource Manager UI integration is part of the expected
product surface.

For split hosting, register the capability package's Control Plane side in the
Control Plane host and the package's UI side in the CloudShell UI host. UI
integrations should consume their product area's public managers and client
adapters. For Resource Manager UI, that means Resource Manager/Control Plane
abstractions such as `IControlPlane` or `IResourceManager`, which can be backed
by an in-process implementation in a combined host or a remote adapter in a
split host. The core CloudShell UI shell should only know that Resource
Manager contributed shell surfaces; it should not own Control Plane behavior
or resource-management backend state.

UI extensions do not have to use the same component stack everywhere. They can
render extension-owned pages or tools with their own presenters when they own
that surface. Shell-owned areas are different: menus, pages, sections,
settings, notifications, and other CMS-like shell surfaces should be integrated
through CloudShell extension services and public contracts. A concrete shell
implementation then renders those contributions with its chosen presenters.
This lets an extension call a layout, notification, or composition service as a
normal integration point without depending on `CloudShell.Hosting` internals or
the Fluent UI presenters used by the built-in shell.

## Entry Point

Implement extension entry points for the surfaces the package supports. A
package can expose both a Control Plane extension and a CloudShell UI
extension, but their registrations should stay separate:

```csharp
public sealed class AcmeControlPlaneExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        Id: "acme.infrastructure",
        DisplayName: "Acme Infrastructure",
        Description: "Tools for Acme's on-premise environment.",
        Version: "1.0.0",
        Provides: ["acme.resources"],
        Consumes: ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder
            .AddResourceProvider<AcmeResourceProvider>()
            .AddLogProvider<AcmeLogProvider>()
            .AddScoped<IAcmeClient, AcmeClient>();
    }
}

public sealed class AcmeUiExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        Id: "acme.infrastructure.ui",
        DisplayName: "Acme Infrastructure UI",
        Description: "CloudShell UI integration for Acme infrastructure.",
        Version: "1.0.0",
        Provides: ["acme.resources.ui"],
        Consumes: ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.AddCompositionModule(
            CompositionModuleId.Create("acme.infrastructure"),
            composition =>
            {
                composition.AddPage(
                    PageId.Create("acme-dashboard"),
                    "Acme Dashboard",
                    "/acme-dashboard");
            });

        builder
            .AddResourceType<Pages.RegisterAcmeCluster>(
                id: "acme.cluster",
                displayName: "Acme Cluster",
                description: "Register an Acme cluster as a CloudShell resource.",
                icon: "server",
                order: 50)
            .RegisterView<Pages.AcmeDashboard>()
            .AddNavigationItem<Pages.AcmeDashboard>(
                text: "Acme Dashboard",
                icon: "grid",
                order: 51);
    }
}
```

Use `builder.AddCompositionModule(...)` for new shell pages, menu items,
settings sections, or other composition-backed UI contributions. The older
navigation APIs remain for compatibility while existing extensions migrate to
composition-native menu and page registrations.

Expose a package-level registration method:

```csharp
public static class AcmeCloudShellExtensions
{
    public static IControlPlaneBuilder AddAcmeBackend(
        this IControlPlaneBuilder builder) =>
        builder.AddExtension<AcmeControlPlaneExtension>();

    public static ICloudShellBuilder AddAcmeUi(
        this ICloudShellBuilder builder) =>
        builder.AddExtension<AcmeUiExtension>();
}
```

The host installs the extension through DI:

```csharp
builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.AddAcmeBackend();
    });

builder.AddCloudShellUi(ui =>
{
    ui.AddAcmeUi();
});
```

In the intended NuGet distribution flow, the host references the package and
calls its registration method in the appropriate host application. Split
hosting may require referencing the package in both the Control Plane host and
the CloudShell UI host, or referencing separate provider/UI packages when the
capability is split across assemblies.

## Activation

`AddExtension<T>()` registers the extension as supported and enabled by host
configuration. Use this for programmatic development environments where the
host code defines the active environment.

Shared environments can register an extension as supported but leave activation
to the Extensions UI and the persisted activation store:

```csharp
builder.AddCloudShellUi(ui =>
{
    ui.AddSupportedExtension<AcmeExtension>();
});
```

Supported extensions are disabled until the UI enables them. Host
configuration can also force an extension off:

```csharp
builder.AddCloudShellUi(ui =>
{
    ui.DisableExtension<AcmeExtension>();
});
```

Host-enabled and host-disabled extensions cannot be changed from the UI. UI
activation state is persisted by `ICloudShellExtensionActivationStore`; the EF
Core persistence provider stores it in the `ExtensionActivations` table.

## Validation

CloudShell validates extension registrations at startup:

- extension IDs must be unique
- view routes must be unique
- shell-hosted view IDs must be unique
- shell-hosted view menu item IDs must be unique within each hosted view
- at most one extension can configure the shell start route
- the shell start route must point at a route contributed by an installed
  extension
- consumed capabilities must be provided by an installed extension

Invalid extension sets fail during startup with a concrete error.

## Trust Model

The current model loads extensions in-process. Extensions can register services
and execute arbitrary .NET code, so only trusted extensions should be
installed.

Untrusted or independently deployed integrations should eventually use an
out-of-process provider protocol over HTTP or gRPC while retaining the same
resource contracts at the host boundary.
