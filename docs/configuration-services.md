# Configuration Services

CloudShell includes a configuration provider that contributes `configuration.store`
resources. Each resource is a separate local configuration store with its own
entries, endpoint, access token, and resource group assignment. Each store owns
the runtime process that serves its HTTP API; it does not register that process
as a separate application resource.

Use separate configuration services when different projects or resource groups
need different settings or secrets. For example, a frontend/API group can depend
on one configuration service while a worker group depends on another.

## Resource Model

A configuration service is added from `/resources/add` by choosing
**Configuration service**. It can be assigned to any resource group, or left
in the default group.

Control Plane hosts can also declare configuration services in checked-in
startup code:

```csharp
controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:example", "Example Configuration")
        .WithEntries(
        [
            new("SampleMessage", "Hello from CloudShell configuration"),
            new("SampleSecret", "local-development-secret", IsSecret: true)
        ]);
});
```

Each store stores key-value entries:

- `Name`: the setting name.
- `Value`: the stored value.
- `Secret`: marks the entry as sensitive in UI and template export behavior.

Provider-owned state is persisted in:

```text
CloudShell.Host/Data/configuration-stores.json
```

The core CloudShell database still stores only platform metadata such as the
resource registration and group assignment.

## Service Runtime

Each configuration store owns a local service process. For a store such as
`configuration:example`, the default process ID is:

```text
configuration-service-configuration-example
```

The current runtime implementation starts:

```bash
dotnet run --project CloudShell.ConfigurationService/CloudShell.ConfigurationService.csproj --no-launch-profile --urls http://localhost:5138
```

The actual URL is stored on the configuration store definition. If the user does
not provide one, the provider generates a stable endpoint from
`ServiceBasePort`, `ServiceHost`, and the resource ID. The default generated
range starts from:

```text
http://localhost:5138
```

Configure service instance defaults in the host through
`AddConfigurationProvider(...)`, including:

```text
ServiceBasePort
ServiceHost
ServiceUrlScheme
ServiceProjectPath
ServiceWorkingDirectory
ServiceProcessIdPrefix
```

The service process receives the provider-owned store file path and its own
configuration resource ID through `CloudShell:ConfigurationService:DefinitionsPath`
and `CloudShell:ConfigurationService:ResourceId`. That resource ID filter is
what keeps each process scoped to one configuration service instance.

Configuration services expose their own resource log in the Logs view. The log
uses the same local process runner as executable applications, so stdout,
stderr, and lifecycle entries are available without modeling the service as an
`application.executable` resource.

The runtime is intentionally an implementation detail of the configuration
resource type. Today it is a host-local process for development; a future
configuration provider can replace that with a container image while keeping the
resource model and logs attached to the configuration service resource.

## Application Access

Executable applications receive configuration service connection details through
resource dependencies. If an application depends on a configuration service,
CloudShell injects environment variables when the process starts:

```text
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_STORE_ID
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_TOKEN
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_STORE_ID
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_ENDPOINT
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_TOKEN
```

`<SERVICE_NAME>` and `<RESOURCE_ID>` are uppercased and normalized for
environment variable names. The resource-ID variables avoid collisions when two
groups use similarly named services.

Applications fetch settings from:

```text
GET <configuration-service-endpoint>/api/configuration/entries?resourceId=<resource-id>
GET <configuration-service-endpoint>/api/configuration/entries/{name}?resourceId=<resource-id>
```

Pass the token with either:

```text
Authorization: Bearer <token>
X-CloudShell-Configuration-Token: <token>
```

The configuration API is hosted by each standalone service application instance
and is anonymous at the ASP.NET authentication layer because it uses the resource
token as its own authentication boundary. Missing tokens return `401`; invalid
tokens and missing services return `404`.

## Microsoft Configuration API

Applications can consume CloudShell configuration through the reusable
`CloudShell.Configuration` project. It implements the standard
`Microsoft.Extensions.Configuration` provider contract:

```csharp
using CloudShell.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddCloudShellConfiguration();
```

By default, the provider discovers the first injected
`CLOUDSHELL_CONFIGURATION_*_ENDPOINT` and matching `*_TOKEN` environment
variable pair. To select a specific service or configure explicit connection
details:

```csharp
builder.Configuration.AddCloudShellConfiguration(options =>
{
    options.ServiceName = "Example Configuration";
    options.Timeout = TimeSpan.FromSeconds(5);
});
```

Loaded entries are available through normal `IConfiguration` lookup:

```csharp
var value = builder.Configuration["SampleMessage"];
```

Provider diagnostics are exposed under `CloudShell:Configuration:*`, including
`Status`, `Detail`, `Source`, `LoadedKeys`, and `SecretKeys`. The provider does
not throw when the service is unavailable; it records unavailable status so the
application can continue running and log the state.

## Sample

The host declares an `Example Configuration` service programmatically. If an
executable application depends on that service, the sample app can use
`CloudShell.Configuration` to read the injected CloudShell endpoint and token,
load settings at startup, log connection failures, and continue running if the
service is unavailable.

When the sample app is started from Resource Manager, open:

```text
http://localhost:5127/configuration
```

The sample returns the provider status and loaded keys from `IConfiguration`.
Secret values are masked in the response.

See [Programmatic resources](programmatic-resources.md) for the declaration and
persistence model.

## Templates

Configuration services support resource group templates. Export includes
non-secret entry values. Secret entries are exported as placeholders with an
empty value so templates do not leak secrets by default. Import creates a new
configuration service and generates a fresh access token.
