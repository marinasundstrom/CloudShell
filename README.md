# CloudShell

CloudShell is an extensible, self-hosted cloud-portal shell for local development and on-premise environments. It uses Blazor, Fluent UI, and .NET 11 preview, with an operational experience inspired by the .NET Aspire Dashboard.

The goal is to make it possible to build your own cloud-platform shell: a place where teams can register resources, group them by project, inspect endpoints and state, and let extensions add focused operational tools. Control-plane services are separate versioned services; shell integrations connect the WebUI to those services.

## Current Status

This repository is an early shell prototype. It currently includes:

- A Blazor shell with Fluent UI styling and Aspire-like density.
- Extension registration through the .NET service container.
- A Resource Manager surface with resource groups, nested resources, endpoints, state, and details.
- Programmatic Control Plane resource declarations through checked-in `Resources` code.
- Resource-bound actions for standard lifecycle commands and provider-specific commands.
- Resource group templates for provider-owned import/export of grouped resources.
- Configuration service resources for sharing settings and secrets between dependent resources.
- A Logs section where providers and extensions can expose resource or artifact logs.
- Resource type registration, where extensions provide the UI used to add resources.
- EF Core persistence for explicitly registered root resources and resource groups.
- Configurable ASP.NET Core Identity, dashboard-secret, OIDC, or external-scheme authentication.
- Role, permission, resource-group, and resource-scoped authorization.
- Host UI localization with a persisted language picker.
- SQLite or SQL Server persistence selected through configuration.
- A Docker reference extension that registers a local Docker Engine resource and shows containers as sub-resources.
- An executable application extension for local dev services, with configurable process lifetime and environment variables.

## Concepts

### Resources

A resource is the core domain object in CloudShell. It represents something the platform can manage or inspect, such as a Docker Engine, container, service, database, queue, or internal tool.

Resources can have:

- A stable ID.
- A type ID.
- Lifecycle state.
- Resource actions, including standard lifecycle commands such as Run, Stop, Pause, and Restart.
- Endpoints.
- Dependencies.
- A detail route owned by an extension.
- A parent resource, which makes it a sub-resource.

Resources can also be associated with logs. Logs are separate services, not fields on the resource itself, so multiple providers or extensions can expose one or more logs for the same resource or for a non-resource artifact. Resource Manager shows a shortcut when a resource has registered logs, and the Logs section can open a resource-scoped view.

### Resource Types

Resource types are the user-facing extensibility point for adding resources.

An extension registers a resource type and provides a Blazor registration component for it. The CloudShell Add Resource page shows all registered resource types in a dropdown and renders the selected type's registration UI.

For example, the Docker extension registers the `docker.engine` resource type. Its registration UI discovers the local Docker socket and lets the user add the Docker Engine as a CloudShell resource.

The executable application extension registers the `application.executable` resource type. Its registration UI lets the user add a local command, pass environment variables, choose a working directory and endpoint, and configure whether the process is detached from or scoped to the CloudShell control plane.

Control Plane hosts can also declare selected baseline resources in code with
`Resources`. The sample host declares an `Example Configuration`
service this way, while leaving other resources to be added through the UI.
See [Programmatic resources](docs/programmatic-resources.md).

### Resource Providers

Resource providers are internal implementation services. They are not shown as a product concept in the UI.

Providers map external systems into CloudShell resources. A provider can discover available resources, but root resources only become visible in the shell after the user explicitly adds them. Dynamic children can appear under a registered root resource.

Providers can attach actions directly to each resource. Actions are part of the `CloudResource` API, so the Resource Manager inventory, resource details, and provider-owned overview pages can render the same command set. Standard lifecycle actions use `ResourceActionKind` values for Run, Stop, Pause, and Restart. Providers can also expose custom actions with stable IDs and user-facing labels. `ResourceActionPresentation` controls UI placement, icon, and confirmation prompts separately from provider execution logic.

