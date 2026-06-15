# SQL Server Resources

SQL Server resources project as `application.sql-server`. They are authored as
a first-class resource type, but the current provider materializes them as a
container-backed application using the SQL Server Linux container image.

For shared application-provider behavior, see
[Application resources](application-resources.md). For the underlying
container app model, see [Container apps](container-apps.md).

## Registration

The Resource Manager SQL Server create flow asks for:

- resource name and optional resource ID
- container image
- SA password
- container host
- local TDS endpoint
- resource group
- optional data volume

The default image is:

```text
mcr.microsoft.com/mssql/server:2022-latest
```

The resource exposes a `tds` endpoint with container target port `1433`.

## Overview

The SQL Server overview shows the projected TDS endpoint, a convenience
connection string, and the configured SA password. The connection string uses
the current local endpoint and the configured `MSSQL_SA_PASSWORD` value:

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

## Future Database Resources

`application.sql-server` represents the SQL Server instance. A future
SQL Server provider should be able to inspect a running instance and project
individual databases as separate database resources, for example a
`sqlserver.database` resource type with a database-oriented resource class when
that class is added to the domain model.

Those database resources should be children of, or otherwise related to, the
SQL Server instance. They can then support database-specific management
features such as listing databases, inspecting size and state, creating or
dropping databases, backup/restore workflows, user grants, and connection
metadata without overloading the server resource.
