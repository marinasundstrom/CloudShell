# Extension Authoring

An extension is a Razor class library or .NET class library that references
`CloudShell.Abstractions`. UI extensions also reference Fluent UI Blazor.

CloudShell has separate extension surfaces for the shell UI and for
Control Plane or Resource Manager behavior. They are logically related for
most user-facing resource providers, but they target different apps. The
CloudShell UI and the Control Plane may be hosted together in one ASP.NET Core
process during development, or they may run as separate deployable services.

See [shell customization design goals](shell-customization.md) for the broader
product objectives behind these extension points.

## Specification Docs

| Area | Use |
| --- | --- |
| [UI extensions](extensions/ui.md) | Shell views, sidebar navigation, shell-hosted views, cross-extension navigation, and start page customization. |
| [Control Plane and Resource Manager extensions](extensions/control-plane-resource-manager.md) | Resource providers, resource type registration UI, programmatic resource factories, resource actions, Resource Manager tabs/detail routes, logs, and resource provider examples. |

## Boundary

Control Plane resource provider registration is not the same thing as
CloudShell UI registration.

A Control Plane provider contributes resource behavior: projection, creation,
procedure execution, logs, templates, provider-owned runtime state, and
provider-owned configuration. Resource Manager and shell UI contributions
contribute presentation: Add Resource forms, update components, tabs, detail
routes, UI actions, navigation, and custom views.

Most user-facing providers should ship both surfaces together. A provider that
only registers Control Plane behavior is programmatic-only unless another UI
extension contributes the Resource Manager experience. That may be intentional,
but it should be a product decision rather than an accidental omission.

## Entry Point

Implement one `ICloudShellExtension`:

```csharp
public sealed class AcmeExtension : ICloudShellExtension
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
                order: 51)
            .AddScoped<IAcmeClient, AcmeClient>();
    }
}
```

Expose a package-level registration method:

```csharp
public static class AcmeCloudShellExtensions
{
    public static ICloudShellBuilder AddAcme(
        this ICloudShellBuilder builder) =>
        builder.AddExtension<AcmeExtension>();
}
```

The host installs the extension through DI:

```csharp
builder
    .AddCloudShell()
    .AddAcme();
```

## Activation

`AddExtension<T>()` registers the extension as supported and enabled by host
configuration. Use this for programmatic development environments where the
host code defines the active environment.

Shared environments can register an extension as supported but leave activation
to the Extensions UI and the persisted activation store:

```csharp
builder
    .AddCloudShell()
    .AddSupportedExtension<AcmeExtension>();
```

Supported extensions are disabled until the UI enables them. Host
configuration can also force an extension off:

```csharp
builder
    .AddCloudShell()
    .DisableExtension<AcmeExtension>();
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
