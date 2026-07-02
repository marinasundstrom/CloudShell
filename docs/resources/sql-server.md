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
    .DeclareDatabase("appdb", "Application DB")
    .WithIdentity(identityProvider);
```

This declares `application.sql-server` with `ResourceClass.Service`. The local
provider still uses the SQL Server Linux container image to run the service,
but callers do not receive a container-app builder and should not configure
generic image deployment or replicas through the SQL Server API.

## Databases

SQL Server resources can declare database resources through the programmatic
builder:

```csharp
resources
    .AddSqlServer("main")
    .DeclareDatabase("orders", "Orders");
```

The application provider projects each declared database resource as an
`application.sql-database` child resource with `ProviderManaged` management and
diagnostic visibility. The child resource records its parent SQL Server
resource, database name, and projection source.

Declared SQL databases record the resource-model assumption that the database
should exist on the SQL Server. The declaration itself is not an operation and
does not create the database; it gives CloudShell a child resource to show in
Resource Manager, attach grants to, and correlate with provider observations.
That is different from a container app replica, which is a runtime-managed
resource materialized by the container app when scaling is enabled.

Resource Manager displays declared databases in the SQL Server **Databases**
tab even when the SQL Server instance is stopped. When the instance is running,
the provider uses the configured instance password to connect to SQL Server,
inspect `sys.databases`, and report whether each declaration exists on the
server.

The **Databases** tab is read-only. CloudShell does not create missing
declared databases during SQL Server startup by default; the declaration states
the assumption that the database should exist, while creating the database
schema remains an application or migration responsibility. Local development
and test resources can opt in with `DeclareDatabase(...).EnsureCreated()`,
which is a separate provider operation request to create the database if it is
missing before database grants are reconciled. CloudShell does not drop
databases or project database connection strings for workloads.

Database read/write grant intent can be assigned to SQL Server resources
through the normal Access control model. Resource Manager distinguishes
requested CloudShell grants from effective SQL Server access so users can see
whether the provider has applied, failed, or drifted from the requested state.
The local SQL Server provider can reconcile read/write grants for
resource-identity principals by creating provider-owned contained database
users and `db_datareader`/`db_datawriter` role memberships for declared
databases, then inspect SQL-side state and report applied, pending, failed, or
drifted status.

SQL Server resources expose a **Reconcile database access** action that
reapplies CloudShell database grants to declared SQL databases while the
instance is running. The action uses the provider's SQL administrator path;
workloads still receive provider-neutral resource identity credentials rather
than the bootstrap administrator password.

## Overview

The SQL Server overview leads with SQL service details: projected TDS endpoint,
declared database count, administrator name, storage, identity, access grants,
and diagnostics. Provider runtime container details remain behind
configuration and diagnostics surfaces instead of being the primary overview.

For local development, the overview also shows a convenience connection string
and the configured SA password. The connection string uses the current local
endpoint and omits the password so copying it does not expose the configured
`MSSQL_SA_PASSWORD` value:

```text
Server=localhost,<port>;User Id=sa;TrustServerCertificate=True;
```

The password is hidden by default with reveal and copy actions for local
development convenience.

## Storage

For the shared storage and volume model, see
[Storage and Volumes](storage-and-volumes.md).

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

## Identity And Access

The current SQL Server provider uses password-based access because the MVP
implementation is container-backed and optimized for local development. The
`MSSQL_SA_PASSWORD` value remains the bootstrap path for the built-in resource
and for provider-owned reconciliation operations.

CloudShell resource identity is the user-facing access model. An application
resource with an assigned CloudShell identity can receive standard
`CLOUDSHELL_IDENTITY_*` credential environment variables, and the SQL Server
provider can materialize matching database users and role memberships for
declared database grants. This is intentionally similar to Azure managed
identity flows at the product level, while the current local provider still
uses SQL Server-contained users behind the provider boundary.

Identity access should be modeled through resource identity and scoped grants,
not by projecting database credentials as ordinary secrets into dependent
resources. Database-scoped permissions will likely become clearer when SQL
Server can project individual databases as child database resources.

The current builder can record grant intent on the SQL Server resource:

```csharp
sql.Allow(api.Principal, CloudShellPermissions.Database.Actions.ReadWrite);
```

Resource Manager Access control and identity views show the requested grant and
the provider-reported effective status when the provider can inspect the SQL
Server.

## Future Database Resources

`application.sql-server` represents the SQL Server instance. The current
provider can project declared `application.sql-database` child resources and
inspect whether those declared databases exist. A future SQL Server provider
should also be able to inspect a running instance and project discovered
databases, possibly through a dedicated `sqlserver.database` resource type with
a database-oriented resource class when that class is added to the domain
model.

Those database resources should be children of, or otherwise related to, the
SQL Server instance. They can then support database-specific management
features such as listing databases, inspecting size and state, creating or
dropping databases, backup/restore workflows, user grants, and connection
metadata without overloading the server resource.
