# .NET Applications

Use the .NET app resource type for .NET web projects and published assemblies.
These resources project as `application.dotnet-app`. Local-source declarations
can use project mode through `project.path` or executable mode through the flat
`executablePath` attribute. Resource Manager-created .NET app resources should
use application artifact mode instead, where the provider finds the entry
assembly inside the host-managed resource artifact folder by artifact layout
convention.

.NET app resources are not plain executable application resources. They are
.NET-specific application resources with a provider-owned process runner. The
provider hides the generated `dotnet build`, `dotnet run`, `dotnet watch`, or
`dotnet <assembly>` command used to host the app.

`project.path` and `executablePath` are local-development source inputs. They
should be authored by launchers, graph builders, local-development host
profiles, or explicitly trusted host-path automation. A remote CloudShell host
must not let ordinary Resource Manager users set those attributes through the
browser UI, because the path is resolved on the runtime host. Hosted Resource
Manager create and edit flows use application artifacts instead: upload a
published package today, or later configure a provider-supported source that
the host downloads or pulls into the resource artifact folder.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [Executable applications](executable-applications.md) and
[Container apps](container-apps.md).

## Declaration

Project-backed programmatic declarations usually use `AddDotnetProject(...)`
with a scoped resource name. The provider derives the canonical resource ID
from that name. Apply an optional display label with `.WithDisplayName(...)`
when it helps the local development experience:

```csharp
resources
    .AddDotnetProject(
        "example-web-api",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .WithDisplayName("Example Web API")
    .WithReference(configuration);
```

`AddDotnetApp(...)` is the broader fluent entrypoint for the same
`application.dotnet-app` resource type. Use it when code needs to choose the
source mode through fluent configuration:

```csharp
resources
    .AddDotnetApp("example-web-api")
    .WithProjectPath("samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj");
```

Executable mode points at a host-readable assembly or executable path and
starts it with `dotnet <path>`:

```csharp
resources
    .AddDotnetApp("example-web-api")
    .WithExecutablePath("publish/CloudShell.ExampleWebApi.dll")
    .WithArguments("--urls http://localhost:5080");
```

This executable path is only for local-source mode. In artifact mode, the .NET
provider validates the uploaded or downloaded artifact layout and chooses the
entry assembly from the contents of the resource artifact folder.

By default, CloudShell serializes .NET app builds before launch and
then starts each project with `dotnet run --no-build`:

```bash
dotnet build samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --nologo
dotnet run --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --no-build --no-launch-profile
```

Before Start or Restart dispatches, Resource Manager verifies that the
project path exists. Relative project paths are resolved against the resource
working directory when one is configured, otherwise against the CloudShell host
content root. Missing project paths are reported as action-unavailable reasons
instead of failing later during `dotnet build`.
The configured working directory must also exist before Start or Restart can
dispatch.

Lifecycle actions also require a .NET app runtime controller. The built-in
provider registration supplies the local process runtime controller for normal
hosts. If a custom or direct operation path is constructed without that
controller, Resource Manager projects lifecycle actions as unavailable with a
missing-controller reason, and direct provider-execution calls return the same
readiness failure as a diagnostic instead of succeeding as a no-op.

Pass `hotReload: true` to opt into `dotnet watch`. When hot reload is enabled,
CloudShell starts watch mode with `--non-interactive` and sets
`DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true`, so rude edits restart the app instead
of leaving the hosted process blocked on the watch prompt. Pass
`applicationArguments` when the hosted app should receive command-line
arguments. CloudShell appends those arguments after the hidden `dotnet` runner
arguments.

Use `AsContainerApp(...)` when the project should be modeled as a container app
instead of a process-backed .NET app:

```csharp
resources
    .AddDotnetProject(
        "example-web-api",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .WithDisplayName("Example Web API")
    .AsContainerApp(registry: "registry.local:5000")
    .WithContainerHost("docker:dev");
```

`AsContainerApp(...)` converts the projected resource to
`application.container-app` while keeping the project path in the workload
descriptor. When no Dockerfile is supplied, the default local runner publishes
the project through the .NET SDK container publisher
(`dotnet publish /t:PublishContainer`) before running the image. When the
project owns a Dockerfile, pass it as `dockerfile: "Dockerfile"` and the
selected container host uses the Dockerfile build path. Pass `tag: "..."` for
samples or deployment flows that need a predictable project-container image
tag.

## Endpoints

.NET app endpoint sources are resolved in this order:

1. Programmatic endpoints declared with `endpoint`, `WithEndpoint(...)`,
   `WithHttpEndpoint(...)`, `WithHttpsEndpoint(...)`, or
   `WithEndpointPort(...)`.
2. `Properties/launchSettings.json` only when the declaration explicitly calls
   `WithLaunchSettingsEndpoints()`.
3. The ASP.NET project provider default: a stable local HTTP endpoint.

The third case covers the common local development case where the project needs
an HTTP endpoint but the caller does not care which concrete address Resource
Manager or the local network provider assigns. It is an .NET app
provider rule, not a generic rule for all resource types.

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
development default remains **Host network** with provider assignment, but the
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
    .AddDotnetProject(
        "example-web-api",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
        applicationArguments: "--seed")
    .WithDisplayName("Example Web API")
    .WithHttpEndpoint(port: 5127)
    .WithEndpointPort("dashboard", targetPort: 18888, port: 18888, protocol: "http");
```

Fixed endpoint URIs and fixed helper ports are stored as manual endpoint
assignments in the Host network. Calling `WithHttpEndpoint()` or
`WithEndpointPort(...)` without a fixed port stores an explicit auto assignment,
so the provider can choose the local address while the project keeps the
declared service port.

Use launch settings only when the project declaration should intentionally take
its local endpoint shape from the ASP.NET Core development profile:

```csharp
resources
    .AddDotnetProject(
        "example-web-api",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .WithDisplayName("Example Web API")
    .WithLaunchSettingsEndpoints();
```

## Service Discovery

.NET app references follow the Aspire model:
`WithReference(...)` adds service discovery configuration for the referenced
resource and enables the service discovery environment variable mapping. A
client can use logical URIs such as `https+http://example-web-api` or
`https+http://_dashboard.example-web-api` when service discovery is enabled in
the consuming app.

Resource model .NET app resources keep the same distinction through
provider-owned `project.references`
`ResourceReference` values. `DependsOn` remains startup ordering and is not
used to derive service discovery configuration.
