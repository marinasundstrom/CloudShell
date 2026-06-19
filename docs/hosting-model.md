# Hosting Model

CloudShell separates the CloudShell environment from the host applications and
capability packages that compose it.

A CloudShell environment is the managed local, team-owned, or on-premise
cloud-like environment that users inspect and operate. It is anchored by
Control Plane resource state and installed capability packages, and it can be
served by one combined host or by separate Control Plane and UI hosts.

An on-premise CloudShell environment is a CloudShell instance running as its own
cloud environment, potentially for shared hosting. It owns its Control Plane
state, installed capabilities, provider integrations, and runtime placement
policy instead of acting as only a developer workstation process.

A CloudShell host application is an ASP.NET Core application owned by the
integrator. It can host the CloudShell UI, the Control Plane, or both. A
CloudShell capability package is an installable environment capability that can
add Control Plane resource providers, resource type definitions, programmatic
declarations, provider-owned runtime services, Resource Manager UI
integrations, shell views, and client helpers. Capability packages are
intended to be distributed as NuGet packages and installed into the host
application through CloudShell extension registrations.

This mirrors the practical separation between an application host and the
capabilities it loads: the host chooses the deployment shape and configuration;
capability packages contribute resource behavior and UI support.

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

UI-only hosts can install UI-only capability packages or the UI side of a
broader capability package. The package's Control Plane providers and
declarations must run in a Control Plane host when the UI needs resource data or
operations.

UI-only hosts persist CloudShell environment preferences, such as theme and
collapsed navigation, independently through the local
`ICloudShellUserSettingsProvider`. The default `Shell:EnvironmentSettings:Storage`
value is `Local`, which stores settings in `Data/environment-settings.json` under the UI
host content root. Settings are scoped to the authenticated user when one is
available; when authentication is not enabled, the provider uses a local
profile.

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
    .AddSecretsProvider()
    .AddApplicationProvider()
    .UseLocalDevelopmentDefaults();

