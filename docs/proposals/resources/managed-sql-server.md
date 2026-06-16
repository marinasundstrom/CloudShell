# Managed SQL Server Resource Proposal

## Status

Proposed.

The current `application.sql-server` resource is a transitional local
development implementation. It is authored as a first-class SQL Server
resource, but the application provider materializes it with the SQL Server
Linux container image and reuses several container-backed application
mechanics.

## Problem

SQL Server is a database service, not a generic container application. The
current implementation is useful for the MVP because it proves stateful
application dependencies, local container hosting, TDS endpoints, storage
mounts, connection-string display, and sample topology. However, if the
Resource Manager exposes generic container-app controls such as image
deployment, replicas, revisions, app ingress, or app-owned deployment flows on
SQL Server, users may confuse implementation details with the managed database
resource model.

CloudShell needs a future SQL Server resource shape that can still use
containers, Docker hosts, Kubernetes, systemd, or another provider as an
implementation detail, while presenting database-oriented operations and
configuration to the user.

## Goals

* Keep `application.sql-server` or its eventual successor as a managed
  SQL Server instance resource, not as a container app.
* Make the canonical programmatic API declare a SQL Server resource directly,
  even when the provider uses a container as its local implementation.
* Preserve the current local-development path while documenting that
  container-backed projection is transitional.
* Hide or replace generic container-app deployment controls for SQL Server
  when the managed resource model is implemented.
* Surface SQL Server-oriented configuration: version, edition, TDS endpoint,
  administrator/bootstrap password, storage, identity access, databases,
  users/grants, backup/restore, and diagnostics.
* Allow providers to materialize SQL Server through containers or other
  runtimes without exposing those runtime artifacts as the main management
  surface.
* Support future child database resources so database-level operations do not
  overload the SQL Server instance resource.

## Non-Goals

* Do not implement a full managed SQL Server provider in the MVP.
* Do not remove the current container-backed local development SQL Server
  resource before samples have a replacement.
* Do not standardize SQL Server high availability, backups, identity-backed
  login provisioning, or database resources in this proposal's first slice.
* Do not make SQL Server a special case of the generic container app
  deployment UI.
* Do not expose arbitrary image override APIs, such as `WithImage(...)`, on the
  future SQL Server service builder. A user should not be able to turn a SQL
  Server resource into an unknown container image while keeping the SQL Server
  type.

## Current Implementation

Implemented today:

* Resource type `application.sql-server`.
* Resource Manager create flow with image, SA password, container host,
  TDS endpoint, resource group, and optional data volume.
* Container-backed runtime using `mcr.microsoft.com/mssql/server:2022-latest`.
* TDS endpoint projection and convenience connection string display.
* Password reveal/copy for the raw `MSSQL_SA_PASSWORD` value.
* SQL Server-specific storage recommendation for `/var/opt/mssql`.
* Volume selector and Storage tab inherited from the application resource
  provider.
* ApplicationTopology and Container Host samples that use SQL Server as a
  stateful dependency.

Implementation caveat: because the provider currently stores SQL Server as an
application resource and uses the same runtime path as container-backed
applications, SQL Server can inherit container-oriented implementation details.
That is acceptable for the current local-development flow, but not the desired
future managed SQL Server experience.

Sample caveat: the current sample-local `AddSqlServer(...)` helpers are
implemented by composing `AddContainer(...)` and returning
`IContainerResourceBuilder`. Those helpers are useful for ApplicationTopology
and ContainerHost samples, but they are not the intended canonical SQL Server
declaration API. A provider-owned SQL Server builder should return a
SQL-oriented builder or the common resource builder, project the SQL Server
type, and expose SQL Server version/edition settings instead of arbitrary
container image settings.

## Resource Type and Kind

SQL Server should be modeled as its own resource type. The current
`application.sql-server` type exists because the built-in application provider
owns the local-development bridge. The long-term type name remains open:

* Keep `application.sql-server` if SQL Server stays in the application provider
  package as a built-in managed backing service.
* Move to a provider package type such as `sqlserver.server` if SQL Server gets
  its own capability package and provider.

