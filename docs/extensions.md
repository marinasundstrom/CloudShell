# Extension authoring

An extension is a Razor class library or .NET class library that references `CloudShell.Abstractions`. UI extensions also reference Fluent UI Blazor.

## Entry point

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
            .AddResourceType<Pages.RegisterAcmeCluster>(
                id: "acme.cluster",
                displayName: "Acme Cluster",
                description: "Register an Acme cluster as a CloudShell resource.",
                icon: "server",
                order: 50)
            .AddView<Pages.AcmeDashboard>(
                title: "Acme",
                route: "/acme",
                icon: "server",
                order: 50,
                showInNavigation: false)
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
builder.Services
    .AddCloudShell()
    .AddExtension<CoreShellExtension>()
    .AddExtension<ResourceManagerExtension>()
    .AddAcme();
```

## Views

Views are ordinary routable Blazor components in the extension assembly. `AddView<TComponent>` records the component assembly so the host can include it in both Blazor routing and server endpoint mapping.

An extension can contribute multiple views. Set `showInNavigation` to `false` for detail or workflow routes that should not appear in the sidebar.

## Resource types

Resource types are the user-facing contract for adding resources. An extension registers each type with `AddResourceType<TRegistrationComponent>`.

The registration component is rendered inside the common Add Resource page after the user chooses the type from a dropdown. It owns the type-specific form, validation, discovery hints, and the call to `IResourceRegistrationStore.RegisterAsync`.

Resource providers are not shown as a product concept in the UI. They are implementation services that resource types use to map external systems into CloudShell resources.

## Internal resource providers

Implement `IResourceProvider` to contribute discovered resource data:

- resources
- lifecycle state
- dependencies
- named endpoints

Providers are aggregated by Resource Manager. Other extensions consume `IResourceManagerStore`; they do not depend on provider implementations.

Resources can set `DetailRoute` to link to an extension-owned view. This supports the familiar cloud-portal pattern where a resource opens its own operational workspace.

Root resources are persisted registrations. Discovered resources stay hidden until the user explicitly adds one through a resource type registration UI. Descendants of a registered root can appear dynamically as sub-resources.

Resource groups are user-managed project boundaries owned by the platform, not by providers. A root resource can be assigned to a group during registration, and its sub-resources inherit that group for filtering and display.

The Docker reference extension uses this hierarchy:

```text
Local Docker Engine
├── detail route: /resources/docker-engine
└── Docker Container sub-resources
    └── depend on docker:engine
```

Docker discovery runs in a background service and publishes an in-memory resource snapshot. Provider connectivity never blocks shell page rendering.

The Docker endpoint is discovered from:

1. An endpoint configured through `AddDockerProvider`.
2. The `DOCKER_HOST` environment variable.
3. Docker Desktop's user socket.
4. A rootless Docker runtime socket.
5. `/var/run/docker.sock`.

```csharp
builder.Services
    .AddCloudShell()
    .AddDockerProvider(options =>
    {
        options.Endpoint = new Uri("unix:///var/run/docker.sock");
        options.RefreshInterval = TimeSpan.FromSeconds(15);
    });
```

## Validation

CloudShell validates extension registrations at startup:

- extension IDs must be unique
- view routes must be unique
- consumed capabilities must be provided by an installed extension

Invalid extension sets fail during startup with a concrete error.

## Trust model

The current model loads extensions in-process. Extensions can register services and execute arbitrary .NET code, so only trusted extensions should be installed.

Untrusted or independently deployed integrations should eventually use an out-of-process provider protocol over HTTP or gRPC while retaining the same resource contracts at the host boundary.