Providers and extensions can also register log providers. A log provider returns `LogDescriptor` values, reads recent `LogEntry` values for a selected log, and can override `StreamLogAsync` for live tailing. Descriptors can point at a `ResourceId`, an artifact ID, or provider-owned source, and opt in to live streaming through `SupportsStreaming`.

Providers can opt in to resource templates through `IResourceTemplateProvider`.
CloudShell owns the group-level template envelope and import/export orchestration,
while providers own the schema and validation of their `configuration` payload.
Unsupported resources are reported as template diagnostics instead of blocking
the entire group export or import.

The Docker provider follows that pattern:

```text
Local Docker Engine
├── detail route: /resources/docker-engine
└── Docker Container sub-resources
```

Executable application resources are local dev processes. By default, they use a detached lifetime: CloudShell starts the process, persists the last known PID and process start time, and does not stop it when CloudShell exits. When CloudShell restarts, the provider uses that persisted process metadata to rediscover a still-running process without trusting a PID alone. A control-plane-scoped lifetime is also available for temporary helpers that should stop with CloudShell.

### Resource Groups

Resource groups are user-managed project boundaries. They are owned by the CloudShell platform, not by providers.

A root resource can be assigned to a resource group when it is added. Sub-resources inherit the group for filtering and display. Resources without an explicit group are shown in the default group.

Resource dependencies are group-scoped by default. In the application registration and update flows, dependency candidates come from the selected resource group; default-group resources only see other default-group resources.

Resource groups are authorization scopes. Roles and direct claims determine which groups and inherited resources a user can read or manage.

Resource groups can be exported and imported as templates from `/resources/templates`.
Template import creates a new group and delegates each resource entry to the
owning provider. See [Resource templates](docs/resource-templates.md).

## Projects

- `CloudShell.Host`: Blazor shell, layout, built-in Resource Manager, Extensions, and Observability views.
- `CloudShell.ControlPlane`: control-plane services, authorization adapters, resource/log stores, and the versioned OpenAPI endpoint module.
- `CloudShell.Abstractions`: extension SDK, shell contributions, and resource contracts.
- `CloudShell.Configuration`: Microsoft `IConfiguration` provider for CloudShell configuration services.
- `CloudShell.Persistence`: EF Core SQLite or SQL Server persistence for resources and local Identity.
- `CloudShell.Providers.Applications`: extension for executable application resources on a local development machine.
- `CloudShell.Providers.Configuration`: extension for local configuration service resources.
- `CloudShell.Providers.Docker`: reference extension for local Docker Engine and containers.
- `CloudShell.Abstractions.Tests`: extension registration and validation tests.

## Prerequisites

- .NET 11 preview SDK.
- Docker Desktop or a local Docker daemon if you want to use the Docker sample.

The repository includes `global.json` to pin the preview SDK expected by the project.

## Run

From the repository root:

```bash
dotnet restore
dotnet run --project CloudShell.Host --urls http://localhost:5088
```

Then open:

```text
http://localhost:5088
```

Useful routes:

- `/resources`: resource inventory and resource groups.
- `/resources/add`: add a resource by choosing a registered resource type.
- `/resources/templates`: export and import resource group templates.
- `/resources/docker-engine`: Docker Engine detail view.
- `/extensions`: installed extensions and contributed resource types.
- `/api/control-plane/v1`: versioned Control Plane API.
- `/api/configuration/entries?resourceId=...`: token-authenticated configuration service API.
- `/openapi/control-plane-v1.json`: OpenAPI document for generated clients.

## Test

```bash
dotnet build
dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore
```

## Persistence

By default, CloudShell stores platform registrations in SQLite:

```text
CloudShell.Host/Data/cloudshell.db
```

The database is created automatically at startup. The `Data` directory is ignored by git because it is local runtime state. SQL Server can be selected in `appsettings.json`; see [docs/persistence.md](docs/persistence.md).

Persisted data currently includes:

- Explicitly registered root resources.
- Resource group definitions.
- Resource-to-group assignments.