Either way, the type is the stable user-facing concept. The future SQL Server
builder should not expose `WithImage(...)`. If the provider uses a container
internally, it should choose from validated SQL Server versions, editions, or
provider policy, then map those choices to a known SQL Server runtime image
behind the provider boundary. A future SQL Server provider can still project
attributes such as `sqlserver.version`, `sqlserver.edition`, or an
implementation image digest for inspection, but those attributes do not make
the resource a generic container app and should not let users select an
arbitrary non-SQL image.

The built-in `application.sql-server` type should project as
`ResourceClass.Service` until CloudShell has a dedicated database class. It
should not project as `ResourceClass.Container` unless the user actually
declares a generic container app. A provider can still run SQL Server in a
container internally, but that runtime container is provider-owned
materialization or a diagnostic child, not the SQL Server resource kind.

The desired model still needs a durable database-service class decision before
database children become a broader product concept. Options are:

* add a database-oriented class, such as `ResourceClass.Database`, for SQL
  Server instances and database children;
* use `ResourceClass.Service` for managed database servers and introduce a
  database resource type convention for children; or
* keep `ResourceClass.Service` for database servers and use a child-resource
  convention until there is enough pressure for a dedicated database class.

The proposal should not add a new class until the database child-resource and
provider inspection model are ready to validate it across API/client, Resource
Manager filters, generated details, and samples.

## Desired Resource Surface

The future managed SQL Server page should prioritize:

* Overview: server status, endpoint, connection string, active provider,
  storage persistence, version/edition, and warnings.
* Connectivity: TDS endpoint, internal/external exposure relationships, DNS or
  name mappings where applicable.
* Security: bootstrap administrator credentials, CloudShell identity access,
  database users, grants, and permission diagnostics.
* Storage: data, log, backup, and optional temp storage attachment points with
  provider compatibility diagnostics.
* Databases: projected database child resources when provider inspection is
  available.
* Operations: start/stop/restart for local/dev resources, backup/restore,
  reconcile/provision identity access, and provider-specific maintenance
  operations when supported.
* Observability: SQL Server logs, structured logs, metrics, traces where
  available, health checks, and activity events.

The page should not expose generic container app image deployment, revision,
replica, app ingress, or container-app-specific deployment controls by default.
If a provider uses a container internally, that runtime artifact may be
available through contextual diagnostics or runtime-managed resource views, not
as the SQL Server resource's primary management model.

The page should include a database-oriented **Databases** tab when provider
inspection can list databases. That tab should show projected database child
resources with name, state, size, compatibility level, owner or identity
metadata when available, connection metadata, and available database actions.
It should be an instance-scoped view of child resources, not a separate global
database inventory by default.

## Database Resources

The SQL Server instance should first be able to project discovered child
database resources after provider inspection exists. A future resource type
could be named `sqlserver.database` or use another database resource naming
convention once the domain model has a database resource class or a durable
database resource convention.

Projected database children should use stable resource IDs derived from the
server and database name, for example:

```text
sqlserver:main/database:orders
```

They should set `ParentResourceId` to the SQL Server instance and inherit the
resource group through the parent unless explicitly modeled otherwise. They
should not appear as top-level inventory resources by default. Resource Manager
should show them from the SQL Server **Databases** tab and in relationship
views when the caller has permission to inspect them.

Database resources should support database-oriented management such as:

* list/create/drop databases
* size/state/compatibility-level inspection
* backup and restore
* user and role grants
* connection metadata
* identity-backed access grants

Declaring SQL database resources in code is a separate authoring decision from
projecting inspected databases. The likely programmatic shape is:

```csharp
var sql = resources
    .AddSqlServer("main")
    .WithVersion("2022")
    .WithEdition("Developer");

var orders = sql.AddDatabase("orders");
```

or, when an existing server should be referenced by ID:

```csharp
resources
    .AddSqlDatabase("orders", server: "sqlserver:main");
```

The first implementation should prefer projected database children for
inspection, then add declarative database resources only when create/drop,
idempotent reconciliation, export/import, and permission semantics are clear.
Declarative databases should be desired state owned by the SQL Server provider;
they should not be implemented as independent platform records that bypass the
server provider's validation.

## Identity Direction

The future provider should support CloudShell resource identities for SQL
Server access, similar in spirit to Azure managed identity. A dependent
application identity should be grantable to the SQL Server instance or a
database child resource. The provider then materializes the required SQL
logins, database users, and grants.

