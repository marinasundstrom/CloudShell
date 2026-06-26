# SQL Database Reference Provider

## Overview

- Resource type: `application.sql-database`
- Provider id: `applications.sql-database`
- Purpose: declares a SQL database child resource in the Resource Graph.

## Ported

- Database name, source, and ensure-created attributes.
- Provider-managed read-only `database.server` `ResourceReference` attribute declaration for the owning SQL Server.
- Temporary server `ResourceReference` validation through current `DependsOn` inputs.
- Typed wrapper projection of the owning server as a `belongsTo` reference.
- Ensure-created operation with an injected provider-owned runtime creation
  handler seam that receives both the database resource and its resolved owning
  SQL Server resource.
- Resource Manager bridge projection and execution.

## Remaining

- Provider projection of `database.server` when SQL database children are materialized by the SQL provider.
- Real SQL database materialization through the provider-owned creation
  handler.
- Credential/grant reconciliation, provider-managed child ownership metadata, and UI tabs.
