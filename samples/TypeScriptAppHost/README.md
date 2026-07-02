# TypeScript App Host Sample

This sample declares a CloudShell resource graph from TypeScript using the
experimental `@cloudshell/local-development` package.

It proves the hosting integration shape:

- TypeScript code declares resources through builder-style APIs.
- The package emits ResourceTemplate JSON accepted by CloudShell.
- The sample can hand that template to the current CloudShell CLI.

Generate the template:

```bash
npm install
npm run template
```

Apply the template to an already-running Control Plane:

```bash
CLOUDSHELL_CONTROL_PLANE_URL=http://127.0.0.1:5097 npm run apply
```

Start the configured local host before applying. If the sample already has a
recorded running Control Plane process in `.cloudshell/control-plane.json`, the
CLI reuses that process instead of starting a new one:

```bash
npm run apply -- --start
```

The current sample host normally enforces Control Plane authentication. For an
isolated local proof-of-concept run, disable host authentication when launching
a new host process:

```bash
Authentication__Enabled=false npm run apply -- --start --no-build
```

On a fresh checkout, omit `--no-build` or build the .NET host first.

`Authentication__Enabled=false` only affects a host process that is launched by
that command. If a host is already running, it keeps the authentication mode it
started with. Stop the recorded sample host before relaunching with different
authentication settings:

```bash
dotnet run --project ../../CloudShell.Cli/CloudShell.Cli.csproj -- \
  control-plane stop \
  --state-dir .cloudshell
Authentication__Enabled=false npm run apply -- --start --no-build
```

A successful run starts the configured host, waits until the Control Plane API
is ready, applies the generated template, and prints:

```text
Template applied.
```

When running against an authenticated host, supply a Control Plane bearer token
through `CLOUDSHELL_CONTROL_PLANE_TOKEN` or pass `--bearer-token` through the
sample apply command:

```bash
CLOUDSHELL_CONTROL_PLANE_TOKEN=<token> npm run apply -- --start
npm run apply -- --start --bearer-token <token>
```

Stop the local daemon state recorded for this sample:

```bash
dotnet run --project ../../CloudShell.Cli/CloudShell.Cli.csproj -- \
  control-plane stop \
  --state-dir .cloudshell
```

If you want to reset generated local sample state after a run:

```bash
rm -rf .cloudshell ../JavaScriptApp/Host/Data
```
