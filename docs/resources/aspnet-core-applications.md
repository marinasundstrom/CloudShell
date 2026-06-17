# ASP.NET Core Applications

Use the ASP.NET Core project resource type for local .NET web projects. These
resources project as `application.aspnet-core-project` and expose project-shaped
attributes such as project path, application arguments, and hot reload mode.

ASP.NET Core project resources are not plain executable application resources.
They are project resources with a provider-owned process runner. Resource
Manager shows the project shape, while the provider hides the generated
`dotnet run` or `dotnet watch` command used to host the project.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [Executable applications](executable-applications.md) and
[Container apps](container-apps.md).

## Declaration

Programmatic declarations use `AddAspNetCoreProject(...)` with a scoped
resource name. The provider derives the canonical resource ID from that name.
Apply an optional display label with `.WithDisplayName(...)` when it helps the
local development experience:

```csharp
resources
    .AddAspNetCoreProject(
        "example-web-api",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .WithDisplayName("Example Web API")
    .WithReference(configuration);
```

By default, CloudShell starts ASP.NET Core project resources with plain
`dotnet run`:

```bash
dotnet run --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --no-launch-profile
```

Pass `hotReload: true` to opt into `dotnet watch`. When hot reload is enabled,
CloudShell starts watch mode with `--non-interactive` and sets
`DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true`, so rude edits restart the app instead
of leaving the hosted process blocked on the watch prompt. Pass
`applicationArguments` when the hosted app should receive command-line
arguments. CloudShell appends those arguments after the hidden `dotnet` runner
arguments.

Use `AsContainer(...)` when the project should be modeled as a container app
instead of a process-backed ASP.NET Core project:

```csharp
resources
    .AddAspNetCoreProject(
        "example-web-api",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .WithDisplayName("Example Web API")
    .AsContainer(registry: "registry.local:5000")
    .WithContainerHost("docker:dev");
```

`AsContainer(...)` converts the projected resource to
`application.container-app` while keeping the project path in the workload
descriptor. When no Dockerfile is supplied, the default local runner publishes
the project through the .NET SDK container publisher
(`dotnet publish /t:PublishContainer`) before running the image. When the
project owns a Dockerfile, pass it as `dockerfile: "Dockerfile"` and the
selected container host uses the Dockerfile build path.

## Endpoints

ASP.NET Core project endpoint sources are resolved in this order:

1. Programmatic endpoints declared with `endpoint`, `WithEndpoint(...)`,
   `WithHttpEndpoint(...)`, `WithHttpsEndpoint(...)`, or
   `WithEndpointPort(...)`.
2. `Properties/launchSettings.json` only when the declaration explicitly calls
   `WithLaunchSettingsEndpoints()`.
3. The ASP.NET project provider default: a stable local HTTP endpoint.

Explicit endpoint declarations always win. If endpoints are declared manually,
CloudShell ignores launch settings even when launch-settings endpoint loading
was enabled earlier in the builder chain. Resource Manager create and update
flows use the shared endpoint assignment pattern: the user can let CloudShell
assign the local mapping or specify a fixed local port for convenience. These
flows do not ask for a raw endpoint URI and do not read `launchSettings.json`.
The fixed-port option is a local-development affordance for callers on the
developer machine. In managed or on-premise environments, Resource Manager
should favor network placement, internal DNS names, and explicit public
exposure instead of asking users to choose host ports. If a future UI exposes
launch-settings endpoint loading, that option should be disabled whenever
explicit endpoints are configured.

Resource Manager create and update forms also expose the selected network and,
when manual assignment is selected, an optional manual host/address. The local
development default remains **Local network** with provider assignment, but the
same UI shape is ready for virtual networks and private DNS/name-mapping
policies.

Provider defaults are local development bindings, not a general exposure
mechanism. Public or broader resource exposure should be declared explicitly by
the resource author or operator.

When the project process starts, CloudShell injects the projected
endpoint-network mapping address into `ASPNETCORE_URLS`, so the project listens
on the address that the selected topology assigned. If the resource has not
been projected through Resource Manager yet, the provider falls back to the
same local endpoint mapping calculation used for projection.

Use endpoint builder methods to model additional or named endpoints:

```csharp
resources
    .AddAspNetCoreProject(
        "example-web-api",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
        applicationArguments: "--seed")
    .WithDisplayName("Example Web API")
    .WithHttpEndpoint(port: 5127)
    .WithEndpointPort("dashboard", targetPort: 18888, port: 18888, protocol: "http");
```

Fixed endpoint URIs and fixed helper ports are stored as manual endpoint
assignments in the implied local network. Calling `WithHttpEndpoint()` or
`WithEndpointPort(...)` without a fixed port stores an explicit auto assignment,
so the provider can choose the local address while the project keeps the
declared service port.

Use launch settings only when the project declaration should intentionally take
its local endpoint shape from the ASP.NET Core development profile:

```csharp
resources
    .AddAspNetCoreProject(
        "example-web-api",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .WithDisplayName("Example Web API")
    .WithLaunchSettingsEndpoints();
```

## Service Discovery

ASP.NET Core project references follow the Aspire model:
`WithReference(...)` adds service discovery configuration for the referenced
resource and enables the service discovery environment variable mapping. A
client can use logical URIs such as `https+http://example-web-api` or
`https+http://_dashboard.example-web-api` when service discovery is enabled in
the consuming app.
