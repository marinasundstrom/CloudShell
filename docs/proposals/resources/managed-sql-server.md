# Managed SQL Server Resource Proposal

## Status

Partially implemented.

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
* Provider-owned programmatic `AddSqlServer(...)` builder that projects the
  SQL Server resource type and returns a SQL-oriented builder instead of a
  container-app builder.
* Resource Manager create flow with image, SA password, container host,
  TDS endpoint, resource group, and optional data volume.
* Container-backed runtime using `mcr.microsoft.com/mssql/server:2022-latest`.
* TDS endpoint projection and convenience connection string display.
* Password reveal/copy for the raw `MSSQL_SA_PASSWORD` value.
* SQL Server-specific storage recommendation for `/var/opt/mssql`.
* Volume selector and Storage tab inherited from the application resource
  provider.
* ApplicationTopology and Container Host samples that use SQL Server as a
  stateful dependency through the provider-owned builder.
* Resource Manager SQL Server pages omit generic container-app Deployment and
  Scale and replicas tabs by default.
* Database read/write grant intent can be assigned to SQL Server resources
  through the existing Access control model.
* Resource Manager can show requested-versus-effective grant status from the
  SQL Server provider. The local provider can reconcile read/write grants for
  resource-identity principals by creating provider-owned contained database
  users and `db_datareader`/`db_datawriter` role memberships for declared
  databases, then inspect SQL-side state and report applied, pending, failed,
  or drifted status.
* SQL Server resources expose a **Reconcile database access** action that
  reapplies CloudShell database grants to declared SQL databases while the
  instance is running.
* Programmatic declarations can add expected databases with
  `WithDatabase(...)`; those project as provider-managed
  `application.sql-database` child resources and display in a SQL Server
  **Databases** tab.

Implementation caveat: because the provider currently stores SQL Server as an
application resource and uses the same runtime path as container-backed
applications, SQL Server can inherit container-oriented implementation details.
That is acceptable for the current local-development flow, but not the desired
future managed SQL Server experience.

Implementation caveat: the first provider-owned `AddSqlServer(...)` builder
still maps to the shared local application runtime and fixed SQL Server Linux
container image. It intentionally does not expose arbitrary image or replica
configuration through the SQL-facing builder. Future slices should replace the
remaining local-development image-oriented setup fields with validated SQL
Server version/edition choices or provider policy.

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

The page includes a first database-oriented **Databases** tab for declared
database children. The current local provider performs a read-only live query
against `sys.databases` when the SQL Server instance is running, then merges
declared databases and live databases into one list so users can see which
declarations really exist. Creating the database is currently an application or
migration responsibility. Future provider inspection can enrich those rows with
size, compatibility level, owner or identity metadata when available,
connection metadata, and available explicit database actions. It should remain
an instance-scoped
view of child resources, not a separate global database inventory by default.

## Database Resources

The current provider projects declared database children as
`application.sql-database` resources under the SQL Server instance. A future
resource type could be named `sqlserver.database` or use another database
resource naming convention once the domain model has a database resource class
or a durable database resource convention.

Projected database children use stable resource IDs derived from the server and
database name. The current application-provider form is:

```text
application:main/database:orders
```

A future dedicated SQL Server provider may use:

```text
sqlserver:main/database:orders
```

They should set `ParentResourceId` to the SQL Server instance and inherit the
resource group through the parent unless explicitly modeled otherwise. They
should not appear as top-level inventory resources by default. The current
projection uses diagnostic visibility so Resource Manager can show them from
the SQL Server **Databases** tab without treating them as ordinary top-level
inventory resources.

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
* The provider reports requested-versus-effective access status and drift
  diagnostics without projecting passwords, tokens, connection secrets, or raw
  credential material through `Resource.Attributes`.

Azure should guide the shape without becoming the only implementation. The
CloudShell model should resemble an app with a managed identity granted access
to an Azure SQL server or database, while keeping the provider free to map the
same intent to local SQL Server containers, Microsoft Entra ID, Keycloak-backed
development identities, or on-premise directory integration.

In the Azure SQL managed-identity flow, the database connection still targets
the SQL Server endpoint. The connection string or client configuration selects
an identity-aware authentication mode, the SQL client obtains an access token
from the configured authority, and SQL Server validates that token because the
server is configured to trust the authority. SQL Server still needs local
authorization metadata such as external users, contained users, role
membership, or equivalent provider-native mappings. Azure RBAC-style intent
does not by itself create database permissions.

CloudShell should mirror that boundary. The identity provider or broker is the
credential authority, not the protected database resource. The SQL Server
provider remains responsible for making SQL Server understand the principal by
materializing users, roles, or external-identity mappings. For local
development, where the stock SQL Server container cannot validate CloudShell
tokens directly, the built-in broker may translate a CloudShell principal plus
an effective database grant into a provider-owned SQL authentication path. That
translation must stay behind provider contracts and must not expose the SQL
administrator credential to workloads.

