# Programmatic Resources

CloudShell resources can be declared in code as an alternative to the Add
Resource UI. Declarations are a Control Plane concern: install the providers you
need in the Control Plane host, then declare provider-specific resources inside
`Resources`. This lets a host check in its baseline configuration
instead of relying on every developer or operator to add the same resources by
hand.

```csharp
var controlPlane = builder.Services
    .AddControlPlane()
    .AddExtension<CoreShellExtension>()
    .AddExtension<ResourceManagerExtension>()
    .AddConfigurationProvider()
    .AddApplicationProvider()
    .AddDockerProvider();

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

Provider packages expose specialized extension methods for their own resource
types. Built-in methods include:

- `AddConfigurationStore(...)` from `CloudShell.Providers.Configuration`.
- `AddExecutable(...)` and `AddExecutableApplication(...)` from
  `CloudShell.Providers.Applications`.

## Aspire-Like Application Workflow

Programmatic resources can also be used in an Aspire-like style for local
development. In this workflow, resource declarations return builder objects that
can be passed to executable applications. This keeps resource relationships
strongly connected in code instead of repeating string IDs at each call site.

Executable applications distinguish endpoint references from startup ordering:

- `WithReference(resource)` means the application should receive endpoint
  configuration for that resource.
- `WaitFor(resource)` means the application should wait for or start after that
  resource, without automatically receiving its endpoint.
- `WithServiceDiscovery()` enables service discovery variables for the
  application's referenced resources.

```csharp
var configuration = resources.AddConfigurationStore(
    "configuration:example",
    "Example Configuration");

var database = resources.Declare("managed", "postgres-main");

resources
    .AddExecutableApplication(
        "application:example-web-api",
        "Example Web API",
        executablePath: "dotnet",
        arguments: "run --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --no-launch-profile",
        endpoint: "http://localhost:5127")
    .WithReference(configuration)
    .WaitFor(database)
    .WithServiceDiscovery();
```

When the application starts, CloudShell maps referenced resource endpoints into
the .NET configuration shape used for service discovery, which provides a level
of compatibility with Aspire applications:

```text
services__<resource-name-or-id>__<endpoint-name-or-scheme>__0=<endpoint-address>
```

Endpoint variables are emitted only for referenced resources registered in the
same resource group as the application. Explicit application environment
variables are applied after generated values, so they can override generated
endpoint variables when needed.

Applications can consume the generated values through normal `IConfiguration`.
The reusable `CloudShell.Configuration` package also includes small helpers for
HttpClient-style setup:

```csharp
var endpoint = builder.Configuration.GetResourceEndpoint(
    "configuration-example",
    "entries");

var managementEndpoint = builder.Configuration.GetResourceEndpoint(
    "rabbitmq",
    "management");

if (endpoint is not null)
{
    builder.Services.AddHttpClient("configuration", client =>
    {
        client.BaseAddress = endpoint;
    });
}
```

The generic `ICloudShellResourceBuilder` still supports string IDs as a lower
level escape hatch, but typed executable application builders prefer passing
resource objects to `WithReference(...)`, `WithReferences(...)`, and
`WaitFor(...)`.

The host sample declares only `Example Configuration` programmatically. Other
resources are expected to be added through the Resource Manager UI unless a host
chooses to declare more of them in code.

## Runtime Behavior

Programmatic declarations are registered when CloudShell starts. They appear in
Resource Manager, participate in authorization, can be assigned to a resource
group with `WithResourceGroup(...)`, and can declare dependencies with the
provider-specific builder methods.

By default, programmatic resources are not persisted. The code declaration is
the source of truth for the current process. If the declaration is removed from
startup code, the resource no longer appears after restart unless it was
persisted separately.

Use `Persist()` when the declaration should materialize into provider-owned
configuration and the core resource registration store using the same provider
setup logic as the UI:

```csharp
controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:shared", "Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true")
        .Persist();
});
```

Persisted declarations are written during startup after the CloudShell database
has been initialized. After that, the resource can continue to exist even if the
declaration is removed from code.

`Persist()` does not overwrite an existing persisted resource. Use
`Persist(overwrite: true)` when checked-in configuration should replace the
current persisted provider configuration and registration metadata:

```csharp
controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:shared", "Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true")
        .Persist(overwrite: true);
});
```

## Provider Boundary

Programmatic resources follow the same persistence boundary as UI-created
resources:

- CloudShell owns the core resource registration, group assignment, and
  dependency metadata.
- Providers own resource-specific configuration such as executable command
  settings or configuration entries.

`Persist()` writes both sides through their existing stores. Without `Persist()`,
provider-specific configuration is kept in memory for the current process.

In split deployments, keep this API in the Control Plane host. The UI host
should discover resources through the Control Plane API rather than declaring
resources itself.

See [Hosting model](hosting-model.md).
