# Extension authoring

An extension is a Razor class library or .NET class library that references `CloudShell.Abstractions`. UI extensions also reference Fluent UI Blazor.

See [shell customization design goals](shell-customization.md) for the broader product objectives behind these extension points.

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
            .AddCustomView(
                id: "acme.workspace",
                title: "Acme Workspace",
                route: "/acme/workspace",
                icon: "server",
                order: 55,
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
                order: 20)
            .UseStartRoute("/acme/workspace")
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

`AddExtension<T>()` registers the extension as supported and enabled by host
configuration. Use this for programmatic development environments where the
host code defines the active environment.

Shared environments can register an extension as supported but leave activation
to the Extensions UI and the persisted activation store:

```csharp
builder.Services
    .AddCloudShell()
    .AddExtension<CoreShellExtension>()
    .AddExtension<ResourceManagerExtension>()
    .AddSupportedExtension<AcmeExtension>();
```

Supported extensions are disabled until the UI enables them. Host configuration
can also force an extension off:

```csharp
builder.Services
    .AddCloudShell()
    .DisableExtension<AcmeExtension>();
```

Host-enabled and host-disabled extensions cannot be changed from the UI. UI
activation state is persisted by `ICloudShellExtensionActivationStore`; the EF
Core persistence provider stores it in the `ExtensionActivations` table.

## Views

Views are ordinary routable Blazor components in the extension assembly. `AddView<TComponent>` records the component assembly so the host can include it in both Blazor routing and server endpoint mapping.

An extension can contribute multiple views. Set `showInNavigation` to `false` for detail or workflow routes that should not appear in the sidebar.

CloudShell supports two implementation styles for views:

- `AddView<TComponent>` contributes a standalone routable component.
- `AddCustomView` contributes a shell-hosted view with the standard CloudShell layout and extension-owned menu item components.

Both styles are first-class shell views in the product UI. The `CustomView` name is an API-level distinction for adding composed views through the shell host.

## Shell-hosted views

Use shell-hosted views for CMS-like integrations that should use CloudShell's common workspace layout instead of owning an entire routable page. A shell-hosted view contributes one sidebar navigation item and a set of extension-owned menu items. The host owns the route, layout, ordering, and validation; the extension owns the menu item components.

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

Menu items are rendered in the left rail using the same interaction pattern as resource configuration views. The active menu item is stored in the `item` query string, so shell-hosted views can be linked directly, for example `/acme/workspace?item=settings`.

A minimal counter view is enough to prove the view contribution path:

```csharp
builder
    .AddCustomView(
        id: "cloudshell.click-me",
        title: "Click me",
        route: "/click-me",
        icon: "pulse",
        order: 10,
        description: "A simple shell view contributed through the CloudShell extension model.")
    .AddCustomViewMenuItem<ClickMeCounter>(
        viewId: "cloudshell.click-me",
        id: "counter",
        title: "Counter",
        order: 10,
        description: "Click the button to update local component state.");
```

## Start page

CloudShell does not require Resource Manager to own the landing experience. An extension set can choose its own start page with `UseStartRoute`. The configured route must be contributed by an installed extension through `AddView`, `AddCustomView`, or `AddNavigation`.

```csharp
builder.UseStartRoute("/acme/workspace");
```

Only one installed extension can configure the start route. This keeps customization explicit and prevents competing extensions from silently changing the root experience.

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

Use `SupportsStreaming = true` only when the provider can support live tail semantics. Streaming-capable logs are tailed automatically in the Logs view when selected, and users can pause or resume streaming from the log header. The viewer keeps a bounded entry window: it loads the newest page first, appends streamed entries into that window, and fetches older pages only when requested. It follows the latest entry only while the user is already at the latest content; if they scroll back, new entries continue to append without moving their position.

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
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        // Read one bounded page. Use before to page back from the oldest visible entry.
        return Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entry in await ReadLogAsync(logId, initialEntries, cancellationToken: cancellationToken))
        {
            yield return entry;
        }

        await foreach (var entry in TailBackingSystemAsync(logId, cancellationToken))
        {
            yield return entry;
        }
    }
}
```

Streaming implementation guidance:

- Keep `ReadLogAsync` as the bounded snapshot API. It should return recent entries and complete quickly. When `before` is supplied, return the newest entries older than that timestamp so the view can page backward.
- Override `StreamLogAsync` only for descriptors that set `SupportsStreaming = true`.
- Honor cancellation promptly. The Logs view cancels the stream when users pause streaming, select another log, or leave the page.
- Yield parsed `LogEntry` values as complete lines/events arrive. Do not buffer indefinitely.
- Use `initialEntries` to optionally replay recent context before live events. Pass `0` through to the backing system when the caller wants only new entries.
- The control-plane endpoint `GET /api/cloudshell/logs/{logId}/stream?initialEntries=50` streams newline-delimited JSON (`application/x-ndjson`) for API clients.
- The snapshot endpoint `GET /api/cloudshell/logs/{logId}/entries?maxEntries=100&before=...` returns one bounded history page. Use it to load older entries incrementally instead of loading complete logs.

The Docker provider is the reference implementation. Container log descriptors set `SupportsStreaming: true`; `ReadLogAsync` calls Docker logs with `Follow = false` and a bounded `Tail`, while `StreamLogAsync` optionally replays recent entries and then calls Docker logs with `Follow = true` and `Tail = "0"`. Docker stdout and stderr frames are read incrementally with `MultiplexedStream.ReadOutputAsync`, converted into `LogEntry` values, and yielded as each newline is completed.

The Docker reference extension uses this hierarchy:

```text
Local Docker Engine
├── detail route: /resources/docker-engine
└── Docker Container sub-resources
    └── depend on docker:engine
```

Docker discovery runs in a background service and publishes an in-memory resource snapshot. Provider connectivity never blocks shell page rendering.

Docker container sub-resources expose actions from the resource API. Running containers expose Stop, Pause, and Restart. Stopped containers expose Run. Paused containers expose Resume, Stop, and Restart.

Docker can also be declared programmatically in the resource graph. The
declarative API models Docker as the parent resource and containers as
sub-resources:

```csharp
controlPlane.Resources(resources =>
{
    var docker = resources.AddDocker("docker:dev", "Development Docker");

    var redis = docker
        .AddContainer("redis", "redis", "7.2")
        .DependsOn("configuration:settings");
});
```

`AddDocker()` declares the default local Docker Engine. `AddDocker(id, name)`
allows more than one Docker parent to be modeled. Containers created from a
Docker builder are parented to that specific Docker resource, while
`DependsOn(...)` records normal resource graph dependencies.

Parent-child resource relationships are distinct from dependency relationships:
the parent controls containment in Resource Manager, while `DependsOn(...)`
records topology or ordering between any two resources.

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
- shell-hosted view IDs must be unique
- shell-hosted view menu item IDs must be unique within each hosted view
- at most one extension can configure the shell start route
- the shell start route must point at a route contributed by an installed extension
- consumed capabilities must be provided by an installed extension

Invalid extension sets fail during startup with a concrete error.

## Trust model

The current model loads extensions in-process. Extensions can register services and execute arbitrary .NET code, so only trusted extensions should be installed.

Untrusted or independently deployed integrations should eventually use an out-of-process provider protocol over HTTP or gRPC while retaining the same resource contracts at the host boundary.
