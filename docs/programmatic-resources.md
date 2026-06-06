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

Executable application declarations can opt in to Aspire-style endpoint
environment variables for referenced resources:

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
    .WithAspireEndpointEnvironmentVariables();
```

For executable applications, `WithReference(...)` means the application should
receive endpoint configuration for that resource. Use `WaitFor(...)` when the
application only needs startup ordering against another resource.

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
