# ASP.NET Core Applications

Use the ASP.NET Core project resource type for local .NET web projects. These
resources project as `application.aspnet-core-project` and expose project-shaped
attributes such as project path, application arguments, and hot reload mode.

ASP.NET Core project resources are not plain executable application resources.
They are project resources with a provider-owned process runner. Resource
Manager shows the project shape, while the provider hides the generated
`dotnet run` or `dotnet watch` command used to host the project.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [Executable applications](executable-applications.md) and
[Container apps](container-apps.md).

## Declaration

Programmatic declarations use `AddAspNetCoreProject(...)` when specifying a
resource ID, or `AddAspNetCoreProjectFromName(...)` when CloudShell should
derive the resource ID from the name:

```csharp
resources
    .AddAspNetCoreProject(
        "application:example-web-api",
        "Example Web API",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .WithReference(configuration);
```

By default, CloudShell starts ASP.NET Core project resources with hot reload:

```bash
dotnet watch --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj run --no-launch-profile
```

Pass `hotReload: false` to use plain `dotnet run --no-launch-profile` instead.
Pass `applicationArguments` when the hosted app should receive command-line
arguments. CloudShell appends those arguments after the hidden `dotnet` runner
arguments.

## Endpoints

ASP.NET Core project resources always get an HTTP endpoint. If the declaration
omits `endpoint`, CloudShell assigns a stable local port. If the declaration
sets `endpoint`, that URL fixes the displayed port. In both cases CloudShell
injects the resolved URL into `ASPNETCORE_URLS` when the process starts, so the
project listens on the same endpoint that Resource Manager displays.

Use endpoint builder methods to model additional or named endpoints:

```csharp
resources
    .AddAspNetCoreProject(
        "application:example-web-api",
        "Example Web API",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
        applicationArguments: "--seed")
    .WithHttpEndpoint(port: 5127)
    .WithEndpointPort("dashboard", targetPort: 18888, port: 18888, protocol: "http");
```

## Service Discovery

ASP.NET Core project references follow the Aspire model:
`WithReference(...)` adds service discovery configuration for the referenced
resource and enables the service discovery environment variable mapping. A
client can use logical URIs such as `https+http://example-web-api` or
`https+http://_dashboard.example-web-api` when service discovery is enabled in
the consuming app.
