# Executable Applications

Use the executable application resource type for commands that run on the
CloudShell host as `application.executable` resources. Executable applications
can configure endpoints, environment variables, process lifetime, references,
and service discovery, then can be started, stopped, restarted, and inspected
from Resource Manager.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [ASP.NET Core applications](aspnet-core-applications.md) and
[Container apps](container-apps.md).

## Lifetime

Executable applications support two lifetimes:

- `Detached`: the default. CloudShell starts the process, records the last known
  process ID and start time, and does not stop it when the CloudShell control
  plane or UI exits. On restart, CloudShell checks the persisted PID and process
  start time to rediscover the running process without trusting a potentially
  reused PID by itself. Rediscovery applies when the resource definition is
  still available through persisted Resource Manager configuration or the same
  programmatic declaration is loaded again.
- `ControlPlaneScoped`: CloudShell owns the process lifetime. The provider stops
  the process tree when the CloudShell process is disposed and waits briefly
  for it to exit. On the next Control Plane startup, CloudShell also reconciles
  any persisted PID for a host-scoped process and stops it before auto-starting
  the current declaration graph. Use this for temporary helpers that should not
  outlive the local CloudShell session.

The Resource Manager UI defaults executable applications to `Detached` because
UI-created resources usually represent longer-lived services that should keep
running if the shell restarts. Programmatic executable and ASP.NET Core project
declarations default to `ControlPlaneScoped` for Aspire-like local development
flows. Use `.WithLifetime(ResourceLifetime.Detached)` when a code-first
declaration should intentionally survive the CloudShell host.

This host restart recovery is different from workload crash recovery. If an
executable process exits unexpectedly, CloudShell should project the observed
state and leave restart/no-restart/backoff policy to the orchestrator layer or
an explicit future resource policy.

## Logs

Detached applications write stdout and stderr to a per-resource log file so
output continues to have a stable sink after CloudShell exits. The Logs view
reads and tails that file when CloudShell is running.

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
variables, and the sample uses the `CloudShell.Configuration.Client`
configuration-provider integration to load settings during startup. If the
configuration service is unavailable, the provider records unavailable status
and the app continues running. The `/configuration` endpoint reports the
provider status and currently loaded keys.

After adding the resource through `/resources/add`, use the Start action to start
it and open the `http://localhost:5127` endpoint from the resource details blade.

See [Configuration services](../configuration-services.md).
