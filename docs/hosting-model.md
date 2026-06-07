# Hosting Model

CloudShell supports three registration shapes:

- UI only: host the CloudShell shell and custom UI extensions.
- Control Plane only: host resource providers, stores, declarations, and APIs.
- Combined: host both in one ASP.NET Core process.

The explicit UI and Control Plane registrations are useful for split
deployments. Combined hosts compose those same registrations in one process.

The reusable shell UI and hosting helpers live in `CloudShell.Hosting`, a Razor
class library that references ASP.NET Core. Web SDK projects reference
`CloudShell.Hosting` and keep their own `Program.cs`, appsettings, environment,
and scenario-specific extension registrations.

## UI-Only Host

Use the UI-only host when an application wants CloudShell navigation, layout,
localization, authentication plumbing, and extension-provided views without
hosting Control Plane stores or APIs.

```csharp
using CloudShell.Hosting;
using CloudShell.Hosting.Components;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddCloudShellUi()
    .AddExtension<SampleWorkspaceExtension>();

var app = builder.Build();

await app.UseCloudShellUiAsync();
app.MapCloudShellUi<App>();

app.Run();
```

In this shape, extension views can contribute routes and navigation through the
same extension model as a full host. Resource Manager and Control Plane
endpoints are not registered unless the host adds them separately.

See `samples/CloudShell.UiExtensionHost`.

## Combined Host

For local development, the UI and Control Plane can run in the same ASP.NET Core
process. This is the shape used by the `CloudShell.Host` development sample.

```csharp
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;

var builder = WebApplication.CreateBuilder(args);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddConfigurationProvider()
    .AddApplicationProvider()
    .AddDockerProvider();

cloudShell.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:example", "Example Configuration")
        .WithEntry("SampleMessage", "Hello from checked-in configuration");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
```

In this mode, the UI consumes the same `IControlPlane` abstraction as a split
host, but the registered implementation is in-process and backed by Control
Plane services.

Authentication is shared inside the ASP.NET Core process. The shell and Control
Plane use the same configured authentication scheme, cookie/session state, and
`ClaimsPrincipal`, so no OAuth token forwarding is required between the shell
and Control Plane services.

The default host models each configuration service instance as an individual
`configuration.store` resource. The configuration provider owns the local
runtime process and exposes resource logs directly, while still keeping store
definitions and Resource Manager integration under the configuration resource.

See `samples/CloudShell.ResourceHost`.

## Control-Plane-Only Host

Use the Control Plane-only registration when APIs, resource providers, and
persisted state should run separately from the UI.

```csharp
var builder = WebApplication.CreateBuilder(args);

var controlPlane = builder
    .AddCloudShellControlPlane()
    .AddConfigurationProvider();

controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:shared", "Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
```

The Control Plane host owns:

- Resource providers.
- Resource declarations.
- Provider-owned resource configuration.
- Resource registrations, groups, and dependencies.
- Resource procedures and logs.
- The versioned Control Plane API.

## Split Host

For shared on-premise environments, the UI and Control Plane can be hosted
separately.

The UI host owns:

- Blazor shell rendering.
- Navigation and layout.
- An `IControlPlane` implementation backed by the remote Control
  Plane API.
- Authentication challenge UX when required by the deployment.
- A Control Plane credential used by the remote adapter.

```csharp
builder.Services.AddRemoteControlPlane(options =>
{
    options.BaseAddress = new Uri("https://control-plane.example.com");
});
```

Declarative resources must be configured in the Control Plane host:

```csharp
var controlPlane = builder
    .AddCloudShellControlPlane()
    .AddConfigurationProvider();

controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:shared", "Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true");
});
```

The UI host should not declare resources. It should discover resources through
`IControlPlane` so one shared Control Plane remains the authority for
checked-in configuration, persisted state, provider actions, and authorization.

### Split-hosting authentication

Implemented today, split hosting configures the UI host with a remote Control
Plane base URL and registers the remote `IControlPlane` adapter. Authentication
can still be configured through the existing ASP.NET Core authentication modes,
and the Control Plane API is protected when authentication is enabled.

The direction is that split-hosting authentication remains primarily a
configuration concern:

- Configure the UI host for user sign-in with the shared OIDC authority.
- Configure the UI host with a Control Plane credential that can authenticate
  to the Control Plane protected API resource on behalf of the signed-in user.
- Configure the remote `IControlPlane` adapter to use that credential and attach
  the required authentication metadata to HTTP calls.
- Configure the Control Plane host to validate credentials from the same
  authority or trusted authentication abstraction.

For OAuth-based deployments, the remote call should carry the token in the
standard authorization header:

```text
Authorization: Bearer <control-plane-access-token>
```

For server-rendered Blazor or a BFF-style UI host, the browser keeps only the
UI host's session cookie. The UI server's configured Control Plane credential
acquires the authentication material and the remote adapter forwards it to the
Control Plane. Browser JavaScript should only hold provider credentials when
the deployment intentionally uses a public client architecture.

The credential abstraction should be similar to Azure SDK credentials: the host
configures a credential, the remote adapter requests authentication material
for the target protected API resource, and transport code attaches the
resulting headers or other metadata. Shell views and extensions continue to call
`IControlPlane` without knowing how the deployment authenticated.

For Azure-style OAuth providers, model each separately hosted CloudShell
service as its own API resource. The Control Plane might use an identifier such
as `api://cloudshell-control-plane`; delegated scopes and app-only permissions
are defined on that resource, and the Control Plane validates that incoming
tokens were issued for it. Other providers can use equivalent service
identities, API keys, certificates, signed requests, or mesh identities.

Apply that model only to resources that expose an independently protected API.
Many CloudShell resources are managed through the Control Plane and do not need
their own provider-specific authentication registration. When a resource
provider does expose a protected runtime API, its setup needs both CloudShell
resource registration and protected API authentication metadata so clients can
authenticate to that runtime API.

The runtime service still owns enforcement. If the protected API is implemented
by a process or container started by CloudShell, that process or container must
validate credentials on its own HTTP endpoints. CloudShell can pass endpoint,
credential, and dependency metadata to the service, but it is not an automatic
reverse proxy or policy enforcement point for every resource API.

The credential abstraction and protected API metadata described here are
directional. The current remote adapter does not yet expose a provider-neutral
credential API.

## Persistence

Programmatic declarations are startup configuration by default. Calling
`Persist()` tells the owning provider to apply the resource through the same
setup path used by the UI. Existing persisted state is left unchanged unless the
declaration uses `Persist(overwrite: true)`.

See [Programmatic resources](programmatic-resources.md) for the declaration API.
