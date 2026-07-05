# C# App Host Launcher Sample

This sample shows the launcher-style C# hosting pattern. The `AppHost` project
declares the distributed application with the same Resource Model builders used
by in-process C# hosts, but it does not reference or compose the Control Plane
directly.
The sample also seeds development Configuration Store settings and Secrets Vault
secrets through create-only resource-definition attributes.

The launcher defaults to `CloudShell.LocalDevelopmentHost`, the stable
CloudShell local-development host profile. Host profile settings live in
`AppHost/appsettings.json`; the launcher reads them through normal .NET
configuration and forwards that file to the launched host. The resource graph
stays in `Program.cs`.

Generate the template:

```bash
dotnet run --project AppHost/CloudShell.CSharpAppHost.csproj
```

Apply to an already-running Control Plane:

```bash
CLOUDSHELL_CONTROL_PLANE_URL=http://127.0.0.1:5099 \
dotnet run --project AppHost/CloudShell.CSharpAppHost.csproj -- --apply
```

Run the local-development host in the foreground, apply the declarations, and
keep the host tied to the launcher command lifetime:

```bash
./cloudshell.sh run
```

Start or reuse the daemon-style local-development host profile, then apply the
declarations:

```bash
dotnet run --project AppHost/CloudShell.CSharpAppHost.csproj -- --start
```

Use `AppHost/appsettings.json` to choose host settings such as
`Authentication`, `CloudShell:DataDirectory`, persistence, or the default
`CloudShell:Launcher:ControlPlaneUrl`.

Open the Web UI:

```bash
dotnet run --project ../../CloudShell.Cli/CloudShell.Cli.csproj -- ui open \
  --url http://127.0.0.1:5099
```

The JavaScript app resource is declared but not auto-started. Start it from
Resource Manager or through the helper. The helper enables dependency startup,
so the Configuration Store and Secrets Vault resources are started before the
JavaScript app. The generated template seeds `Sample--Message` in the
Configuration Store and `Sample--ApiKey` in the Secrets Vault; those values are
not emitted by default template export after apply. The sample binds the secret
to an environment variable only to make the launcher demo visible; production
workloads should prefer resolving secrets through the Secrets Vault client
instead of exposing secret values in process environment state.

```bash
./cloudshell.sh start-app
```
