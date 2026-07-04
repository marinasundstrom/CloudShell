# SDK Clients

CloudShell SDK clients are package-ready client libraries for authored
services, integrations, and built-in services that call CloudShell-protected
APIs.

The SDK clients should not drag in the full Control Plane or resource-model
abstractions unless they expose that domain surface directly. Service-specific
clients depend on the small shared client credential package and their own
request/response contracts.

## Projects

- `CloudShell.Client`: shared SDK credential primitives, including
  `CloudShellResourceCredential`, `DefaultCloudShellResourceCredential`, and
  the environment-backed credential source. This package intentionally does not
  reference `CloudShell.Abstractions`.
- `CloudShell.ControlPlane.Client`: remote domain client for the Control Plane
  API. This package references `CloudShell.Abstractions` because it exposes the
  domain-shaped `IControlPlane`, `IResourceManager`, resource, log, and trace
  contracts.
- `CloudShell.Configuration.Client`: Configuration Store SDK client. It
  references `CloudShell.Client`, not the full Control Plane abstractions, and
  owns the Microsoft `IConfiguration` integration for configuration entries.
- `sdk/typescript/configuration-client`: experimental TypeScript
  Configuration Store client. It is separate from the TypeScript hosting
  package and is used by Node.js applications at runtime to call a
  Configuration Store endpoint.
- `CloudShell.Secrets.Client`: Secrets Vault SDK client. It references
  `CloudShell.Client`, not the full Control Plane abstractions, and owns the
  Microsoft `IConfiguration` integration for vault secrets.
- `CloudShell.RabbitMQ.Client`: RabbitMQ credential and connection helper. It
  resolves provider-owned RabbitMQ username, password, and virtual-host access
  from a CloudShell resource identity credential and can produce normal
  `RabbitMQ.Client` connection factories.
- `CloudShell.SqlServer.Client`: SQL Server credential and connection helper.
  It resolves provider-owned SQL connection strings from a CloudShell resource
  identity credential and returns normal `Microsoft.Data.SqlClient`
  connections.

Future service-specific SDK clients should follow the same `.Client`
convention and avoid depending on `CloudShell.Abstractions` unless the client
explicitly exposes Control Plane domain contracts.

## Resource Credentials

Authored services running as CloudShell resources should use
`DefaultCloudShellResourceCredential` unless they need to test or override a
specific credential source:

```csharp
using CloudShell.Client.Authentication;

var credential = new DefaultCloudShellResourceCredential();
```

In ASP.NET Core services, register the credential once and supply it to SDK
clients from the application service model:

```csharp
using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;
using CloudShell.Secrets.Client;

builder.Configuration.AddCloudShellConfigurationStore();
builder.Configuration.AddCloudShellSecretsVault();
builder.Services.AddSingleton<CloudShellResourceCredential>(_ => new DefaultCloudShellResourceCredential());
builder.Services.AddSingleton<CloudShellServiceClients>();

app.MapGet("/configuration", async (
    CloudShellServiceClients clients,
    CancellationToken cancellationToken) =>
{
    var configuration = clients.CreateConfigurationStoreClient();
    return await configuration.GetEntriesAsync(cancellationToken);
});

sealed class CloudShellServiceClients(CloudShellResourceCredential credential)
{
    public ConfigurationStoreClient CreateConfigurationStoreClient() =>
        ConfigurationStoreClient.FromEnvironment(credential);

    public SecretsVaultClient CreateSecretsVaultClient() =>
        SecretsVaultClient.FromEnvironment(credential);
}
```

The first credential source reads the environment contract injected by the
resource provider that starts the workload process or container. Environment
variables are the default injection mechanism because they work consistently
for local executables, direct container starts, and descriptor-driven container
orchestration:

```text
CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT
CLOUDSHELL_IDENTITY_CLIENT_ID
CLOUDSHELL_IDENTITY_CLIENT_SECRET
CLOUDSHELL_IDENTITY_SCOPE
```

The convention is the same for local processes, direct container starts, and
descriptor-driven container orchestration:

- Resource providers inject these variables only for resources that have a
  resolved identity binding and a supported credential acquisition mechanism.
- Orchestrators that materialize workload descriptors must pass the variables
  through to the workload container or process unchanged.
- Authored services use `DefaultCloudShellResourceCredential` or an explicit
  `CloudShellResourceCredential`; service clients use that credential to
  request bearer tokens and attach `Authorization: Bearer ...`.
