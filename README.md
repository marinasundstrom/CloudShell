# CloudShell

CloudShell is an extensible, self-hosted control-plane UI for local development and on-premise environments. It uses Blazor, Fluent UI, and .NET 11 preview, with an operational experience inspired by the .NET Aspire Dashboard.

The goal is to make it possible to build your own cloud-platform shell: a place where teams can register resources, group them by project, inspect endpoints and state, and let extensions add focused operational tools.

## Current Status

This repository is an early shell prototype. It currently includes:

- A Blazor shell with Fluent UI styling and Aspire-like density.
- Extension registration through the .NET service container.
- A Resource Manager surface with resource groups, nested resources, endpoints, state, and details.
- Resource-bound actions for standard lifecycle commands and provider-specific commands.
- A Logs section where providers and extensions can expose resource or artifact logs.
- Resource type registration, where extensions provide the UI used to add resources.
- EF Core persistence for explicitly registered root resources and resource groups.
- Configurable ASP.NET Core Identity, dashboard-secret, OIDC, or external-scheme authentication.
- Role, permission, resource-group, and resource-scoped authorization.
- SQLite or SQL Server persistence selected through configuration.
- A Docker reference extension that registers a local Docker Engine resource and shows containers as sub-resources.

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

### Resource Providers

Resource providers are internal implementation services. They are not shown as a product concept in the UI.

Providers map external systems into CloudShell resources. A provider can discover available resources, but root resources only become visible in the shell after the user explicitly adds them. Dynamic children can appear under a registered root resource.

Providers can attach actions directly to each resource. Actions are part of the `CloudResource` API, so the Resource Manager inventory, resource details, and provider-owned overview pages can render the same command set. Standard lifecycle actions use `ResourceActionKind` values for Run, Stop, Pause, and Restart. Providers can also expose custom actions with stable IDs and user-facing labels. `ResourceActionPresentation` controls UI placement, icon, and confirmation prompts separately from provider execution logic.

Providers and extensions can also register log providers. A log provider returns `LogDescriptor` values and reads recent `LogEntry` values for a selected log. Descriptors can point at a `ResourceId`, an artifact ID, or provider-owned source, and can opt in to streaming support through `SupportsStreaming`.

The Docker provider follows that pattern:

```text
Local Docker Engine
├── detail route: /resources/docker-engine
└── Docker Container sub-resources
```

### Resource Groups

Resource groups are user-managed project boundaries. They are owned by the CloudShell platform, not by providers.

A root resource can be assigned to a resource group when it is added. Sub-resources inherit the group for filtering and display. Resources can also stay ungrouped.

Resource groups are authorization scopes. Roles and direct claims determine which groups and inherited resources a user can read or manage.

## Projects

- `CloudShell.Host`: Blazor shell, layout, built-in Resource Manager, Extensions, and Observability views.
- `CloudShell.Abstractions`: extension SDK, shell contributions, and resource contracts.
- `CloudShell.Persistence`: EF Core SQLite or SQL Server persistence for resources and local Identity.
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
- `/resources/docker-engine`: Docker Engine detail view.
- `/extensions`: installed extensions and contributed resource types.

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

- [Authentication and authorization](docs/authentication-and-authorization.md)
- [Persistence](docs/persistence.md)

## Trust Model

The current extension model loads code in-process. Extensions can register services and execute arbitrary .NET code, so only trusted extensions should be installed.

Longer term, untrusted or independently deployed integrations should use an out-of-process protocol, such as HTTP or gRPC, while keeping the same resource contracts at the host boundary.
