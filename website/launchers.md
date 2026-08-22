---
title: Launchers
description: Author one CloudShell resource graph from C#, TypeScript, Java, Go, Python, or YAML without moving platform responsibilities into the workload.
---

# CloudShell launchers

A launcher is a strongly typed, language-specific builder DSL for describing a CloudShell application environment. Its builders produce a normal CloudShell resource template—the same desired-state document you can author directly in YAML—then the launcher can start a local host or apply that template to a compatible Control Plane.

> [!IMPORTANT]
> Launcher packages have not been published yet. To run a launcher or any launcher-based repository sample, clone the CloudShell repository and build the complete solution first. The published preview CLI with a project-local YAML template remains the supported path for trying CloudShell without building the repository.

The launcher is not the CloudShell server and it is not application runtime code. These responsibilities stay separate:

- **Launcher:** defines resources and relationships.
- **CloudShell host:** runs the Control Plane, Resource Manager, providers, and runtime adapters.
- **Application:** uses injected endpoints and credentials to consume the resources it references.

## One model, idiomatic APIs

C# can use fluent extension methods. TypeScript can use builders and object options. Java, Go, and Python should feel natural in their ecosystems. The resulting template still uses the same type IDs, resource IDs, endpoints, dependencies, references, identities, and access grants.

Current preview launcher work exists for:

- C#
- TypeScript and JavaScript
- Java
- Go
- Python

Parity is still evolving. C# currently has the broadest builder coverage; the other launchers demonstrate that CloudShell's authoring and workload model is not limited to .NET.

## Try a launcher from source

Clone the repository and build it before running a launcher sample:

```bash
git clone https://github.com/marinasundstrom/CloudShell.git
cd CloudShell
dotnet build CloudShell.slnx
```

Samples under `samples/` use launcher projects and packages from that same checkout. Follow the sample's README for any additional language toolchain—such as Node.js, a JDK, Go, or Python—and for its exact run command.

## The same idea in every launcher

These abbreviated examples declare one local HTTP application. They focus on the resource graph; the repository samples also configure paths to the source-built CLI and local host profile.

# [C#](#tab/csharp)

```csharp
using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;

var app = CloudShellDistributedApplication
    .CreateBuilder("sample", args);

app.DefineResources(resources =>
{
    resources
        .AddDotnetProject("api", "../Api/Api.csproj")
        .WithDisplayName("API")
        .WithHttpEndpoint(
            host: "localhost",
            port: 5080,
            targetPort: 5080);
});

return await app.LaunchAsync();
```

# [TypeScript](#tab/typescript)

```typescript
import { cloudshell } from "@cloudshell/local-development";

const app = cloudshell("sample");

app
  .addJavaScriptApp("api", "../app")
  .withDisplayName("API")
  .withPackageManager("npm")
  .withScript("dev")
  .withHttpEndpoint({
    host: "localhost",
    port: 5173,
    targetPort: 5173
  });

await app.run({
  cliProject: "../../CloudShell.Cli/CloudShell.Cli.csproj",
  hostProject: "../../CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj"
});
```

# [Java](#tab/java)

```java
import com.cloudshell.launcher.CloudShellApp;
import com.cloudshell.launcher.CloudShellLauncherOptions;
import java.nio.file.Path;

CloudShellApp app = CloudShellApp.create("sample");

var hostNetwork = app.addNetwork("host")
    .withResourceId("network:host")
    .withNetworkKind("Host")
    .withHostReadiness("hostReady");

app.addJavaApp("api", "../app", "target/app.jar")
    .withDisplayName("API")
    .withHttpEndpoint("localhost", 8080, 8080, hostNetwork);

var options = new CloudShellLauncherOptions()
    .withCliProject(Path.of("../../CloudShell.Cli/CloudShell.Cli.csproj"))
    .withHostProject(Path.of("../../CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj"));

System.exit(app.run(options).exitCode());
```

# [Go](#tab/go)

```go
import (
    "os"

    "github.com/cloudshell/launcher-go/cloudshell"
)

app := cloudshell.NewApp("sample")

hostNetwork := app.AddNetwork("host").
    WithResourceID("network:host").
    WithNetworkKind("Host").
    WithHostReadiness("hostReady")

app.AddGoApp("api", "../app").
    WithDisplayName("API").
    WithHttpEndpoint("localhost", 8080, 8080, hostNetwork)

options := cloudshell.DefaultLauncherOptions()
options.CLIProject = "../../CloudShell.Cli/CloudShell.Cli.csproj"
options.HostProject = "../../CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj"

os.Exit(app.RunWithOptions(os.Args[1:], options))
```

# [Python](#tab/python)

```python
import sys

from cloudshell.launcher import CloudShellDistributedApplication, LauncherOptions

app = CloudShellDistributedApplication.create_builder(
    "sample",
    sys.argv[1:],
)

def define_resources(resources):
    (
        resources.add_python_app("api", "../app")
        .with_display_name("API")
        .with_http_endpoint(
            host="localhost",
            port=5188,
            target_port=5188,
        )
    )

app.define_resources(define_resources)
options = LauncherOptions(
    cli_project="../../CloudShell.Cli/CloudShell.Cli.csproj",
    host_project="../../CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj",
)
raise SystemExit(app.run(options=options))
```

---

## Typical launcher lifecycle

1. Run the launcher with the normal command for its language.
2. The launcher builds a resource template.
3. It starts the local CloudShell host or targets an existing Control Plane.
4. It applies the resource graph.
5. CloudShell prints the Resource Manager address and keeps the environment available.

Launchers should also be able to emit the generated template for review. YAML remains the clearest supported starting point today; the builder APIs and package names may change before launcher packages are published.

## Choose YAML or a launcher

Use YAML when the graph is small, reviewability is the priority, or you want the current supported CLI path. Use a launcher when normal language constructs, shared defaults, conditional composition, or reusable builders materially improve authoring. Both approaches should produce equivalent resource intent.
