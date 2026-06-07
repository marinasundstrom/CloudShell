# Executable Applications

CloudShell includes an application provider for local development machines. It
lets you register commands that run on the CloudShell host as
`application.executable` resources, and ASP.NET Core project references as
`application.aspnet-core-project` resources. Both configure endpoint, environment
variables, process lifetime, and references, then can be started, stopped,
restarted, and inspected from Resource Manager.

Application resources are primarily intended for local development: ASP.NET Core
APIs, frontend dev servers, emulators, workers, and similar host-local tools.
They are not a deployment abstraction for remote infrastructure.

## ASP.NET Core Apps

Use the ASP.NET Core project resource type for local .NET web projects.
Programmatic declarations use `AddAspNetCoreProject(...)` when specifying a
resource ID, or `AddAspNetCoreProjectFromName(...)` when CloudShell should
derive the resource ID from the name:

```csharp
resources
    .AddAspNetCoreProject(
        "application:example-web-api",
        "Example Web API",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .WithReference(configuration)
    .WithServiceDiscovery();
```

By default, CloudShell starts ASP.NET Core project resources with hot reload:

```bash
dotnet watch --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj run --no-launch-profile
```

Pass `hotReload: false` to use plain `dotnet run --no-launch-profile` instead.
ASP.NET Core project resources always get an HTTP endpoint. If the declaration
omits `endpoint`, CloudShell assigns a stable local port. If the declaration
sets `endpoint`, that URL fixes the displayed port. In both cases CloudShell
injects the resolved URL into `ASPNETCORE_URLS` when the process starts, so the
project listens on the same endpoint that Resource Manager displays.

## Lifetime

Executable applications support two lifetimes:

- `Detached`: the default. CloudShell starts the process, records the last known
  process ID and start time, and does not stop it when the CloudShell control
  plane or UI exits. On restart, CloudShell checks the persisted PID and process
  start time to rediscover the running process without trusting a potentially
  reused PID by itself.
- `ControlPlaneScoped`: CloudShell owns the process lifetime. The provider stops
  the process when the CloudShell process is disposed. Use this for temporary
  helpers that should not outlive the local CloudShell session.

The default is `Detached` because executable application resources usually
represent local dev services such as APIs, frontend dev servers, emulators, or
workers. Those services should keep running if the CloudShell UI or control plane
is restarted.

## Runtime State

The provider persists runtime state separately from application configuration.
By default:

```text
CloudShell.Host/Data/application-resources.json
CloudShell.Host/Data/application-runtime-state.json
CloudShell.Host/Data/application-logs/
```

The runtime state file stores the last known PID, observed process start time,
last observation time, last exit code, and log path. The `Data` directory is
ignored by git because this is local machine state.

## Resource Templates

The application provider supports resource templates for
`application.executable`, `application.aspnet-core-project`,
`application.container-image`, and `application.sql-server` resources. Export writes a provider-owned
configuration payload with:

- executable path
- arguments
- working directory
- endpoint
- environment variables
- lifetime
- service discovery opt-in

Import creates a new application definition in the provider's configuration
store, assigns it to the imported group, and avoids overwriting an existing
application with the same generated ID.

See [Resource templates](resource-templates.md).

## Logs

Detached applications write stdout and stderr to a per-resource log file so output
continues to have a stable sink after CloudShell exits. The Logs view reads and
tails that file when CloudShell is running.

Control-plane-scoped applications keep stdout and stderr redirected through the
provider process while CloudShell is running, and provider lifecycle entries are
also written to the per-resource log file.

## Sample

Add the sample web API through `/resources/add` as an executable application
that runs:

```bash
dotnet run --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --no-launch-profile
```

It sets:

```text
ASPNETCORE_URLS=http://localhost:5127
CLOUDSHELL_APPLICATION=Example Web API
```

The sample can depend on the programmatically declared `Example Configuration`
store. CloudShell injects service-specific endpoint and token environment
variables, and the sample uses the reusable `CloudShell.Configuration` provider
to load settings during startup. If the configuration service is unavailable,
the provider records unavailable status and the app continues running. The
`/configuration` endpoint reports the provider status and currently loaded keys.

Applications can also opt in to Aspire-compatible service discovery for
referenced resources. `WithReference(...)` records that an application wants
endpoint/configuration values for another resource; `WithServiceDiscovery()` is
the separate opt-in that maps those referenced resource endpoints into
environment variables using the .NET configuration shape:

```text
services__<resource-name>__<endpoint-name-or-scheme>__0=<endpoint-address>
```

CloudShell emits names based on both the referenced resource name and resource
ID, normalized for environment variables. Explicit application environment
variables are applied last, so they can override generated service discovery
variables.

Endpoint variables are generated from the application's referenced resources,
not from its wait dependencies. For declarative application resources,
`WithReference(...)` records an endpoint reference, while `DependsOn(...)`
records a startup dependency. The broader resource model uses `DependsOn(...)`
as the standard dependency relationship; `WaitFor(...)` remains available on the
executable application builder as an Aspire-compatible alias. CloudShell only
emits endpoint variables when the referenced resource is registered in the same
resource group.

An executable application can depend on any resource builder returned from the
declarative graph, including provider sub-resources such as Docker containers.

Service discovery is intentionally opt-in. An application can reference or
depend on resources without receiving generated environment variables, which
leaves room for other discovery mechanisms such as a service discovery service
running in a container.

Applications can read the generated URLs directly through `IConfiguration`:

```csharp
client.BaseAddress = builder.Configuration.GetResourceUri("example-api", "http");
```

After adding the resource through `/resources/add`, use the Run action to start
it and open the `http://localhost:5127` endpoint from the resource details blade.

See [Configuration services](configuration-services.md).