- Protected resource services authorize the bearer token through resource
  permission claims. For example, Secrets Vault checks
  `SecretsVault.ReadSecrets` on the vault resource before returning a secret.
- The credential values are runtime inputs. They must not be copied into
  resource attributes, generated UI details, logs, activity messages, or other
  user-facing projections.

Credentials resolved from CloudShell-protected service endpoints should be
treated as access material for opening native client connections. Do not
resolve them for every operation, and do not store them as durable application
configuration. Resolve before opening a native connection, reuse the native
connection according to that client's normal lifetime rules, and resolve again
when a new connection is needed after `expiresOn`, a login failure, an
access-refused failure, or a provider-driven reconnect.

Service endpoints are a separate concern. Configuration Store, Secrets Vault,
and other resource-backed services should be discovered through the same
service discovery and networking model as other services. Until network-level
service discovery is available, applications configure the SDK endpoint
variables explicitly or receive them from the current local development host
integration.

The credential contract is public preview. Future sources can add managed
identity endpoints, federated workload identity, local development
credentials, external provider plugins, or platform-specific brokers without
changing service-client code. Local development credentials may be backed by a
file or developer profile on disk, similar to Azure SDK developer credentials;
that stored credential is a credential source in the chain, not a replacement
for the resource identity binding or permission grants.

## Control Plane Client

Use `CloudShell.ControlPlane.Client` when a service needs the domain-shaped
Control Plane API:

```csharp
using CloudShell.Client.Authentication;
using CloudShell.ControlPlane.Client;

var credential = new DefaultCloudShellResourceCredential();
var controlPlane = new RemoteControlPlane(
    new Uri("https://control-plane.example.com"),
    credential,
    ["ControlPlane.Access"]);

var resources = await controlPlane.ListResourcesAsync();
```

DI registration supports the same credential object:

```csharp
builder.Services.AddRemoteControlPlane(
    new Uri("https://control-plane.example.com"),
    new DefaultCloudShellResourceCredential(),
    ["ControlPlane.Access"]);
```

## Configuration Store Client

Use `CloudShell.Configuration.Client` for direct Configuration Store service
calls:

```csharp
using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;

var credential = new DefaultCloudShellResourceCredential();
var configuration = ConfigurationStoreClient.FromEnvironment(credential);
var entries = await configuration.GetEntriesAsync();
```

Applications that configure Configuration Store endpoint discovery use
variables such as:

```text
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_STORE_ID
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_STORE_ID
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_ENDPOINT
```

The endpoint points at the protected entries collection. The client requests a
resource identity token and sends it as a bearer token on each service call.
CloudShell can set these variables through the current application-level
service discovery mapping when the store is referenced; callers may also set
them explicitly.

The same package provides the configuration-provider integration:

```csharp
using CloudShell.Configuration.Client;

builder.Configuration.AddCloudShellConfigurationStore(options =>
{
    options.ServiceName = "Sample App Settings";
});
```

Provider diagnostics are exposed under `CloudShell:ConfigurationStore:*`,
including `Status`, `Detail`, `Source`, `LoadedKeys`, and `SecretKeys`.
By default, `--` in entry names maps to the .NET configuration `:`
delimiter, so a stored entry named `Orders--Api--BaseUrl` is available through
`Configuration["Orders:Api:BaseUrl"]`.

The experimental TypeScript client follows the same service boundary for
Node.js applications:

```ts
import {
  ConfigurationStoreClient,
  StaticTokenCredential
} from "@cloudshell/configuration-client";

const configuration = ConfigurationStoreClient.fromEnvironment({
  credential: new StaticTokenCredential(process.env.CLOUDSHELL_TOKEN ?? "")
});

const entries = await configuration.getEntries();
const values = await configuration.toObject();
```

This client reads the injected
`CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT` variables, sends
`Authorization: Bearer ...`, and can map `--` in entry names to `:` keys. It
does not declare resources or launch hosts; that remains the responsibility of
the TypeScript hosting package and the CloudShell CLI.

See `samples/TypeScriptConfigurationClient` for a minimal Node.js application
that reads a Configuration Store endpoint from the environment and calls it
with a bearer token.

The experimental Java client follows the same boundary with Java-native
classes under `sdk/java/cloudshell`:

