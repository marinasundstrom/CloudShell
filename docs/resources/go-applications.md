# Go Applications

Use the Go app resource type for local Go services that should participate in
the CloudShell local development resource graph. These resources project as
`application.go-app`.

CloudShell provider authoring is currently C#-only. The built-in Go app
provider is implemented in C#, and Go workloads integrate as managed
application resources. The experimental Go launcher under
`Launchers/Go/cloudshell` can emit templates or call the Control Plane API,
but provider implementation remains a C# extension boundary for now.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [Executable applications](executable-applications.md),
[Java applications](java-applications.md), and [Container apps](container-apps.md).

## Declaration

Programmatic C# declarations use `AddGoApp(...)` with a scoped resource name
and project path:

```yaml
resources:
  - type: application.go-app
    name: api
    project:
      path: src/api
```

The equivalent C# declaration is:

```csharp
resources
    .AddGoApp("api", "src/api");
```

The default local runtime starts:

```bash
go run .
```

`command`, `packagePath`, `binaryPath`, and `arguments` let resource authors
describe the local process shape without making a specific Go web framework
part of the CloudShell resource model. When `binaryPath` is configured, the
runtime starts that binary from the project directory instead of invoking
`go run`.

Endpoint and package-path choices are scenario-specific additions:

```yaml
resources:
  - type: application.go-app
    name: api
    project:
      path: src/api
      endpointRequests:
        - name: http
          protocol: http
          targetPort: 8080
          port: 8080
          exposure: Local
    command: go
    packagePath: ./cmd/api
```

Go app resources can declare endpoint requests, environment variables, service
references, health checks, log sources, and volume mounts using the same
Resource model patterns as other application resources. The default local
runtime tracks process state and exposes process logs and metrics through
Resource Manager.

Lifecycle actions require a Go app runtime controller. The built-in provider
registration supplies the local process runtime controller for normal hosts.
If a custom or direct operation path is constructed without that controller,
Resource Manager projects lifecycle actions as unavailable with a
missing-controller reason, and direct provider-execution calls return the same
readiness failure as a diagnostic instead of succeeding as a no-op.

## Runtime SDK

Go workloads use the experimental runtime SDK under `sdk/go/cloudshell` to call
CloudShell-protected services. The package is intentionally separate from the
Go launcher package:

- `Launchers/Go/cloudshell` declares ResourceTemplates and starts/applies them.
- `sdk/go/cloudshell` runs inside workloads and calls service endpoints.

The Go `DefaultCredential` resolves credentials in the same order as the C#,
TypeScript, and Java runtime clients: injected `CLOUDSHELL_IDENTITY_*` workload
identity, environment bearer tokens, then the active CloudShell profile. The
first Go runtime client is Configuration Store:

```go
configuration, err := cloudshell.ConfigurationStoreFromEnvironment("", nil)
if err != nil {
    return err
}

settings, err := configuration.GetSettings(context.Background())
```

Use `AsContainerApp(...)` when a Go app should be authored as a Go project but run
as a scalable container app:

```csharp
resources
    .AddGoApp("api", "src/api", packagePath: "./cmd/api")
    .AsContainerApp(tag: "dev", dockerfile: "Dockerfile")
    .WithReplicas(3);
```

The projection changes the Resource Manager resource to
`application.container-app` while retaining Go project metadata such as
`project.path`, `command`, `packagePath`, `binaryPath`, and `arguments`.

`samples/GoContainerApp` demonstrates this path from the Go launcher. It
declares a Go app as an `application.container-app`, seeds Configuration Store
and Secrets Vault resources, grants the container app identity read access,
and uses the Go runtime SDK from the launched container's `/configuration`
endpoint.

## Samples

The preferred Go authoring sample is `samples/GoAppHost`. It uses a Go
launcher program to declare:

- an `application.go-app` API rooted at `samples/GoApp/App`
- Configuration Store and Secrets Vault resources referenced by the Go app
- endpoint, health, logs, monitoring, and environment projection through
  Resource Manager

Run the launcher-owned local development host in a foreground terminal:

```bash
samples/GoAppHost/cloudshell.sh run-no-auth
```

From a second terminal, open the Web UI, list resources, and start the Go app:

```bash
samples/GoAppHost/cloudshell.sh open
samples/GoAppHost/cloudshell.sh resources
samples/GoAppHost/cloudshell.sh start-app
```

This is Go resource authoring and host launch support, not a Go provider
implementation. It targets `CloudShell.LocalDevelopmentHost` rather than
defining a custom CloudShell host.

Use `samples/GoContainerApp` when validating Go launcher authoring against the
container-app projection and runtime Configuration Store credential flow.

The older `samples/GoApp` full-host sample exists only for provider/host
composition coverage while the Go provider path is being proven. It should not
be used as the default Go app authoring example.
