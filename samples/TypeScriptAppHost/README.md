# TypeScript App Host Sample

This sample declares a CloudShell resource graph from TypeScript using the
experimental `@cloudshell/local-development` package.

It proves the TypeScript declaration shape:

- TypeScript code declares resources through builder-style APIs.
- The package emits ResourceTemplate JSON accepted by CloudShell.
- The sample can hand that template to the current CloudShell CLI.

The current POC still runs the .NET CloudShell host as the process that owns
the Control Plane and Web UI. The TypeScript file is the declaration client for
that host, not a replacement host process yet.

Generate the template:

```bash
npm install
npm run template
```

Apply the template to an already-running Control Plane:

```bash
CLOUDSHELL_CONTROL_PLANE_URL=http://127.0.0.1:5097 npm run apply
```

Run the app host in a foreground terminal. The host starts the Control Plane
and Web UI in the same process:

```bash
./cloudshell.sh run-no-auth
```

From a second terminal, apply the TypeScript-authored template to that running
host:

```bash
./cloudshell.sh apply
```

After applying the template, open the Web UI and start the TypeScript-declared
JavaScript app from Resource Manager, or start it from the helper:

```bash
./cloudshell.sh open
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5173
```

The helper also keeps CLI daemon commands available for daemon-specific
testing, but daemon mode is not part of the normal sample flow.

When running against an authenticated host, supply a Control Plane bearer token
through `CLOUDSHELL_CONTROL_PLANE_TOKEN` or pass `--bearer-token` through the
sample apply command:

```bash
CLOUDSHELL_CONTROL_PLANE_TOKEN=<token> npm run apply
npm run apply -- --bearer-token <token>
```

`Authentication__Enabled=false` only affects a host process that is launched by
that command. Stop the foreground host and relaunch when changing
authentication settings.

A successful apply prints:

```text
Template applied.
```

If you want to reset generated local sample state after a run:

```bash
./cloudshell.sh reset
```
