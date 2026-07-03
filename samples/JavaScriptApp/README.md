# JavaScript App Sample

This sample declares a CloudShell `application.javascript-app` resource for a
Node.js app and a referenced Configuration Store resource. It demonstrates the
basic local Node.js process flow.

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

Start the JavaScript app from Resource Manager, or start it from the helper:

```bash
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5173
```

The launcher can also print the resource template without starting a host:

```bash
./cloudshell.sh template
```

The Node app itself can also be run directly:

```bash
cd App
npm run dev
```

`reset` removes generated launcher and host state. Use it when clearing stale
resources from earlier sample runs.