cloudShell.Resources(resources =>
{
    resources
        .AddConfigurationStore("example")
        .WithDisplayName("Example Configuration")
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

Combined local-development hosts do not introduce a separate runner concept.
When resources are declared programmatically in the combined host, the same
host process starts the local Control Plane, installs the providers, maps the
UI, and gives provider implementations the local process context they need to
start executable, project, or container-backed resources. The local Control
Plane remains the owner of resource registration, lifecycle policy, dependency
startup, provider dispatch, logs, and API projection.

Combined hosts can install both sides of a capability package in one process.
For example, an application-resource package can register Control Plane
providers and programmatic declaration helpers, while the same package
contributes Resource Manager UI components for adding, updating, and inspecting
those resources.

Shell environment preferences still go through
`ICloudShellUserSettingsProvider`. Combined hosts can keep the default local
storage backend or set `Shell:EnvironmentSettings:Storage` to `ControlPlane` to store
them through the in-process Control Plane settings endpoint. These settings are
not part of the Control Plane resource model or `IControlPlane` domain facade.

Authentication is shared inside the ASP.NET Core process. The shell and Control
Plane use the same configured authentication scheme, cookie/session state, and
`ClaimsPrincipal`, so no OAuth token forwarding is required between the shell
and Control Plane services.

The default host models each configuration service instance as an individual
`configuration.store` resource. The configuration provider owns the local
runtime process and exposes resource logs directly, while still keeping store
definitions and Resource Manager integration under the configuration resource.

For container-backed local development, `UseLocalDevelopmentDefaults()`
registers Docker as the default container host and keeps lifecycle execution
on CloudShell's built-in default orchestrator unless the user has already saved
a different Resource Manager orchestration selection. Use `AddDockerProvider()`
plus `resources.AddDocker()` only when the Docker host should appear as a
managed container host with discovered child containers.

See `samples/CloudShell.ResourceHost`.

## Control-Plane-Only Host

Use the Control Plane-only registration when APIs, resource providers, and
persisted state should run separately from the UI.

```csharp
var builder = WebApplication.CreateBuilder(args);

var controlPlane = builder
    .AddCloudShellControlPlane()
    .AddConfigurationProvider()
    .AddSecretsProvider();

controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("shared")
        .WithDisplayName("Shared Configuration")
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
- User-scoped CloudShell environment settings for UI hosts that select
  `ControlPlane` settings storage.

Control Plane-only hosts install the Control Plane side of capability packages:
providers, stores, declarations, provider-owned services, templates, logs,
actions, and API-backed behavior. They do not need to install Resource Manager
UI integrations unless the same process also hosts the CloudShell UI.

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
    .AddConfigurationProvider()
    .AddSecretsProvider();

controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("shared")
        .WithDisplayName("Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true");
});
```

The UI host should not declare resources. It should discover resources through
`IControlPlane` so one shared Control Plane remains the authority for
checked-in configuration, persisted state, provider actions, and authorization.
Install cross-cutting capability packages on both sides when they include both
resource behavior and UI support: the Control Plane host receives the
provider/runtime registrations, and the UI host receives the shell or Resource
Manager integrations that talk to the remote Control Plane.
The remote settings adapter is separate from `IControlPlane`. Set
`Shell:EnvironmentSettings:Storage` to `ControlPlane` when the split UI should persist
CloudShell environment settings with the Control Plane instead of in the UI
process. When authentication is disabled on the Control Plane, settings fall
back to the Control Plane's local profile.

### Split-hosting authentication

Implemented today, split hosting configures the UI host with a remote Control
Plane base URL and registers the remote `IControlPlane` adapter. The adapter
can run without credentials, use a static bearer token, or acquire a
client-credentials token from the built-in Control Plane token authority.
Authored services running as CloudShell resources can also use the SDK-style
resource credential flow by passing `DefaultCloudShellResourceCredential` to
the remote Control Plane client. See [SDK clients](sdk-clients.md) for package
boundaries and usage:

```csharp
builder.Services.AddRemoteControlPlane(
    new Uri("https://control-plane.example.com"),
    new DefaultCloudShellResourceCredential(),
    ["ControlPlane.Access"]);
```

Authentication can still be configured through the existing ASP.NET Core
authentication modes, and the Control Plane API is protected when
authentication is enabled.

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

Protected API metadata for resource-owned runtime APIs is directional. The
current remote adapter credential support covers Control Plane calls only.

## Resource Manager Read-Only Mode

Hosts can disable Resource Manager write affordances in the UI by setting:

```json
{
  "ResourceManager": {
    "ReadOnly": true
  }
}
```

The same setting can be supplied through environment variables as
`ResourceManager__ReadOnly=true`.

Read-only mode is a UI policy for inspection-focused environments, especially
local-development hosts where programmatic declarations should remain
authoritative. It hides or disables Resource Manager creation, lifecycle,
update, import, identity-provisioning, image-deployment, and delete controls in
the hosted UI. It is not a Control Plane security boundary; authorization and
API enforcement still belong to the Control Plane.

## Orchestrator Dependency Behavior

Resource Manager can control what happens when a Start or Restart action tries
to auto-start dependencies and one of those dependencies cannot start. The
default is fail-fast:

```json
{
  "ResourceManager": {
    "DependencyStartFailureBehavior": "FailAction"
  }
}
```

Set `DependencyStartFailureBehavior` to `WarnAndContinue` when the orchestrator
should record dependency-start warnings but still attempt to start the
requested resource. The same setting can be changed from Resource Manager
Settings unless appsettings provides an explicit value.

## Persistence

Programmatic declarations are startup configuration by default. Calling
`Persist()` tells the owning provider to apply the resource through the same
setup path used by the UI. Existing persisted state is left unchanged unless the
declaration uses `Persist(overwrite: true)`.

This is the transition from code-first local development to Control
Plane-managed environment state. In the local combined-host flow, developers can
model and run a distributed app entirely from programmatic declarations. Once a
declaration is persisted, the Control Plane and provider stores become the
record for that environment, and later local changes should be treated as
updates to promote into that Control Plane state.

Deployment is a separate mechanism. `Persist()` records the resources and
provider configuration; it does not deploy them to a target host. An on-premise
CloudShell environment should be treated as a deployment target: a standalone
CloudShell cloud environment, potentially for shared hosting, similar in role
to future targets such as Azure or AWS. Deployment should use the orchestrator
deployment API once that API is available. Whether deployment is triggered from
a CLI, Resource Manager UI, or another automation surface is a later decision.
Until then, shared or on-premise environments should keep declarations in the
Control Plane host that owns the environment, while UI hosts remain clients of
that Control Plane.

See [Programmatic resources](programmatic-resources.md) for the declaration API.
