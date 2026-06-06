# Executable Applications

CloudShell includes an executable application provider for local development machines.
It lets you register any executable command as an `application.executable` resource,
configure arguments, working directory, endpoint, environment variables, and process
lifetime, then start, stop, restart, and inspect it from Resource Manager.

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
`application.executable` resources. Export writes a provider-owned configuration
payload with:

- executable path
- arguments
- working directory
- endpoint
- environment variables
- lifetime
- Aspire endpoint environment variable opt-in

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
service.
CloudShell injects service-specific endpoint and token environment variables,
and the sample uses the reusable `CloudShell.Configuration` provider to load
settings during startup. If the configuration service is unavailable, the
provider records unavailable status and the app continues running. The
`/configuration` endpoint reports the provider status and currently loaded keys.

Applications can also opt in to Aspire-style service discovery endpoint variables
for referenced resources. When enabled, CloudShell maps dependency endpoints into
environment variables using the .NET configuration shape:

```text
services__<resource-name>__<endpoint-name-or-scheme>__0=<endpoint-address>
```

CloudShell emits names based on both the referenced resource name and resource
ID, normalized for environment variables. Explicit application environment
variables are applied last, so they can override generated Aspire endpoint
variables.

Endpoint variables are generated from the application's referenced resources,
not from its wait dependencies. For declarative application resources,
`WithReference(...)` records an endpoint reference, while `WaitFor(...)` records
a startup dependency. CloudShell only emits endpoint variables when the
referenced resource is registered in the same resource group.

Applications can read the generated URLs directly through `IConfiguration`:

```csharp
var apiUrl = builder.Configuration.GetCloudShellServiceEndpoint("example-api", "http");
client.BaseAddress = builder.Configuration.GetCloudShellServiceEndpointUri("example-api", "http");
```

After adding the resource through `/resources/add`, use the Run action to start
it and open the `http://localhost:5127` endpoint from the resource details blade.

See [Configuration services](configuration-services.md).
