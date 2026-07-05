# Go App Sample

This sample declares a CloudShell `application.go-app` resource for a small Go
HTTP API.

The Go workload integrates as an application resource through a C# launcher
AppHost. The launcher emits the resource model through a template and starts
`CloudShell.LocalDevelopmentHost`; the sample no longer defines a CloudShell
host in source.

The sample includes:

- a C# launcher AppHost that declares the Go app resource
- a Go HTTP API under `App`
- the experimental Go runtime SDK from `sdk/go/cloudshell`
- Configuration Store and Secrets Vault resources referenced by the Go app
- Resource Manager support for start, stop, restart, endpoints, logs,
  monitoring, configuration, and environment views

Run the launcher-backed sample in the foreground:

```bash
samples/GoApp/cloudshell.sh run
```

From a second terminal, open the Web UI, list resources, and start the Go app:

```bash
samples/GoApp/cloudshell.sh open
samples/GoApp/cloudshell.sh resources
samples/GoApp/cloudshell.sh start-app
```

The Go app can also be run directly while the CloudShell resource remains the
Resource Manager representation:

```bash
cd samples/GoApp/App
go run .
```

The running app exposes `/configuration`, which uses the Go runtime SDK to read
the injected Configuration Store endpoint. Its default credential resolves the
same way as the C#, TypeScript, and Java runtime clients:
`CLOUDSHELL_IDENTITY_*` workload identity first, then environment bearer tokens,
then the active CloudShell profile.