Provider discovery data, such as Docker containers under a registered Docker Engine, is not persisted as platform registration data. Those resources are re-discovered and shown dynamically.

Executable application configuration and runtime state are provider-owned local files under `CloudShell.Host/Data` by default. Configuration is stored separately from runtime state. Runtime state includes the last known process ID, process start time, last observation time, last exit code, and log path.

Configuration services are also provider-owned local files under `CloudShell.Host/Data`.
Each configuration service is its own resource and can be assigned to a resource
group. See [docs/configuration-services.md](docs/configuration-services.md).

Resource templates do not change that ownership model. CloudShell exports a
provider-owned JSON payload for each supported resource, and import delegates
that payload back to the provider instead of storing configuration in the core
resource registration table.

## Executable Application Sample

The executable application extension is intended for local dev services: APIs, frontend dev servers, emulators, workers, and similar commands that should appear in Resource Manager without requiring Docker.

You can add the sample web API through `/resources/add` as an executable
application. Configure it to run:

```bash
dotnet run --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --no-launch-profile
```

The sample runs on `http://localhost:5127` through `ASPNETCORE_URLS`, and that endpoint is rendered as a real link in the resource details blade.

The sample can depend on the programmatically declared `Example Configuration`
service.
When it is started from CloudShell, the reusable `CloudShell.Configuration`
provider loads settings from the injected configuration endpoint and token. The
`/configuration` endpoint reports provider status and loaded keys.

Executable applications default to detached lifetime, so a service can continue running if CloudShell is restarted. Choose control-plane-scoped lifetime when the process should be stopped with CloudShell. Detached application stdout and stderr are written to per-resource files under `CloudShell.Host/Data/application-logs` so the Logs view can read them after restart.

## Docker Sample

The Docker extension looks for a local Docker endpoint in this order:

1. An endpoint configured through `AddDockerProvider`.
2. The `DOCKER_HOST` environment variable.
3. Docker Desktop's user socket.
4. A rootless Docker runtime socket.
5. `/var/run/docker.sock`.

After the Docker Engine resource is added through `/resources/add`, the Resource Manager shows the engine as a root resource and containers as sub-resources.

Docker container sub-resources expose lifecycle actions based on current container state. Running containers can Stop, Pause, or Restart. Stopped containers can Run. Paused containers can Resume, Stop, or Restart.

Docker also contributes logs. The Docker Engine resource exposes provider diagnostics, and each container sub-resource exposes a container log source. Resource log shortcuts open `/logs` with the selected resource and log preselected when there is only one matching log.

## Extension Model

Extensions are trusted, in-process .NET extensions registered through DI.

An extension can contribute:

- Blazor views and navigation items.
- Resource types with extension-owned registration UI.
- Internal resource providers.
- Log providers for resources, providers, or extension-owned artifacts.
- Services with singleton, scoped, or transient lifetimes.
- Capability metadata used for startup validation.

CloudShell validates extension registrations at startup:

- Extension IDs must be unique.
- View routes must be unique.
- Consumed capabilities must be provided by an installed extension.
- Resource type IDs must be unique.

See [docs/extensions.md](docs/extensions.md) for the extension-authoring model.

Deployment configuration:

- [Control plane API and generated clients](docs/control-plane-api.md)
- [Authentication and authorization](docs/authentication-and-authorization.md)
- [Hosting model](docs/hosting-model.md)
- [Localization](docs/localization.md)
- [Persistence](docs/persistence.md)
- [Programmatic resources](docs/programmatic-resources.md)
- [Resource templates](docs/resource-templates.md)
- [Configuration services](docs/configuration-services.md)
- [Executable applications](docs/executable-applications.md)

## Trust Model

The current shell extension model loads code in-process. Extensions can register services and execute arbitrary .NET code, so only trusted extensions should be installed.

Independently deployed control-plane integrations should use versioned out-of-process protocols, starting with the HTTP OpenAPI contract exposed by the CloudShell Control Plane API. Shell integrations should consume generated clients for those contracts rather than taking direct dependencies on the service implementation.