```java
import com.cloudshell.sdk.ConfigurationStoreClient;

ConfigurationStoreClient configuration =
    ConfigurationStoreClient.fromEnvironment();

String message = configuration
    .getEntry("Sample--Message")
    .map(entry -> entry.value())
    .orElse("Default message");
```

`ConfigurationStoreClient.toProperties(true)` maps portable CloudShell
hierarchy names such as `Sample--Message` into Java property names such as
`Sample.Message`.

## Secrets Vault Client

Use `CloudShell.Secrets.Client` for direct Secrets Vault service calls:

```csharp
using CloudShell.Client.Authentication;
using CloudShell.Secrets.Client;

var credential = new DefaultCloudShellResourceCredential();
var vault = SecretsVaultClient.FromEnvironment(credential);
var secret = await vault.GetSecretAsync("sample-api-key");
```

Applications that configure Secrets Vault endpoint discovery use variables
such as:

```text
CLOUDSHELL_SECRETS_<VAULT_NAME>_VAULT_ID
CLOUDSHELL_SECRETS_<VAULT_NAME>_ENDPOINT
CLOUDSHELL_SECRETS_<RESOURCE_ID>_VAULT_ID
CLOUDSHELL_SECRETS_<RESOURCE_ID>_ENDPOINT
```

The endpoint points at the protected vault secrets collection. The client
requests a resource identity token and sends it as a bearer token on each
service call.
CloudShell can set these variables through the current application-level
service discovery mapping when the vault is referenced; callers may also set
them explicitly.

The same package provides the configuration-provider integration for secrets:

```csharp
using CloudShell.Secrets.Client;

builder.Configuration.AddCloudShellSecretsVault(options =>
{
    options.VaultName = "Sample App Secrets";
});
```

Secret names are loaded as configuration keys. By default, `--` in secret
names maps to the .NET configuration `:` delimiter, matching the Azure Key
Vault-style convention. Provider diagnostics are exposed under
`CloudShell:SecretsVault:*`, including `Status`, `Detail`, `Source`, and
`LoadedKeys`.

The Java SDK also includes a `SecretsVaultClient`:

```java
import com.cloudshell.sdk.SecretsVaultClient;

SecretsVaultClient secrets = SecretsVaultClient.fromEnvironment();
String secret = secrets
    .getSecret("Sample--Secret")
    .map(value -> value.value())
    .orElse("");
```

Java clients read `CLOUDSHELL_CONFIGURATION_*_ENDPOINT` and
`CLOUDSHELL_SECRETS_*_ENDPOINT` variables and use
`CLOUDSHELL_CONFIGURATION_TOKEN`, `CLOUDSHELL_SECRETS_TOKEN`,
`CLOUDSHELL_CONTROL_PLANE_TOKEN`, or `CLOUDSHELL_TOKEN` for bearer tokens.

## SQL Server Client

Use `CloudShell.SqlServer.Client` when a workload should connect to SQL Server
through CloudShell resource identity instead of receiving an administrator
password or static connection string:

```csharp
using CloudShell.SqlServer.Client;

builder.Services.AddCloudShellSqlServerClient(options =>
{
    options.SqlServerResourceName = "application-topology-sql-server";
});

app.MapGet("/database", async (
    CloudShellSqlConnectionFactory sql,
    CancellationToken cancellationToken) =>
{
    await using var connection = await sql.OpenConnectionAsync(
        "application-topology-sql-server",
        "application_topology",
        cancellationToken);

    // Use Microsoft.Data.SqlClient as usual.
});
```

The client reads `CLOUDSHELL_SQL_CREDENTIAL_ENDPOINT` by default and uses
`DefaultCloudShellResourceCredential` to authorize the credential request.

### Entity Framework Core

EF Core registration is mostly synchronous, while CloudShell SQL credential
resolution is async. Avoid putting a resolved CloudShell SQL connection string
into `appsettings.json`, and avoid `AddDbContextPool` for rotating credentials.
Use an async factory that resolves credentials when a context is needed:

```csharp
using CloudShell.SqlServer.Client;
using Microsoft.EntityFrameworkCore;

builder.Services.AddCloudShellSqlServerClient(options =>
{
    options.SqlServerResourceName = "application-topology-sql-server";
});
builder.Services.AddScoped<AppDbContextFactory>();

public sealed class AppDbContextFactory(ICloudShellSqlCredentialResolver sql)
{
    public async ValueTask<AppDbContext> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var credential = await sql.ResolveCredentialAsync(
            new CloudShellSqlConnectionRequest(
                "application-topology-sql-server",
                "application_topology"),
            cancellationToken);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(credential.ConnectionString)
            .Options;

        return new AppDbContext(options);
    }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) :
    DbContext(options);
```

