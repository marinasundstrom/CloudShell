# C# App Host Launcher Sample

This sample shows the launcher-style C# hosting pattern. The `AppHost` project
declares the distributed application with the same Resource Model builders used
by in-process C# hosts, but it does not reference or compose the Control Plane
directly.

The `Host` project is the CloudShell host profile. It starts the Control Plane,
Web UI, built-in providers, and provider runtime adapters. The launcher app can
start that host through the CLI, or apply the generated template to an already
running Control Plane.

Generate the template:

```bash
dotnet run --project AppHost/CloudShell.CSharpAppHost.csproj
```

Apply to an already-running Control Plane:

```bash
CLOUDSHELL_CONTROL_PLANE_URL=http://127.0.0.1:5099 \
dotnet run --project AppHost/CloudShell.CSharpAppHost.csproj -- --apply
```

Start or reuse the sample host profile, then apply the declarations:

```bash
Authentication__Enabled=false \
dotnet run --project AppHost/CloudShell.CSharpAppHost.csproj -- --start
```

Open the Web UI:

```bash
dotnet run --project ../../CloudShell.Cli/CloudShell.Cli.csproj -- ui open \
  --url http://127.0.0.1:5099
```

The JavaScript app resource is declared but not auto-started. Start it from
Resource Manager or through the CLI:

```bash
dotnet run --project ../../CloudShell.Cli/CloudShell.Cli.csproj -- resource action execute \
  application.javascript-app:csharp-declared-frontend start \
  --control-plane http://127.0.0.1:5099
```
