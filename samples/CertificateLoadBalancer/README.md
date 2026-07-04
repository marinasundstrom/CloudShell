# Certificate Load Balancer

This sample declares a Secrets Vault certificate and consumes it from an HTTPS
load-balancer entrypoint:

```csharp
var certificates = resources
    .AddSecretsVault("edge-certificates")
    .WithSeed(seed => seed.Certificate(CreateDevelopmentCertificate()));

resources
    .AddLoadBalancer("public")
    .WithProvider("traefik")
    .ExposeHttps(certificates.Certificate("EdgeTls"), port: 4443)
    .MapHost("secure.cloudshell.local", webResource, port: 80, entrypoint: "https");
```

Applying the load-balancer action resolves the vault-backed certificate,
writes provider-owned PEM files under `Data/traefik/certificates`, and writes
Traefik dynamic configuration that references those files. The load balancer
resource stores only the certificate reference.

Run it with:

```bash
dotnet run --project samples/CertificateLoadBalancer/CloudShell.CertificateLoadBalancer.csproj -- --urls http://localhost:5012
```

Open `http://127.0.0.1:5012/resources`, start **HTTPS Web App**, then start
**Certificate Load Balancer** or invoke **Apply load balancer configuration**.
The sample uses a generated self-signed development certificate for
`secure.cloudshell.local`.
