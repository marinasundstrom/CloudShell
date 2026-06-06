# Hosting Model

The shell supports two hosting shapes.

## Combined Host

For local development, the UI and Control Plane can run in the same ASP.NET Core
process. This is the default shape used by `CloudShell.Host`.

```csharp
var controlPlane = builder.Services
    .AddControlPlane()
    .AddExtension<CoreShellExtension>()
    .AddExtension<ResourceManagerExtension>()
    .AddConfigurationProvider()
    .AddApplicationProvider()
    .AddDockerProvider();

controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:example", "Example Configuration")
        .WithEntry("SampleMessage", "Hello from checked-in configuration");
});
```

In this mode, the UI directly consumes the same in-process Control Plane
services that own resource providers, registrations, groups, logs, templates,
and resource procedures.

The default host models each configuration service instance as its own
executable application resource. Configuration stores remain individual
`configuration.store` resources, and each one depends on its paired application
resource. The provider remains responsible for store definitions and Resource
Manager integration.

## Split Host

For shared on-premise environments, the UI and Control Plane can be hosted
separately.

The Control Plane host owns:

- Resource providers.
- Resource declarations.
- Provider-owned resource configuration.
- Resource registrations, groups, and dependencies.
- Resource procedures and logs.
- The versioned Control Plane API.

The UI host owns:

- Blazor shell rendering.
- Navigation and layout.
- Calls to the remote Control Plane API.
- Authentication challenge UX when required by the deployment.

Declarative resources must be configured in the Control Plane host:

```csharp
var controlPlane = builder.Services
    .AddControlPlane()
    .AddConfigurationProvider();

controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:shared", "Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true");
});
```

The UI host should not declare resources. It should discover resources through
the Control Plane API so one shared Control Plane remains the authority for
checked-in configuration, persisted state, provider actions, and authorization.

## Persistence

Programmatic declarations are startup configuration by default. Calling
`Persist()` tells the owning provider to apply the resource through the same
setup path used by the UI. Existing persisted state is left unchanged unless the
declaration uses `Persist(overwrite: true)`.

See [Programmatic resources](programmatic-resources.md) for the declaration API.
