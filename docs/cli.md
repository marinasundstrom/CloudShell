# CloudShell CLI

The CloudShell CLI is the command-line entry point for local-development and
automation workflows. Its command shape should be closer to Azure CLI than to a
single-purpose app launcher: command groups manage CloudShell surfaces and
operate the Control Plane through the same API used by remote clients.

The first supported responsibilities are:

- start, stop, and inspect a local Control Plane host process
- open an already-hosted CloudShell UI on a best-effort basis
- list resources, inspect resource details, and execute resource actions
  through the Control Plane API
- apply ResourceTemplate YAML documents through the Control Plane API, with
  JSON available as the alternative format
- configure local hosts-file name mappings for development names
- use identity when calling the Control Plane API

The CLI does not become a second Control Plane. It launches or discovers a
host, then uses the Control Plane API for resource operations.

## Local Control Plane

Start a local Control Plane host:

```bash
dotnet run --project CloudShell.Cli -- control-plane start
```

Use an explicit host project or URL when the defaults do not fit:

```bash
dotnet run --project CloudShell.Cli -- control-plane start \
  --host-project samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj \
  --url http://127.0.0.1:5200
```

The CLI records daemon state in `.cloudshell/control-plane.json` by default.
That state contains the process id, Control Plane URL, host project path, and
start time. It must not contain credentials or secret values.

Inspect or stop the recorded process:

```bash
dotnet run --project CloudShell.Cli -- control-plane status
dotnet run --project CloudShell.Cli -- control-plane stop
```

## CloudShell UI

Open CloudShell UI in the default browser:

```bash
dotnet run --project CloudShell.Cli -- ui open
```

`ui open` is best effort. If `--url` is omitted, the CLI opens the recorded
local Control Plane host URL, which works when that host also serves the UI.
Use an explicit URL when the UI is hosted elsewhere:

```bash
dotnet run --project CloudShell.Cli -- ui open --url http://127.0.0.1:5096
```

## Resource Operations

List resources from the recorded local Control Plane:

```bash
dotnet run --project CloudShell.Cli -- resource list
```

List resources from a specific Control Plane:

```bash
dotnet run --project CloudShell.Cli -- resource list \
  --control-plane https://control-plane.example.com
```

Filter by type, class, or registration state:

```bash
dotnet run --project CloudShell.Cli -- resource list \
  --type application.container-app \
  --class Service \
  --registered true
```

Show a single resource before executing operations on it:

```bash
dotnet run --project CloudShell.Cli -- resource show application:api
```

The detail view includes resource identity, type, lifecycle state, endpoint
contracts, resolved endpoint addresses, dependencies, actions, attributes, and
capabilities. Use the action IDs from this output with
`resource action execute`.

Execute a resource action through the Control Plane API:

```bash
dotnet run --project CloudShell.Cli -- resource action execute application:api start
```

Action execution supports dependency and dependent-resource switches that map
to the Control Plane resource-action contract:

```bash
dotnet run --project CloudShell.Cli -- resource action execute application:api start \
  --start-dependencies

dotnet run --project CloudShell.Cli -- resource action execute application:api stop \
  --ignore-dependent-warning
```

## Apply Resource Templates

Apply a ResourceTemplate document to the recorded local Control Plane:

```bash
dotnet run --project CloudShell.Cli -- template apply ./cloudshell.template.yaml
```

Apply to a specific remote or split Control Plane:

```bash
dotnet run --project CloudShell.Cli -- template apply ./cloudshell.template.yaml \
  --control-plane https://control-plane.example.com
```

YAML is the preferred authoring format. Use `.json` when a workflow needs the
JSON `ResourceTemplate` projection used by the Control Plane API.

For normal local application development, run the app host itself and let that
host start the Control Plane, UI, and declared resources. `template apply` is
still useful when a script, SDK, or automation flow needs to apply changes to
an already-running Control Plane instance.

Use `--start` when the CLI should launch the local Control Plane host before
applying the template. The same daemon options used by `control-plane start`
can be supplied when the default host project, URL, state directory, or build
behavior does not fit:

```bash
dotnet run --project CloudShell.Cli -- template apply ./cloudshell.template.yaml \
  --start \
  --host-project samples/JavaScriptApp/Host/CloudShell.JavaScriptAppHost.csproj \
  --url http://127.0.0.1:5097 \
  --state-dir samples/TypeScriptAppHost/.cloudshell \
  --no-build
```

If the selected state directory already records a running Control Plane
process, `--start` reuses that process. Host process environment variables and
authentication settings are only applied when the CLI starts a new process; use
`control-plane stop --state-dir <dir>` before relaunching with different host
configuration, or pass credentials that match the running host.

`--control-plane` is the first explicit target selector. Later, the CLI should
support profile-backed target selection so commands can default to a named
local, split, team-owned, or on-premise Control Plane without repeating the URL
on every command.

The apply mode defaults to `create-or-update`. Supported modes are:

- `create-or-update`
- `create-only`
- `update-existing`

For example:

```bash
dotnet run --project CloudShell.Cli -- template apply ./cloudshell.template.yaml \
  --mode update-existing
```

## Identity

CLI commands that call the Control Plane API can pass a bearer token:

```bash
dotnet run --project CloudShell.Cli -- template apply ./cloudshell.template.yaml \
  --bearer-token "$TOKEN"
```

The same token can be supplied through `CLOUDSHELL_CONTROL_PLANE_TOKEN`.

This is intentionally a temporary credential source. CloudShell should later
standardize a local credential/profile store, similar in role to Azure CLI's
profile directory, so CLI commands and SDK launchers can discover the active
Control Plane account, selected Control Plane target, selected environment, and
credential material from one well-known place. That store must be designed
before tokens are persisted.

## Local Host Name Mappings

The CLI can add or remove local hosts-file mappings for development names:

```bash
dotnet run --project CloudShell.Cli -- host names add api.local.test 127.0.0.1
dotnet run --project CloudShell.Cli -- host names remove api.local.test
```

On macOS and Linux the default target is `/etc/hosts`. On Windows the default
target is the system hosts file under `drivers/etc/hosts`. Those files usually
require elevated privileges. The CLI does not silently run `sudo`; if the
write is denied, rerun the command elevated or use a custom file:

```bash
sudo dotnet run --project CloudShell.Cli -- host names add api.local.test 127.0.0.1

dotnet run --project CloudShell.Cli -- host names add api.local.test 127.0.0.1 \
  --hosts-file ./hosts.dev
```

Use `--dry-run` to inspect the planned change without writing:

```bash
dotnet run --project CloudShell.Cli -- host names add api.local.test 127.0.0.1 \
  --dry-run
```

CLI-managed names use the same CloudShell marker block as the local hostname
provider:

```text
# BEGIN CloudShell local hostnames
127.0.0.1 api.local.test
# END CloudShell local hostnames
```
