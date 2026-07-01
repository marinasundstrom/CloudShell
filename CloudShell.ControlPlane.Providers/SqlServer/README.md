# SQL Server Built-in Provider

## Overview

- Resource type: `application.sql-server`
- Provider id: `applications.sql-server`
- Purpose: declares a SQL Server resource in the Resource Graph while leaving runtime SQL materialization and credential handling outside the graph model.

## Ported

- Service class/type defaults.
- Version, edition, and typed endpoint request attributes.
- Optional typed generic/Docker container-host reference validation and projection.
- Declared database configuration.
- Shared volume-consumer capability over direct and storage-backed volumes.
- Start, stop, and restart operation declarations with an injected
  provider-owned lifecycle runtime handler seam and an opt-in local SQL Server
  Docker runtime adapter for mapped resources.
- Reconcile-access operation with an injected provider-owned runtime reconciler seam.
- Typed wrapper plus ContainerHost sample-inspired graph coverage.
- ASP.NET Core service-discovery environment projection from explicit SQL Server `project.references`.
- Resource Manager bridge projection, including endpoint contract/network mapping projection, and execution.
- Resource Manager bridge state projection through the lifecycle runtime
  handler when one is registered.
- Graph-safe Resource Manager UI metadata registration for graph-only samples,
  including display name, icon, TDS endpoint descriptor, and SQL liveness probe
  defaults without registering old application-provider stores or SQL edit
  pages.
- Provider-owned local SQL Server Docker runtime coverage that maps graph SQL
  Server start/stop/restart operations to Docker containers, resolves
  storage-backed volume declarations into bind mounts, projects lifecycle
  status, and optionally waits for SQL readiness.
- ApplicationTopology graph API read/write grant declaration against a graph SQL Server resource.
- ApplicationTopology sample-local graph SQL credential endpoint that validates
  graph resource identity grants and materializes SQL login/user access for
  the graph API `/database` path.
- Manual `ResourceGraphBuilder.AddSqlServer(...)` builder for
  code-first SQL Server definition authoring, declared database configuration,
  and volume mount capability setup.

## Example ResourceDefinition

This is the interchange shape for a graph-backed SQL Server declaration that
mounts a storage-backed CloudShell volume. Runtime SQL startup, database
creation, and credential reconciliation are delegated to the Control Plane
runtime integration for the provider.

```json
{
  "name": "sql-server",
  "typeId": "application.sql-server",
  "resourceId": "application.sql-server:sql-server",
  "providerId": "applications.sql-server",
  "displayName": "SQL Server",
  "dependsOn": [
    {
      "value": "docker.host:sample",
      "relationship": "dependsOn",
      "addressingMode": "resourceId",
      "typeId": "docker.host",
      "providerId": "docker"
    }
  ],
  "attributes": {
    "sqlserver.version": "2022",
    "sqlserver.edition": "Developer",
    "sqlserver.endpointRequests": [
      {
        "name": "tds",
        "protocol": "tcp",
        "targetPort": 1433,
        "host": "localhost",
        "port": 1433,
        "exposure": "Local"
      }
    ],
    "storage.volumeConsumer": {
      "mounts": [
        {
          "volume": "cloudshell.volume:sql-data",
          "targetPath": "/var/opt/mssql",
          "readOnly": false
        }
      ]
    }
  },
  "configuration": {
    "sqlServer": {
      "databases": [
        {
          "name": "application",
          "displayName": "Application database",
          "ensureCreated": true
        }
      ]
    }
  }
}
```

## Switch-over status

Ready to integrate for graph SQL workloads where the host opts into the local
Docker runtime adapter. ApplicationTopology and ContainerHost prove graph SQL
startup, storage-backed volume materialization, endpoint projection,
service-discovery environment projection, database creation, readiness, and
cleanup without old SQL resource records. A durable non-local SQL runtime and
reusable credential/grant reconciliation remain post-switch work.

## Remaining

- Durable non-local SQL runtime integration behind the lifecycle runtime
  handler.
- Default/preferred container-host resolution.
- Reusable provider-owned credential/grant reconciliation for graph-backed SQL
  Server resources outside the ApplicationTopology sample-local endpoint.
- Database child projections and editable/provider-specific UI tabs.
