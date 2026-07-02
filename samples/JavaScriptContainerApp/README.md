# JavaScript Container App Sample

This sample declares a JavaScript app resource for a Node.js app and wraps it
as an `application.container-app`. It demonstrates the app-as-container
decorator path: the resource is authored as a JavaScript app, while execution
uses a Dockerfile-backed container app that can scale replicas.

Run the app host in a foreground terminal:

```bash
./cloudshell.sh run-no-auth
```

That helper is just a shorter form of:

```bash
Authentication__Enabled=false dotnet run --project Host/CloudShell.JavaScriptContainerAppHost.csproj -- --urls http://127.0.0.1:5098
```

From a second terminal, inspect resources and open the UI:

```bash
./cloudshell.sh resources
./cloudshell.sh open
```

Start the containerized JavaScript app from Resource Manager, or start it from
the helper:

```bash
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5174
```

The sample container image is built from `App/Dockerfile`. The host port is
`5174`; the container listens on port `8080` and CloudShell routes local
container app ingress to the three JavaScript replicas declared by the host.

The basic `samples/JavaScriptApp` sample remains the local Node.js process
flow. Use this sample when testing container packaging, deployment, and scale
views.
