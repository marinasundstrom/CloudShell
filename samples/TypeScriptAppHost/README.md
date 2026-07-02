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

Start the configured local host before applying:

```bash
npm run apply -- --start
```
