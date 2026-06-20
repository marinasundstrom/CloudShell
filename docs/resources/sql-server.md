# SQL Server Resources

SQL Server resources project as `application.sql-server`. They are authored as
a first-class resource type, but the current provider materializes them as a
container-backed application using the SQL Server Linux container image. The
projected resource is a managed service, not a container app; the container
image is implementation configuration for the local provider.

For shared application-provider behavior, see
[Application resources](application-resources.md). For the underlying
container app model, see [Container apps](container-apps.md). For the desired
future managed database shape, see the
[Managed SQL Server proposal](../proposals/resources/managed-sql-server.md).

## Modeling Boundary

The current SQL Server resource reuses the container-backed application runtime
because it is an MVP-local development implementation. That is a transitional
provider detail, not the desired long-term Resource Manager experience.
SQL Server has its own application resource provider boundary today so
templates, registrations, and future model work can evolve independently from
generic container apps even while the current implementation delegates to the
container-backed application infrastructure.

A future managed SQL Server resource should not expose generic container app
controls such as image deployment, revisions, replicas, or app ingress by
default. It should present SQL Server-oriented configuration and operations:
version, edition, connectivity, storage, administrator credentials,
identity-backed access, database children, backup/restore, diagnostics, and
provider-specific maintenance. It should not let users provide an arbitrary
container image while keeping the SQL Server resource type. If a provider uses
a container internally, that container should remain a runtime detail or
contextual diagnostic artifact rather than the main management surface.

## Registration

The Resource Manager SQL Server create flow asks for:

- resource name and optional resource ID
- container image
- SA password
- container host
- local TDS endpoint
- resource group
- optional data volume

The image field is part of the current local-development bridge. The future
managed SQL Server surface should replace arbitrary image entry with validated
SQL Server version/edition choices or provider policy.

The default image is:

```text
mcr.microsoft.com/mssql/server:2022-latest
```

The resource exposes a `tds` endpoint with container target port `1433`.

Programmatic declarations should use the provider-owned SQL Server builder:

```csharp
var sqlData = resources
    .AddVolume("sql-data")
    .WithDisplayName("SQL Data");

var sql = resources
    .AddSqlServer(
        "sql-server",
        administratorPassword: "Your-strong-dev-password!",
        dataVolume: sqlData,
        port: 14334)
    .WithDatabase("appdb", "Application DB")
    .WithIdentity(identityProvider);
```

This declares `application.sql-server` with `ResourceClass.Service`. The local
provider still uses the SQL Server Linux container image to run the service,
but callers do not receive a container-app builder and should not configure
generic image deployment or replicas through the SQL Server API.

## Databases

SQL Server resources can declare databases through the programmatic builder:

```csharp
resources
    .AddSqlServer("main")
    .WithDatabase("orders", "Orders");
```

The application provider projects each declared database as an
`application.sql-database` child resource with `ProviderManaged` management and
diagnostic visibility. The child resource records its parent SQL Server
resource, database name, and projection source.

Resource Manager displays declared databases in the SQL Server **Databases**
tab even when the SQL Server instance is stopped. When the instance is running,
the provider uses the configured instance password to connect to SQL Server,
create any missing declared databases, and list `sys.databases`; the tab
merges those live rows with the declared databases and indicates whether each
declaration exists on the server.

The **Databases** tab is read-only. CloudShell creates missing declared
databases during local SQL Server startup, but does not yet drop databases,
materialize SQL users and roles from access grants, or project database
connection strings for workloads.

Access grants on SQL Server resources are modeled in CloudShell today so the
Resource Manager can show intended access. They are not yet enforced inside
SQL Server. The next access-control slice should translate grants into
provider-owned SQL logins, database users, roles, or another provider-specific
credential model without exposing bootstrap administrator credentials to
workloads. Before that provider materialization lands, Resource Manager should
distinguish requested CloudShell grants from effective SQL Server access so
the UI can show whether the provider has actually applied a grant.

## Overview

The SQL Server overview leads with SQL service details: projected TDS endpoint,
declared database count, administrator name, storage, identity, access grants,
and diagnostics. Provider runtime container details remain behind
configuration and diagnostics surfaces instead of being the primary overview.

For local development, the overview also shows a convenience connection string
and the configured SA password. The connection string uses the current local
endpoint and the configured `MSSQL_SA_PASSWORD` value:

```text
Server=localhost,<port>;User Id=sa;Password=<password>;TrustServerCertificate=True;
```

The password is hidden by default with reveal and copy actions for local
development convenience.

## Storage

SQL Server has a resource-specific data mount point:

```text
/var/opt/mssql
```

When a data volume is selected during registration, CloudShell creates a
`ResourceVolumeMount` named `data` at that path and records the volume resource
as a dependency. That dependency makes the normal lifecycle flow start the
volume dependency before SQL Server and prevents deleting the volume while SQL
Server still references it.

If no data volume is selected, Resource Manager warns:

```text
Data will not be persisted unless a volume is mounted at /var/opt/mssql.
```

This warning is intentionally resource-specific. Authored resources should be
able to describe their meaningful storage attachment points instead of forcing
users to know container paths or provider-native mount syntax.

Storage mappings cannot be changed while SQL Server is running. Stop the
resource before adding, removing, or changing mounted storage.

## Lifecycle

UI-created SQL Server resources use `ControlPlaneScoped` lifetime today because
the built-in resource is primarily a local development service. When created
with **Start after create**, Resource Manager starts the resource through the
standard Start action with dependency startup enabled.

Detached/persistent production-like SQL Server management should eventually be
modeled through the same top-level resource shape, but with provider-backed
storage, clearer recovery behavior, and explicit persistence policy.

## Identity Direction

The current SQL Server provider uses password-based access because the MVP
implementation is container-backed and optimized for local development. The
`MSSQL_SA_PASSWORD` value remains the bootstrap path for the built-in resource
until identity-backed database access is implemented and proven.

The direction is to support CloudShell resource identity for SQL Server access
in addition to password authentication. An application resource with an assigned
CloudShell identity should be able to connect to SQL Server through that
identity, similar to Azure managed identity flows, once the provider can
materialize the required server logins, database users, and grants.

Identity access should be modeled through resource identity and scoped grants,
not by projecting database credentials as ordinary secrets into dependent
resources. Database-scoped permissions will likely become clearer when SQL
Server can project individual databases as child database resources.

The current builder can record grant intent on the SQL Server resource:

```csharp
sql.Allow(api.Principal, CloudShellPermissions.Database.Actions.ReadWrite);
```

For now, that grant is visible in Resource Manager Access control and identity
views. Materializing SQL logins, database users, and roles from the grant is a
future provider capability.

## Future Database Resources

`application.sql-server` represents the SQL Server instance. The current
provider can project declared `application.sql-database` child resources. A
future SQL Server provider should also be able to inspect a running instance
and project discovered databases, possibly through a dedicated
`sqlserver.database` resource type with a database-oriented resource class when
that class is added to the domain model.

Those database resources should be children of, or otherwise related to, the
SQL Server instance. They can then support database-specific management
features such as listing databases, inspecting size and state, creating or
dropping databases, backup/restore workflows, user grants, and connection
metadata without overloading the server resource.
