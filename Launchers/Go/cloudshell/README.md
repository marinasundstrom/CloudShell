# CloudShell Go Launcher

This experimental Go package lets Go launcher programs author CloudShell
ResourceTemplate documents and apply them to a CloudShell host.

It is launcher support only. CloudShell provider authoring remains C#-only;
the Go launcher emits resource declarations for providers installed in the
target CloudShell host, such as the built-in `application.go-app` provider.

The initial package includes builders for:

- Go app resources
- Configuration Store resources
- Secrets Vault resources
- Load Balancer resources
- Host network resources

Secrets Vault certificates can be passed directly to HTTPS load-balancer
entrypoints:

```go
secrets := app.AddSecretsVault("secrets").
	WithSeed(func(seed *cloudshell.SecretsVaultSeed) {
		seed.Certificate("ApiTls", localDevelopmentPem, "application/x-pem-file")
	})

api := app.AddGoApp("api", "src/api")

app.AddLoadBalancer("edge").
	WithProvider("traefik").
	ExposeHTTPS(secrets.Certificate("ApiTls")).
	MapHost("api.local", api, 8080, "https")
```

It exposes the standard launcher verbs:

- `template` emits the ResourceTemplate JSON.
- `apply` applies the template to an already-running Control Plane.
- `start` starts or reuses a daemon-style local host, then applies the
  template.
- `run` starts the local host in the foreground, applies the template, and
  keeps the host tied to the launcher process.

See `samples/GoAppHost` for the current sample.
