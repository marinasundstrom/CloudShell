# CloudShell

CloudShell is an extensible, self-hosted control-plane UI for local development and on-premise environments. It uses Blazor and Fluent UI, with an operational experience inspired by the .NET Aspire Dashboard.

The host is intentionally small. Extensions can contribute:

- Blazor views and navigation
- Resource types with extension-owned registration UI
- Internal resource providers that map external systems into resources
- Resources, nested sub-resources, relationships, and endpoints
- Extension-owned services registered with singleton, scoped, or transient lifetimes
- Capability metadata used to validate extension dependencies

Resources are explicit platform registrations persisted in SQLite. Providers can discover available systems, but a root resource only appears in the shell after a user adds it. Dynamic children, such as Docker containers under a registered Docker Engine, appear as sub-resources.

## Projects

- `CloudShell.Host`: Blazor shell and built-in platform extensions.
- `CloudShell.Abstractions`: extension SDK and resource contracts.
- `CloudShell.Persistence`: EF Core SQLite persistence for resource groups and registrations.
- `CloudShell.Providers.Docker`: reference extension for local Docker containers.
- `CloudShell.Abstractions.Tests`: SDK registration and validation tests.

## Run

```bash
dotnet run --project CloudShell.Host
```

The current extension model is for trusted, in-process .NET extensions referenced by the host. Dynamic package and directory discovery can be added later without changing the extension contract.

See [docs/extensions.md](docs/extensions.md) for the extension-authoring model.
