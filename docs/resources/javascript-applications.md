# JavaScript Applications

Use the JavaScript app resource type for local JavaScript and Node.js
applications that should participate in the CloudShell local development
resource graph. These resources project as `application.javascript-app`.

The first JavaScript app provider assumes the app runs on Node.js. It records
that assumption as `javascript.engine: node` and uses package-manager/script
attributes for the development command. Future extensions can add more focused
registration helpers for frameworks such as Vite, Next.js, or workers without
changing the base resource type.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [ASP.NET Core applications](aspnet-core-applications.md),
[Executable applications](executable-applications.md), and
[Container apps](container-apps.md).

## Declaration

Programmatic declarations use `AddJavaScriptApp(...)` with a scoped resource
name and project path. The provider derives the canonical resource ID from the
name:

```csharp
resources
    .AddJavaScriptApp("frontend", "src/frontend")
    .WithDisplayName("Frontend")
    .WithPackageManager("pnpm")
    .WithScript("dev")
    .WithHttpEndpoint(port: 5173, targetPort: 5173, host: "localhost");
```

The default declaration assumes:

```yaml
javascript:
  engine: node
  packageManager: npm
  script: dev
```

JavaScript app resources can declare endpoint requests, environment variables,
service references, health checks, log sources, and volume mounts using the
same Resource model patterns as other application resources.

Lifecycle execution is intentionally separate from the type declaration. This
slice makes JavaScript apps first-class resources and template entries; the
Node.js runtime operation provider can be added as a later provider adapter
without changing the resource type identity.

Future JavaScript client packages can build on this resource type by giving
Node.js applications typed access to CloudShell services such as Configuration
Store, logs, traces, or service discovery. Those clients should stay separate
from the resource type: the resource type describes how the app participates in
the graph, while client packages make it easier for the running app to consume
CloudShell-managed services.

## Sample

The `samples/JavaScriptApp` sample declares:

- a `application.javascript-app` frontend rooted at `samples/JavaScriptApp/App`
- a Configuration Store resource referenced by the JavaScript app
- environment variables that show how the app can receive another resource's
  endpoint during local development

Run the CloudShell host:

```bash
dotnet run --project samples/JavaScriptApp/Host/CloudShell.JavaScriptAppHost.csproj
```

The Node app itself can also be run directly while the resource type remains
the CloudShell graph and Resource Manager representation:

```bash
cd samples/JavaScriptApp/App
npm run dev
```