Password authentication remains a bootstrap and local-development path until
identity-backed database access is implemented and validated.

Identity and permissions should map through the existing CloudShell resource
identity and scoped permission grant model instead of introducing
SQL-specific credential projection:

* The application owns a CloudShell resource identity, declared with
  `WithIdentity(...)` or `RequireIdentity(...)`.
* The SQL Server instance or database child resource is the protected target.
* Grants can target the server for instance-level operations or a database
  child for database-scoped access.
* The SQL Server provider materializes the grant into provider-owned SQL
  objects: server logins, contained database users, role membership, or
  provider-specific external identity configuration.
* The provider reports provisioning and drift diagnostics without projecting
  passwords, tokens, connection secrets, or raw credential material through
  `Resource.Attributes`.

Azure should guide the shape without becoming the only implementation. The
CloudShell model should resemble an app with a managed identity granted access
to an Azure SQL server or database, while keeping the provider free to map the
same intent to local SQL Server containers, Microsoft Entra ID, Keycloak-backed
development identities, or on-premise directory integration.

Useful permission targets include:

* server management, such as start/stop/restart, inspect, backup, restore, and
  identity reconciliation;
* database data-plane roles, such as read, write, read/write, schema change,
  and owner;
* database management operations, such as create, drop, backup, restore, and
  grant/revoke.

The provider should translate those CloudShell grants into SQL roles or custom
grant materialization. The stable CloudShell permission names can be refined
when database child resources are introduced, but the grant target should be a
resource ID, not a connection string or secret.

## Programmatic API Direction

The future provider-owned API should make SQL Server the declared concept:

```csharp
var sql = resources
    .AddSqlServer("main")
    .WithEdition("Developer")
    .WithVersion("2022")
    .WithDataVolume(sqlData)
    .WithTdsEndpoint(port: 14333)
    .RequireAdministratorPassword("Application:Sql:AdminPassword");
```

Applications should depend on and reference the SQL Server resource for
endpoint discovery:

```csharp
var api = resources
    .AddAspNetCoreProject("api", "../Api/Api.csproj")
    .WithReference(sql)
    .DependsOn(sql);
```

When identity-backed database access is enabled, the API should make the grant
target explicit:

```csharp
var api = resources
    .AddAspNetCoreProject("api", "../Api/Api.csproj")
    .RequireIdentity(name: "api-service");

var orders = sql.AddDatabase("orders");

orders.Allow(api.Identity, "Database/databases/readWrite/action");
```

The exact builder names are not final. The important shape is that callers
declare SQL Server and optional databases as domain resources. SQL Server
version/edition are domain settings; container image, host placement, and
runtime-specific storage remain provider configuration behind those builders.

## MVP Position

For the MVP, leave the current container-backed SQL Server implementation in
place. It is useful for proving local stateful dependencies, mounted storage,
service discovery, configuration/secrets integration, and observability in
ApplicationTopology.

Do not broaden SQL Server-specific managed database work until the immediate
MVP container app, storage, networking, and identity primitives are stable.

## Remaining Tasks

* Decide whether `application.sql-server` remains the long-term resource type
  or whether a provider-specific type such as `sqlserver.server` replaces it.
* Decide the durable resource class for SQL Server and database children.
* Hide or replace generic container-app controls for SQL Server when the
  managed resource UI is introduced.
* Replace sample-local `AddSqlServer(...)` helpers with a provider-owned
  SQL-oriented builder that projects the SQL Server resource type and exposes
  validated SQL Server version/edition choices instead of arbitrary image
  override.
* Define a database resource type, resource ID convention, parent relationship,
  and Resource Manager Databases tab for SQL Server databases.
* Decide when declarative SQL database resources should be supported, after
  projected database children and provider inspection are in place.
* Add provider inspection for databases, version, edition, storage, and health.
* Add identity-backed SQL login/database user provisioning.
* Define stable database permission names and map them to SQL Server logins,
  database users, roles, and provider-specific identity integration.
* Define provider-owned backup/restore and maintenance operations.
* Split runtime container diagnostics from the SQL Server managed resource
  surface.
