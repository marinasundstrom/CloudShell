# Bun JavaScript App Sample

This sample declares a CloudShell `application.javascript-app` resource for a
Bun-backed JavaScript app and a referenced Configuration Store resource. It
demonstrates the local JavaScript process flow when the app runtime and package
manager are both Bun.

Run the launcher in a foreground terminal. The launcher declares the resources,
starts `CloudShell.LocalDevelopmentHost`, and applies the generated template:

```bash
./cloudshell.sh run
```

From a second terminal, inspect resources and open the UI:

```bash
./cloudshell.sh resources
./cloudshell.sh open
```

Start the Bun app from Resource Manager, or start it from the helper:

```bash
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5174
```

The launcher can also print the resource template without starting a host:

```bash
./cloudshell.sh template
```

The Bun app itself can also be run directly:

```bash
cd App
bun run dev
```

Requirements:

- Bun is required to run the app process.
- .NET 11 preview is required to run the launcher.

`reset` removes generated launcher and host state. Use it when clearing stale
resources from earlier sample runs.
