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

## Logs

Detached applications write stdout and stderr to a per-resource log file so output
continues to have a stable sink after CloudShell exits. The Logs view reads and
tails that file when CloudShell is running.

Control-plane-scoped applications keep stdout and stderr redirected through the
provider process while CloudShell is running, and provider lifecycle entries are
also written to the per-resource log file.

## Sample

The host registers an initial `Example Web API` executable application that runs:

```bash
dotnet run --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --no-launch-profile
```

It sets:

```text
ASPNETCORE_URLS=http://localhost:5127
CLOUDSHELL_APPLICATION=Example Web API
```

After adding the resource through `/resources/add`, use the Run action to start
it and open the `http://localhost:5127` endpoint from the resource details blade.
