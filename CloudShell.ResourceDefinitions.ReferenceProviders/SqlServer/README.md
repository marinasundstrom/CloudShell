# SQL Server Reference Provider

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
  provider-owned lifecycle runtime handler seam. The default implementation is
  no-op until a host or sample plugs in runtime behavior.
- Reconcile-access operation with an injected provider-owned runtime reconciler seam.
- Typed wrapper plus ContainerHost sample-inspired graph coverage.
- ASP.NET Core service-discovery environment projection from explicit SQL Server `project.references`.
- Resource Manager bridge projection, including endpoint contract/network mapping projection, and execution.
- Resource Manager bridge state projection through the lifecycle runtime
  handler when one is registered.
- ApplicationTopology sample-local lifecycle runtime handler that maps graph
  SQL Server start/stop/restart operations to the existing SQL Server runtime
  resource and projects cached lifecycle status while the provider-owned
  runtime remains under design. Docker smoke coverage verifies graph SQL start
  and stop through that adapter.
- ApplicationTopology graph API read/write grant declaration against a graph SQL Server resource.
- ApplicationTopology sample-local graph SQL credential endpoint that validates
  graph resource identity grants and materializes SQL login/user access for
  the graph API `/database` path.
- Manual `ResourceDefinitionGraphBuilder.AddSqlServer(...)` builder for
  code-first SQL Server definition authoring, declared database configuration,
  and volume mount capability setup.

## Remaining

- Real SQL runtime integration behind the lifecycle runtime handler.
- Default/preferred container-host resolution.
- Reusable provider-owned credential/grant reconciliation for graph-backed SQL
  Server resources outside the ApplicationTopology sample-local endpoint.
- Database child projections and UI tabs.
