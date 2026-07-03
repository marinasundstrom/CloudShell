# Go App Sample

This sample declares a CloudShell `application.go-app` resource for a small Go
HTTP API.

CloudShell provider authoring is currently C#-only. The Go workload integrates
as an application resource through a C# CloudShell host in this sample. Future
Go launcher support can emit the same resource model through templates or the
Control Plane API, but it does not change the provider boundary.

The sample includes:

- a C# CloudShell host that declares the Go app resource
- a Go HTTP API under `App`
- Configuration Store and Secrets Vault resources referenced by the Go app
- Resource Manager support for start, stop, restart, endpoints, logs,
  monitoring, configuration, and environment views

Run the sample host in the foreground:

```bash
samples/GoApp/cloudshell.sh run-no-auth
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