This identity-backed path is primarily a service-to-database path inside the
CloudShell-managed environment. In cloud deployments, managed identity is most
valuable when application services connect to private database endpoints
without carrying long-lived SQL credentials. The database should not have to
be public for the workload to use identity-backed access; networking,
endpoint exposure, and database authorization remain separate concerns. This
also makes the audit story clearer: CloudShell can record which workload
principal requested access, which grant allowed it, and which provider bridge
materialized the database credential.

Credential resolution should produce resource activity. When a workload
requests database access, CloudShell should be able to record the calling
principal, the SQL Server resource, the database name, the requested
permission, whether the request was allowed or denied, and the provider that
issued or refused the credential. The activity must not include the generated
SQL password, bearer token, connection string, or other credential material.
The first implementation can log provider diagnostics, but the durable model
should surface these as resource-scoped activity events so local development
and hosted environments can answer who or what requested database access and
which grant authorized it.

The CloudShell implementation should be staged around that boundary:

1. Resource Manager records requested grants as it does today.
2. The SQL Server provider reports effective access observations for those
   grants: pending, applied, failed, or drifted.
3. The SQL Server provider reconciles SQL-side authorization metadata using
   provider-owned administrator access.
4. Workloads opt into identity-based database authentication through an
   explicit connection option or helper, analogous to Azure SQL connection
   strings that select managed-identity authentication.
5. The workload credential path asks the configured CloudShell identity broker
   for a database authentication artifact. The broker must validate the
   workload principal and the effective grant before returning anything usable
   by the SQL client.

The provider may implement the artifact differently per environment. A local
development provider might issue a generated contained-user credential or
another short-lived provider-owned SQL credential. A provider integrated with
Microsoft Entra ID, Active Directory, or another authority might instead
configure SQL Server to validate external tokens or map external identities
and groups. In every case, the workload-facing contract is identity-based
access, while the provider-specific credential and SQL administration details
remain internal.

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

The next implementation step should not start by generating workload passwords
or exposing connection-string credentials. Resource Manager now has an
effective-access observation from the SQL Server provider, so the UI can show a
requested grant, whether it has been applied, and any provider diagnostic
explaining why it is still pending or failed. The next SQL-specific access
slice after the local SQL-side reconciliation work is workload credential
delivery: a local broker or provider-owned credential path that lets a
resource identity use the reconciled SQL-side authorization without exposing
administrator credentials or long-lived generated SQL passwords in resource
metadata.

### Experimental Workload Connection Path

The first workload-facing experiment is about bridging CloudShell resource
identity into the SQL Server authentication world for a managed SQL Server
resource. Containers, Docker, or other runtimes are only provider-owned
materialization details; the workload contract should feel like connecting to
a managed database service with identity-backed access.

The experiment is scoped to service workloads running within the local or
hosted CloudShell environment. It is not a requirement that the SQL Server
endpoint be publicly exposed. A frontend, API, worker, or other managed
resource should be able to reach a private SQL endpoint through the
environment's networking model and authenticate through its CloudShell
principal.

The first client shape is a connection factory over a provider-owned
credential resolver:

```csharp
builder.Services.AddCloudShellSqlServerClient(options =>
{
    options.SqlServerResourceName = "application-topology-sql-server";
});

await using var connection =
    await cloudShellSql.OpenConnectionAsync(
        "application-topology-sql-server",
        "application_topology");
```

The factory does not replace ordinary SQL Server connection strings. It is an
opt-in path for workloads that have a CloudShell resource identity and want
database access resolved through CloudShell grants. A resolver authenticates
the current workload to CloudShell, asks the SQL Server provider for a
short-lived SQL-native credential, and returns a connection string that
`Microsoft.Data.SqlClient` can use normally.

The DI registration is intentionally light. It reads the broker endpoint from
the CloudShell SQL endpoint environment variables by default and uses the
default CloudShell resource credential chain unless the workload passes an
explicit endpoint or credential. Application code should usually inject
`CloudShellSqlConnectionFactory` instead of constructing the resolver at the
call site.

The experimental client resolves the credential broker endpoint from either a
generic environment variable:

```text
CLOUDSHELL_SQL_CREDENTIAL_ENDPOINT
```

or a resource-specific variable:

```text
CLOUDSHELL_SQL_{RESOURCE_NAME}_CREDENTIAL_ENDPOINT
```

where `{RESOURCE_NAME}` is normalized the same way as other CloudShell client
environment segments. The broker request is intentionally small:

