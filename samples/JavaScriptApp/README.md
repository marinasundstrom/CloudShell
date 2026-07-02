# JavaScript App Sample

This sample declares a CloudShell `application.javascript-app` resource for a
Node.js app and a referenced Configuration Store resource. It demonstrates the
basic local Node.js process flow.

Run the app host in a foreground terminal. The host declares the resources and
starts the Control Plane and Web UI in the same process, which is the normal
local-development shape:

```bash
./cloudshell.sh run-no-auth
```

That helper is just a shorter form of:

```bash
Authentication__Enabled=false dotnet run --project Host/CloudShell.JavaScriptAppHost.csproj -- --urls http://127.0.0.1:5097
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

The helper also keeps CLI daemon commands available for daemon-specific
testing, but daemon mode is not part of the normal sample flow.

The Node app itself can also be run directly:

```bash
cd App
npm run dev
```

`reset` removes generated sample daemon state and the sample host `Data`
directory. Use it when clearing stale resources from earlier sample runs.
