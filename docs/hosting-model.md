# Hosting Model

CloudShell supports three registration shapes:

- UI only: host the CloudShell shell and custom UI extensions.
- Control Plane only: host resource providers, stores, declarations, and APIs.
- Combined: host both in one ASP.NET Core process.

The explicit UI and Control Plane registrations are useful for split
deployments. Combined hosts compose those same registrations in one process.

The reusable shell UI and hosting helpers live in `CloudShell.Hosting`, a Razor
class library that references ASP.NET Core. Web SDK projects reference
`CloudShell.Hosting` and keep their own `Program.cs`, appsettings, environment,
and scenario-specific extension registrations.

## UI-Only Host

Use the UI-only host when an application wants CloudShell navigation, layout,
localization, authentication plumbing, and extension-provided views without
hosting Control Plane stores or APIs.

```csharp
using CloudShell.Hosting;
using CloudShell.Hosting.Components;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddCloudShellUi()
    .AddExtension<SampleWorkspaceExtension>();

var app = builder.Build();

await app.UseCloudShellUiAsync();
app.MapCloudShellUi<App>();

app.Run();
```

In this shape, extension views can contribute routes and navigation through the
same extension model as a full host. Resource Manager and Control Plane
endpoints are not registered unless the host adds them separately.

See `samples/CloudShell.UiExtensionHost`.

## Combined Host

For local development, the UI and Control Plane can run in the same ASP.NET Core
process. This is the shape used by the `CloudShell.Host` development sample.

```csharp
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;

var builder = WebApplication.CreateBuilder(args);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddConfigurationProvider()
    .AddApplicationProvider()
    .AddDockerProvider();

cloudShell.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:example", "Example Configuration")
        .WithEntry("SampleMessage", "Hello from checked-in configuration");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
```

In this mode, the UI consumes the same `IControlPlane` abstraction as a split
host, but the registered implementation is in-process and backed by Control
Plane services.

The default host models each configuration service instance as an individual
`configuration.store` resource. The configuration provider owns the local
runtime process and exposes resource logs directly, while still keeping store
definitions and Resource Manager integration under the configuration resource.

See `samples/CloudShell.ResourceHost`.

## Control-Plane-Only Host

Use the Control Plane-only registration when APIs, resource providers, and
persisted state should run separately from the UI.

```csharp
var builder = WebApplication.CreateBuilder(args);

var controlPlane = builder
    .AddCloudShellControlPlane()
    .AddConfigurationProvider();

controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:shared", "Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
```

The Control Plane host owns:

- Resource providers.
- Resource declarations.
- Provider-owned resource configuration.
- Resource registrations, groups, and dependencies.
- Resource procedures and logs.
- The versioned Control Plane API.

## Split Host

For shared on-premise environments, the UI and Control Plane can be hosted
separately.

The UI host owns:

- Blazor shell rendering.
- Navigation and layout.
- An `IControlPlane` implementation backed by the remote Control
  Plane API.
- Authentication challenge UX when required by the deployment.

```csharp
builder.Services.AddRemoteControlPlane(options =>
{
    options.BaseAddress = new Uri("https://control-plane.example.com");
});
```

Declarative resources must be configured in the Control Plane host:

```csharp
var controlPlane = builder
    .AddCloudShellControlPlane()
    .AddConfigurationProvider();

controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:shared", "Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true");
});
```

The UI host should not declare resources. It should discover resources through
`IControlPlane` so one shared Control Plane remains the authority for
checked-in configuration, persisted state, provider actions, and authorization.

## Persistence

Programmatic declarations are startup configuration by default. Calling
`Persist()` tells the owning provider to apply the resource through the same
setup path used by the UI. Existing persisted state is left unchanged unless the
declaration uses `Persist(overwrite: true)`.

See [Programmatic resources](programmatic-resources.md) for the declaration API.