```http
POST /api/sql-server/v1/credentials
Authorization: Bearer {CloudShell resource identity token}
Content-Type: application/json

{
  "sqlServerResourceName": "application-topology-sql-server",
  "databaseName": "application_topology",
  "permission": "CloudShell.Database/databases/readWrite/action"
}
```

and the response is provider-owned credential material:

```json
{
  "connectionString": "Server=...;Database=...;User Id=...;Password=...",
  "expiresOn": "2026-06-21T12:00:00Z"
}
```

The endpoint must authenticate the bearer token, identify the calling
CloudShell principal, evaluate the effective grant against the managed SQL
Server or database resource, reconcile SQL-side authorization if needed, and
only then return SQL-native credential material.

Every broker request should also be auditable as a resource activity. A
successful request means the workload attempted to establish SQL access and
CloudShell issued a provider-owned credential. A denied request means a
principal attempted access without a matching effective grant or without a
valid workload identity. Both outcomes are useful for local troubleshooting
and for future hosted audit trails.

The target experience should be at least as familiar and safe as Azure SQL
with Azure managed identity:

* application code opts into identity-backed SQL access explicitly;
* the app does not carry an administrator password or long-lived SQL password;
* CloudShell grants determine whether the current workload principal may
  connect to the managed SQL Server resource or database;
* the SQL Server provider performs the necessary bridge into SQL-native
  users, logins, roles, external identities, or brokered tokens;
* ordinary SqlClient usage remains possible once the provider has resolved
  credential material.

This deliberately keeps the provider boundary intact:

* SQL Server administrator credentials remain provider-owned.
* Short-lived generated credentials are not stored in resource metadata.
* The local Docker SQL Server path can still use ordinary SQL authentication.
* Applications can keep direct connection strings for bootstrap, migration, or
  debugging scenarios where identity-backed access is not desired.

`SqlAuthenticationProvider` remains a spike candidate for making that
experience feel closer to SqlClient's built-in managed identity modes. The
extension point is token-shaped: it can acquire an access token for
SqlClient-supported authentication modes, but it does not by itself make a SQL
Server instance accept a CloudShell token or return a username/password pair.
It may become useful for a future broker, proxy, or token-validating SQL
provider where the database endpoint can validate a CloudShell-compatible
access token. Until then, the portable bridge is a CloudShell-authenticated
credential resolver that produces SQL-native credential material behind a
narrow client API.

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

orders.Allow(api.Principal, "Database/databases/readWrite/action");
```

The exact builder names are not final. The important shape is that callers
declare SQL Server and optional databases as domain resources. SQL Server
version/edition are domain settings; container image, host placement, and
runtime-specific storage remain provider configuration behind those builders.

## MVP Position

For the MVP, leave the current container-backed SQL Server implementation in
place while presenting SQL Server as a service resource. It is useful for
proving local stateful dependencies, mounted storage, service discovery,
configuration/secrets integration, identity/access grant intent, and
observability in ApplicationTopology.

Do not broaden SQL Server-specific managed database work until the immediate
MVP container app, storage, networking, and identity primitives are stable.

## Remaining Tasks

* Decide whether `application.sql-server` remains the long-term resource type
  or whether a provider-specific type such as `sqlserver.server` replaces it.
* Decide the durable resource class for SQL Server and database children.
* Replace the Resource Manager create/update image-oriented setup fields with
  validated SQL Server version/edition choices or provider policy.
* Expand the provider-owned SQL-oriented builder with validated SQL Server
  version/edition choices instead of arbitrary image override.
* Decide whether the current `application.sql-database` child type remains or
  whether a dedicated provider type such as `sqlserver.database` replaces it.
* Decide when declarative SQL database resources should be supported, after
  projected database children and provider inspection are in place.
* Add provider inspection for version, edition, storage, health, and richer
  database metadata.
* Decide the lifecycle policy for declared databases, including explicit create
  and drop behavior, drift handling, and how application-owned migrations are
  represented.
* Expand identity-backed SQL login/database user provisioning beyond the first
  local contained-user and role-membership reconciliation slice.
* Define stable database permission names and map them to SQL Server logins,
  database users, roles, and provider-specific identity integration.
* Harden the experimental workload credential broker path so resource
  identities can use reconciled SQL-side authorization without configured SQL
  credentials.
* Add rotation cleanup, revocation reconciliation, and explicit credential
  lifetime diagnostics to the provider-backed SQL credential resolver flow.
* Replace direct host endpoint mapping with a provider-owned HTTP endpoint
  registration surface for SQL Server credential broker endpoints.
* Spike whether `SqlAuthenticationProvider` can participate in a future
  token-validating SQL broker path without coupling the MVP to SqlClient
  internals.
* Define provider-owned backup/restore and maintenance operations.
* Split runtime container diagnostics from the SQL Server managed resource
  surface.
