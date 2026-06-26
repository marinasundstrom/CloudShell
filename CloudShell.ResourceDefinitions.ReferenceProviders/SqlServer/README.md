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
- Reconcile-access operation with an injected provider-owned runtime reconciler seam.
- Typed wrapper plus ContainerHost sample-inspired graph coverage.
- ASP.NET Core service-discovery environment projection from explicit SQL Server `project.references`.
- Resource Manager bridge projection, including endpoint contract/network mapping projection, and execution.

## Remaining

- Real SQL runtime integration.
- Default/preferred container-host resolution.
- Credential/grant reconciliation, database child projections, and UI tabs.
