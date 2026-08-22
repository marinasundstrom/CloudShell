---
title: Resource catalog
description: Browse the application, data, service, and platform resources available in the CloudShell preview.
---

# Resource catalog

CloudShell models workloads and infrastructure through a common resource graph. The resources below are available in the current codebase, but CloudShell is still in **preview**: authoring shapes, provider behavior, and management experiences may change.

## Choose a category

| Category | Includes | Guide |
| --- | --- | --- |
| Applications | .NET, JavaScript, Java, Go, Python, executables, and container apps | [Application resources](applications.md) |
| Data and services | SQL Server, RabbitMQ, configuration, secrets, events, and device registry | [Data and service resources](data-services.md) |
| Platform | Containers, storage, networking, DNS, load balancing, identities, and logical services | [Platform resources](platform.md) |

## Featured resource types

Start with the resources that best show how CloudShell connects declared intent to live operations:

- [.NET apps](dotnet-apps.md) for fast project-based development with dependencies and request tracing.
- [Container apps](container-apps.md) for packaged workloads, replica-aware runtime state, routing, and stronger deployment boundaries.
- [RabbitMQ](rabbitmq.md) for application messaging, identity-based broker access, persistent storage, and trace propagation through publish and consume operations.

## User-authored and managed resources

Most application and service resources are authored directly in YAML, templates, or launchers. Some lower-level resources are normally created by a host or provider to explain runtime state. For example, prefer a **Container app** for a managed workload; inspect its provider-created container resources only when you need lower-level diagnosis.

The catalog calls out that distinction so a resource being visible does not imply it should be the first thing you declare.
