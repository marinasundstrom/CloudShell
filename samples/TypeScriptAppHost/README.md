# TypeScript App Host Sample

This sample declares a CloudShell resource graph from TypeScript using the
experimental `@cloudshell/local-development` package.

It proves the TypeScript declaration shape:

- TypeScript code declares resources through builder-style APIs.
- The package emits ResourceTemplate JSON accepted by CloudShell.
- The sample can hand that template to the current CloudShell CLI.
- The launcher seeds development Configuration Store settings and Secrets Vault
  secrets through create-only resource-definition attributes.

The current POC uses `CloudShell.LocalDevelopmentHost` as the .NET process that
owns the Control Plane and Web UI. The TypeScript file is the declaration
client for that host, not a replacement host process. Set
`CLOUDSHELL_HOST_PROJECT` only when a scenario needs a custom CloudShell host
profile with additional extensions or host-specific services. Generated daemon
state and host data default to `.cloudshell/` under this sample so databases
and local CloudShell files stay with the launcher project.

Generate the template:

```bash
npm install
npm run template
```

Apply the template to an already-running Control Plane:

```bash
CLOUDSHELL_CONTROL_PLANE_URL=http://127.0.0.1:5097 npm run apply
```

Run the local-development host in a foreground terminal, apply the
TypeScript-authored template, and keep the host tied to the launcher command
lifetime:

```bash
./cloudshell.sh run-no-auth
```

Apply the TypeScript-authored template to an already-running host:

```bash
./cloudshell.sh apply
```

After applying the template, open the Web UI and start the TypeScript-declared
JavaScript app from Resource Manager, or start it from the helper. The helper
enables dependency startup, so the Configuration Store resource is started
before the JavaScript app. The generated template seeds `Sample--Message` in
the Configuration Store and `Sample--ApiKey` in the Secrets Vault; those values
are not emitted by default template export after apply. The sample binds the
secret to an environment variable only to make the launcher demo visible;
production workloads should prefer resolving secrets through the Secrets Vault
client instead of exposing secret values in process environment state:

```bash
./cloudshell.sh open
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5173
```

The helper also keeps CLI daemon `start` and `apply-start` commands available
for daemon-specific testing, but foreground `run` is the normal local sample
flow.

Use `CLOUDSHELL_DATA_DIR` to choose a different local CloudShell data
directory for the launched host.

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
