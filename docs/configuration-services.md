# Configuration Services

CloudShell includes a configuration provider that contributes `configuration.store`
resources. Each resource is a separate local configuration service with its own
entries, endpoint, access token, and resource group assignment.

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

Each service stores key-value entries:

- `Name`: the setting name.
- `Value`: the stored value.
- `Secret`: marks the entry as sensitive in UI and template export behavior.

Provider-owned state is persisted in:

```text
CloudShell.Host/Data/configuration-stores.json
```

The core CloudShell database still stores only platform metadata such as the
resource registration and group assignment.

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
GET /api/configuration/entries?resourceId=<resource-id>
GET /api/configuration/entries/{name}?resourceId=<resource-id>
```

Pass the token with either:

```text
Authorization: Bearer <token>
X-CloudShell-Configuration-Token: <token>
```

The configuration API is anonymous at the ASP.NET authentication layer because it
uses the resource token as its own authentication boundary. Missing tokens return
`401`; invalid tokens and missing services return `404`.

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
