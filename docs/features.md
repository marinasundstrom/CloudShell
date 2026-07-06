# Feature and Specification Index

This index points to the canonical documentation for current CloudShell
behavior. Feature and specification docs are the home for implemented behavior,
current contracts, and how the system works. Proposals remain working documents
for active increments, open decisions, migration tasks, and deferred work.

Use this split when updating docs:

- Feature/specification docs answer: what exists, how it works, which APIs or
  contracts own it, and what providers/samples currently support.
- Proposals answer: what remains to build, why it matters, what is uncertain,
  and which implementation slice should be taken next.
- Future direction docs answer: what may fit later but does not have a
  near-term implementation path.

## Spec Requirements For Extensibility

CloudShell is extensible by resource providers, Control Plane integrations,
Resource Manager UI extensions, shell extensions, launchers, and language SDKs.
Feature/specification docs must therefore capture enough detail for new
integrations to stay on par with existing ones.

When documenting implemented behavior, include the relevant details from this
checklist:

| Detail | Why it matters |
| --- | --- |
| Owning contracts | Names the domain/API/provider/UI contracts an integration must implement or call. |
| Resource model shape | Captures `ResourceDefinition` type IDs, attributes, capabilities, operations, references, and provider-owned payloads. |
| Authoring surfaces | Shows how the feature is authored through YAML/JSON templates, `ResourceGraphBuilder`, launcher packages, CLI, UI, or SDKs. |
| Runtime boundaries | Separates accepted resource state from provider runtime state, orchestration records, handles, caches, and observed status. |
| Diagrams | Carries over valid Mermaid diagrams or replaces stale proposal diagrams with current feature/spec diagrams. |
| Projection requirements | Explains how the feature appears through Resource Manager, Control Plane API/client DTOs, generated UI tabs, and extension points. |
| Provider parity expectations | Lists the minimum behavior a new provider should support to be considered equivalent, and which advanced behavior is optional. |
| Launcher/language parity expectations | Identifies which builder helpers, template output, CLI calls, environment variables, and SDK clients need matching support across C#, TypeScript/JavaScript, Java, and future languages. |
| Language sample parity | Identifies the required proof sample for each supported language, including launcher shape, container projection, Configuration Store, Secrets Vault, resource identity grants, runtime SDK usage, and documented runtime/toolchain versions. |
| Security and secret handling | States which fields must never expose secrets and which permission checks apply. |
| Diagnostics and readiness | Documents action-unavailable reasons, validation diagnostics, preflight checks, health/liveness, monitoring, logs, traces, and activity behavior. |
| Persistence and lifecycle | Records what is persisted, what is in-memory, what is host-scoped, and how cleanup/recovery behaves. |
| Known gaps | Keeps temporary non-parity visible without treating it as the intended contract. |

Do not rely on proposal text to preserve these details after implementation.
If a proposal contains a useful implementation inventory, port the durable
parts into the feature/spec docs and leave the proposal with links plus
remaining work.

## Status Terms

Use these terms when summarizing feature and proposal state:

| Status | Meaning |
| --- | --- |
| Current | Implemented behavior or current contract. The linked feature/spec docs should be the canonical source. |
| In progress | Implemented slices exist, but material behavior or documentation is still being completed. |
| Migration in progress | Current behavior exists while an older path is being replaced. Specs should describe the supported path and proposals should track the remaining migration. |
| Proposed | A near-term proposal exists, but the behavior is not yet implemented as a supported feature. |
| Future direction | Strategically plausible, but not active roadmap work. |

## Core Product And Architecture

| Area | Canonical docs | Current scope |
| --- | --- | --- |
| Product goal and positioning | [Goal](goal.md), [Why CloudShell](why-cloudshell.md), [Roadmap](roadmap.md) | Product intent, MVP focus, and current execution priorities. |
| Architecture and domain model | [Architecture](architecture.md), [Domain model](domain-model.md), [System design guidelines](system-design-guidelines.md), [Terminology](terminology.md) | Ownership boundaries, Control Plane/WebUI split, domain terms, and design rules. |
| Hosting | [Hosting model](hosting-model.md), [Launchers](launchers-and-app-hosts.md), [Integration story](integration-story.md) | Combined hosts, split hosting, local development hosts, launchers, host-profile responsibilities, and cross-language integration expectations. |
| Control Plane API | [Control Plane API](control-plane-api.md), [SDK clients](sdk-clients.md), [CloudShell CLI](cli.md), [Integration story](integration-story.md) | HTTP API shape, remote client behavior, generated/handwritten clients, runtime service clients, and CLI apply/host workflows. |

## Resource Model

| Area | Canonical docs | Current scope |
| --- | --- | --- |
| Resource model concepts | [Resource model](resource-model.md), [ResourceDefinition structure](resource-definition-structure.md), [Resource model providers](resource-model-providers.md), [Built-in resource types](resources/resource-types.md), [Capabilities](capabilities.md), [Provider-created and runtime-managed resources](runtime-managed-resources.md) | Resource classes, types, definitions, attributes, capabilities, operations, provider contracts, graph resolution, runtime-managed projections, visibility, ownership, and diagnostics. |
| Resource templates and apply | [Resource templates](resource-templates.md), [Programmatic resources](programmatic-resources.md), [Integration story](integration-story.md) | YAML/JSON templates, apply modes, `ResourceGraphBuilder`, `DefineResources(...)`, `DefineInitialTemplate(...)`, launcher apply, builder-owned authoring, and language-builder parity. |
| Resource identity and permissions | [Resource identity and permissions](resource-identity-and-permissions.md), [Authentication and authorization](authentication-and-authorization.md) | Built-in and external identity, principals, grants, auth boundaries, and resource access behavior. |
| Persistence | [Persistence](persistence.md) | EF Core persistence, durable stores, accepted state, and operational data boundaries. |

