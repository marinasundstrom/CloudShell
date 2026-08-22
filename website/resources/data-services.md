---
title: Data and service resources
description: Add databases, messaging, configuration, secrets, events, and device enrollment services to a CloudShell environment.
---

# Data and service resources

These resources add the backing services that distributed applications commonly need. In the CLI preview, built-in Configuration Store, Secrets Vault, and Device Registry instances run through versioned container images.

| Resource | Type ID | Use it for |
| --- | --- | --- |
| SQL Server | `application.sql-server` | A local-development SQL Server instance managed as a container-backed service. |
| SQL database | `application.sql-database` | A database that belongs to a SQL Server resource. |
| RabbitMQ | `application.rabbitmq` | A local RabbitMQ broker with AMQP and management endpoints. |
| Configuration Store | `configuration.store` | Non-secret application settings served from a named configuration resource. |
| Host configuration | `configuration.host` | A host or provider-projected configuration source; normally not the first choice for user-authored settings. |
| Secrets Vault | `secrets.vault` | Secret and certificate material exposed through a service boundary without placing values in the resource graph. |
| Event Broker | `event.broker` | A provider-neutral event transport endpoint and lifecycle boundary. |
| Device Registry | `iot.device-registry` | Device enrollment, device identity, and provisioning for local or edge-oriented scenarios. |

## Keep values behind service boundaries

Resource definitions may describe that a setting, secret, certificate, database, or broker exists. Secret values must not appear in resources, logs, diagnostics, or generated site content. Applications reference the owning service resource and obtain values through the appropriate runtime client and permissions.

## Local preview scope

SQL Server and RabbitMQ are currently local container-backed services. Configuration Store, Secrets Vault, and Device Registry are built-in CloudShell services whose runtime is an implementation detail of the resource. External managed-service providers and richer management surfaces can be added later without changing the core idea: applications depend on a stable resource, not on its local process or container identity.

## Featured resource guide

- [RabbitMQ](rabbitmq.md) — broker lifecycle, AMQP and management endpoints, persistent storage, identity-based access, and cross-service messaging traces.