Use the factory from request handlers or application services:

```csharp
app.MapGet("/orders", async (
    AppDbContextFactory contexts,
    CancellationToken cancellationToken) =>
{
    await using var db = await contexts.CreateAsync(cancellationToken);
    return await db.Set<Order>().ToListAsync(cancellationToken);
});
```

This keeps credential exchange at the connection boundary. If a provider
returns `expiresOn`, create new contexts through the factory after expiry. If
SQL login fails because credentials rotated or grants changed, discard the
context and create a new one so the factory resolves fresh credentials.

## RabbitMQ Native Clients

Use `CloudShell.RabbitMQ.Client` when a .NET workload should connect to
RabbitMQ through CloudShell resource identity instead of receiving the broker
administrator password or hand-coding the credential endpoint exchange:

```csharp
using CloudShell.RabbitMQ.Client;

builder.Services.AddCloudShellRabbitMQClient(options =>
{
    options.RabbitMQResourceName = "application.rabbitmq:rabbitmq";
    options.HostName = "localhost";
    options.Port = 5672;
    options.Permission = CloudShellRabbitMQPermissions.Publish;
});

app.MapPost("/publish", async (
    CloudShellRabbitMQConnectionFactory rabbitMQ,
    CancellationToken cancellationToken) =>
{
    var factory = await rabbitMQ.CreateConnectionFactoryAsync(cancellationToken);

    using var connection = factory.CreateConnection("orders-api");
    using var channel = connection.CreateModel();

    // Use RabbitMQ.Client as usual.
});
```

The client reads `CLOUDSHELL_RABBITMQ_CREDENTIAL_ENDPOINT` by default and uses
`DefaultCloudShellResourceCredential` to authorize the credential request. It
requests a CloudShell resource identity token for the RabbitMQ permission that
the connection needs unless explicit token scopes are configured.

CloudShell does not wrap the RabbitMQ protocol itself. Workloads still use
their normal RabbitMQ client library after the CloudShell client resolves
broker-native username, password, and virtual host from
`/api/rabbitmq/v1/credentials`.

### MassTransit

MassTransit configures the RabbitMQ host when the bus is built. Resolve
CloudShell RabbitMQ credentials during application startup with
`CloudShellRabbitMQCredentialResolver`, then pass the resolved values into
`UsingRabbitMq`:

```csharp
using CloudShell.RabbitMQ.Client;
using MassTransit;

var resolver = CloudShellRabbitMQCredentialResolver.FromEnvironment(
    rabbitMQResourceName: "application.rabbitmq:rabbitmq");
var rabbit = await resolver.ResolveCredentialAsync(
    new CloudShellRabbitMQCredentialRequest(
        "application.rabbitmq:rabbitmq",
        CloudShellRabbitMQPermissions.Configure));
builder.Services.AddSingleton(rabbit);

builder.Services.AddMassTransit(bus =>
{
    bus.AddConsumer<OrderSubmittedConsumer>();

    bus.UsingRabbitMq((context, cfg) =>
    {
        var credential = context.GetRequiredService<CloudShellRabbitMQCredential>();
        cfg.Host("localhost", 5672, credential.VirtualHost, host =>
        {
            host.Username(credential.Username);
            host.Password(credential.Password);
        });

        cfg.ConfigureEndpoints(context);
    });
});
```

Do not resolve credentials per message. MassTransit keeps a persistent AMQP
connection and reconnects using the host credentials it was configured with.
For the current local RabbitMQ provider, `expiresOn` is normally unset and the
generated credentials are stable for the resource identity. If a future
provider returns expiring RabbitMQ credentials, schedule a controlled bus
restart before expiry so startup can resolve fresh credentials. If RabbitMQ
closes the connection with an authentication or access-refused failure, stop
the bus, resolve credentials again, and start it with the new values.

`CloudShellRabbitMQCredential` carries `Username`, `Password`, `VirtualHost`,
and optional `ExpiresOn`.

## Stability

These SDK clients are public preview APIs. CloudShell owns the client
credential contract and the built-in service-client contracts, but package
names, constructor options, credential chain sources, and response types may
evolve before the MVP API is declared stable.
