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
            .AddLogProvider<AcmeLogProvider>()
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
- resource-bound actions
- dependencies
- named endpoints

Providers are aggregated by Resource Manager. Other extensions consume `IResourceManagerStore`; they do not depend on provider implementations.

Resources can set `DetailRoute` to link to an extension-owned view. This supports the familiar cloud-portal pattern where a resource opens its own operational workspace.

Resources can also expose actions through `CloudResource.ResourceActions`. These actions belong to the resource instance, not only to the resource type, so providers can vary commands by state or capability. Use the standard `ResourceActionKind` values for Run, Stop, Pause, and Restart when a command controls lifecycle. Use `ResourceActionKind.Custom` with a stable action ID for provider-specific commands.

`ResourceAction` is the command contract. `ResourceActionPresentation` is the UI policy for that command, including whether the action should be shown inline or in overflow, which icon to use, and whether the UI should ask before invoking it. Providers still execute the action normally when `ExecuteActionAsync` is called; confirmation is not part of provider execution.

Providers that support actions implement `IResourceProcedureProvider.ExecuteActionAsync`. The Resource Manager passes the selected `CloudResource` and `ResourceAction` back to the provider, letting the provider execute the command against the underlying system.

```csharp
return new CloudResource(
    Id: "acme:worker:orders",
    Name: "orders",
    Kind: "Acme Worker",
    Provider: "Acme",
    Region: "local",
    State: ResourceState.Running,
    Endpoints: [],
    Version: "1.0.0",
    LastUpdated: DateTimeOffset.UtcNow,
    DependsOn: [],
    Actions:
    [
        ResourceAction.Stop,
        ResourceAction.Restart,
        new ResourceAction(
            "acme.drain",
            "Drain",
            Presentation: new ResourceActionPresentation(
                ResourceActionDisplayStyle.Overflow,
                ResourceActionIcon.Custom,
                RequiresConfirmation: true))
    ]);
```

Root resources are persisted registrations. Discovered resources stay hidden until the user explicitly adds one through a resource type registration UI. Descendants of a registered root can appear dynamically as sub-resources.

Resource groups are user-managed project boundaries owned by the platform, not by providers. A root resource can be assigned to a group during registration, and its sub-resources inherit that group for filtering and display.

## Log providers

Logs are first-class services registered independently of resources. Implement `ILogProvider` when an extension can expose logs for resources, providers, or extension-owned artifacts, then register it with `AddLogProvider<TProvider>()`.

Each provider returns `LogDescriptor` values. A descriptor can point at a resource through `ResourceId`, an extension artifact through `ArtifactId`, or a provider-owned source through `SourceKind`. A single resource can have multiple logs, and multiple providers can expose logs for the same resource. The Resource Manager shows a log shortcut for resources with matching descriptors, and the Logs view opens resource-scoped log lists through `/logs?resourceId=...`.

Use `SupportsStreaming = true` only when the provider can support live tail semantics. The current Logs view reads recent entries through `ReadLogAsync`; streaming-capable logs are marked in the UI so the provider capability is visible before live tail controls are added.

```csharp
public sealed class AcmeLogProvider : ILogProvider
{
    public string Id => "acme";

    public string DisplayName => "Acme";

    public IReadOnlyList<LogDescriptor> GetLogs() =>
    [
        new LogDescriptor(
            Id: "acme:worker:orders:stdout",
            Name: "stdout",
            Provider: DisplayName,
            SourceName: "orders",
            SourceKind: LogSourceKind.Resource,
            ResourceId: "acme:worker:orders",
            SupportsStreaming: true)
    ];

    public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        CancellationToken cancellationToken = default)
    {
        // Read from the backing system and return newest entries.
        return Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }
}
```

The Docker reference extension uses this hierarchy:

```text
Local Docker Engine
├── detail route: /resources/docker-engine
└── Docker Container sub-resources
    └── depend on docker:engine
```

Docker discovery runs in a background service and publishes an in-memory resource snapshot. Provider connectivity never blocks shell page rendering.

Docker container sub-resources expose actions from the resource API. Running containers expose Stop, Pause, and Restart. Stopped containers expose Run. Paused containers expose Resume, Stop, and Restart.

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
