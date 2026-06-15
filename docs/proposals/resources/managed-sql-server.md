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
* Preserve the current local-development path while documenting that
  container-backed projection is transitional.
* Hide or replace generic container-app deployment controls for SQL Server
  when the managed resource model is implemented.
* Surface SQL Server-oriented configuration: edition/image where appropriate,
  TDS endpoint, administrator/bootstrap password, storage, identity access,
  databases, users/grants, backup/restore, and diagnostics.
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

## Database Resources

The SQL Server instance should be able to project child database resources
after provider inspection exists. A future resource type could be named
`sqlserver.database` or use another database resource naming convention once
the domain model has a database resource class.

Database resources should support database-oriented management such as:

* list/create/drop databases
* size/state/compatibility-level inspection
* backup and restore
* user and role grants
* connection metadata
* identity-backed access grants

## Identity Direction

The future provider should support CloudShell resource identities for SQL
Server access, similar in spirit to Azure managed identity. A dependent
application identity should be grantable to the SQL Server instance or a
database child resource. The provider then materializes the required SQL
logins, database users, and grants.

Password authentication remains a bootstrap and local-development path until
identity-backed database access is implemented and validated.

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
* Hide or replace generic container-app controls for SQL Server when the
  managed resource UI is introduced.
* Define a database resource class or convention for SQL Server databases.
* Add provider inspection for databases, version, edition, storage, and health.
* Add identity-backed SQL login/database user provisioning.
* Define provider-owned backup/restore and maintenance operations.
* Split runtime container diagnostics from the SQL Server managed resource
  surface.