## Resource Types

| Area | Canonical docs | Current scope |
| --- | --- | --- |
| Application resources | [Built-in resource types](resources/resource-types.md), [Application resources](resources/application-resources.md), [Executable applications](resources/executable-applications.md), [ASP.NET Core applications](resources/aspnet-core-applications.md), [JavaScript applications](resources/javascript-applications.md), [Java applications](resources/java-applications.md), [Go applications](resources/go-applications.md), [Python applications](resources/python-applications.md) | Stable application resource concepts, launch/runtime behavior, environment, service discovery, logs, monitoring, and language-specific app providers. |
| Container applications | [Container Apps](resources/container-apps.md) | Container app Resource Manager behavior, images, replicas, ingress, load-balancer/name-mapping relationships, runtime-managed replicas, readiness, diagnostics, and monitoring. |
| Container hosts | [Container Hosts](resources/container-hosts.md) | Generic container-host resource shape, host descriptors, resolver order, capabilities, diagnostics, runtime boundaries, and provider/launcher parity. |
| Storage and volumes | [Storage and Volumes](resources/storage-and-volumes.md) | Storage/volume resource types, volume-consumer payloads, mount materialization observations, Resource Manager storage views, permissions, diagnostics, and provider/launcher parity. |
| SQL Server | [SQL Server resources](resources/sql-server.md) | Local-development SQL Server bridge, database child resources, volume support, access grants, and reconcile behavior. |
| RabbitMQ | [RabbitMQ resources](resources/rabbitmq.md) | Local-development RabbitMQ broker bridge, AMQP and management endpoints, optional volume support, generated Resource Manager details, read-only queues/exchanges/bindings topology, CloudShell access grants mapped to RabbitMQ broker permissions, and deferred specialized broker management. |
| Event Broker | [Event Broker](resources/event-broker.md) | Provider-neutral event-transport resource shape, protocol endpoint projection, and the boundary between event distribution, device management, operation queues, and observability. |
| Load balancers | [Load balancers](resources/load-balancers.md), [Networking](networking.md) | Load-balancer resources, routes/backends, Traefik/local file-provider support, DNS/name mapping relationships, and exposure behavior. |
| Configuration and secrets | [Configuration services](configuration-services.md), [Resource identity and permissions](resource-identity-and-permissions.md) | Configuration Store, Secrets Vault, reference flow, identity-backed access, and safe value handling. |
| Device identity and enrollment | [Device Registry](resources/device-registry.md), [Resource identity and permissions](resource-identity-and-permissions.md), [Event Broker](resources/event-broker.md) | Device Registry service resources, trust anchors, enrollment policy, device identity category, built-in identity-backed MVP provisioning, and the event-transport boundary for future device telemetry and check-ins. |

## Operations And Observability

| Area | Canonical docs | Current scope |
| --- | --- | --- |
| Monitoring and usage | [Resource Monitoring and Usage](monitoring-and-usage.md), [Control Plane API](control-plane-api.md) | Provider-observed monitoring snapshots, usage records, capability boundaries, API/client routes, and Resource Manager integration. |
| Orchestration and deployments | [Orchestration and Deployments](orchestration-and-deployments.md), [Resource templates](resource-templates.md) | Internal deployment records, environment revisions, replica groups, deployment coordination, and the boundary between user-authored resource state and runtime materialization. |
| Observability | [Observability](observability.md), [Control Plane API](control-plane-api.md), [ResourceDefinition structure](resource-definition-structure.md), [Resource model](resource-model.md) | Logs, resource events, traces, telemetry metrics, provider monitoring, usage, health/liveness, API routes, permissions, and provider boundaries. |
| Service discovery and networking | [Service discovery](service-discovery.md), [Networking](networking.md) | Endpoint references, environment projection, host/virtual networks, endpoint mappings, DNS zones, and local names. |

## UI And Extensions

| Area | Canonical docs | Current scope |
| --- | --- | --- |
| Shell and UI structure | [UI structure](ui-structure.md), [Shell customization](shell-customization.md), [UI composition](ui-composition.md) | Current shell layout, settings/navigation patterns, reusable composition primitives, and product UI boundaries. |
| Extensions | [Extensions](extensions.md), [Control Plane resource providers](extensions/control-plane-resource-providers.md), [Resource Manager UI extensions](extensions/resource-manager-ui.md), [UI extensions](extensions/ui.md) | Extension contracts, provider boundaries, Resource Manager UI contributions, and shell extension guidance. |
| Localization | [Localization](localization.md) | User-facing text and localization guidance. |

## Planning And Work Tracking

| Area | Canonical docs | Current scope |
| --- | --- | --- |
| Active proposals | [Proposals](proposals/README.md) | Actionable proposal index, fit, next action, current proposal order, and feature-doc migration queue. |
| Future directions | [Future directions](future/) | Deferred strategic ideas that are not active roadmap work. |
| Refactoring | [Refactoring tracker](refactoring.md) | Active cross-cutting cleanup and boundary work that is not itself a feature proposal. |
| Decisions and history | [ADR](../ADR.md), [Changelog](../CHANGELOG.md) | Durable decisions and landed change history. |
