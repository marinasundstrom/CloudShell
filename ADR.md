# Architecture Decision Log

This document records durable CloudShell product and architecture decisions.
It is intentionally separate from [Changelog](CHANGELOG.md), which records
landed implementation changes, and [Roadmap](docs/roadmap.md), which owns
milestone scope and task ordering.

Decision IDs are stable enough to reference from changelog entries and related
docs. When an implementation change follows a decision, the changelog should
link to the decision so the dependency is visible.

## 2026-06-15

### ADR-20260615-004: Treat resource IDs as canonical and display names as presentation

Resource IDs are the canonical identity for resources. They are the stable
handles used by dependencies, permissions, resource events, activity logs,
provider state, API calls, and automation. Activity logs should display or
retain the resource ID as the canonical resource address even when display
names are enabled. Resource display names are presentation labels for Resource
Manager and other user-facing surfaces. Display names are useful during local
development when resource IDs are less important to the immediate workflow,
but they must remain optional. Programmatic registration APIs should therefore
take resource IDs and domain-specific parameters; optional labels are applied
with `WithDisplayName(...)`. Resource Manager create flows should ask for
Resource ID first, then an optional display name when display names are
enabled. Display names are enabled by default because they improve
readability, but hosts and users may prefer ID-first workflows. Resource
Manager should therefore make the resource ID explicit in detail and overview
surfaces, provide a display-name preference, and later add display-name
editing without changing the stable resource ID.

CloudShell does not require one global naming scheme, but teams may use
structured resource IDs, configuration keys, and secret names when that helps
map resource hierarchy into JSON configuration, environment variables, or
DNS-safe projections. The optional `--` separator is acceptable guidance for
hierarchy that needs to travel through systems where `:` has configuration
path meaning or is not accepted.

Character and length restrictions are provider-owned rather than global
CloudShell rules. Different backing platforms, such as Azure, AWS, local
files, DNS providers, container hosts, and future deployment providers, impose
different constraints. The built-in Configuration Store should remain broad
and App Configuration-like, while rejecting names that cannot sensibly
round-trip through text configuration. The built-in Secrets Vault should use a
Key Vault-style secret-name shape and rely on `--` for hierarchical .NET
configuration loading.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260615-003: Keep managed SQL Server distinct from container apps

SQL Server is a managed database service resource, not a generic container
application. The current `application.sql-server` implementation may continue
to use the container-backed application runtime as a transitional local
development bridge, but future SQL Server Resource Manager UX should present
database-oriented configuration and operations instead of generic container app
deployment controls such as image rollout, revisions, replicas, or app
ingress. If a provider uses a container internally, that runtime artifact is an
implementation detail or contextual diagnostic child, not the SQL Server
resource's primary management model.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260615-002: Introduce deployment and runtime-owned resource metadata before public rollout features

Container apps need deployment, revision, and runtime-owned resource relationships to become a useful managed-service primitive. Add the shared abstractions and resource metadata first, use them internally for container apps and provider/orchestrator runtime artifacts, and keep them out of the normal public product surface until the model is proven. A container app remains the user-facing resource; orchestrator deployments/revisions and runtime-managed containers, replicas, endpoint registrations, or provider-owned artifacts are lower-level implementation and diagnostic entities that may be hidden from normal Resource Manager lists.

Hidden from global inventory does not necessarily mean internal. A child
resource such as a replica under a container app or a volume under a Storage
resource can be hidden from the top-level inventory by default while still
being part of the visible resource graph when the user has permission.
Resource Manager decides where those resources are presented, such as parent
pages, relationship views, or selectors. Internal managed artifacts are
stricter: they are provider, orchestrator, or runtime implementation details
and should never appear in the default user-facing graph.

Resources can still be handled individually by the orchestrator. When a
resource state or configuration change has runtime workload intent, the
orchestrator may derive a default deployment for that change so CloudShell can
track what was applied without requiring users to explicitly create or manage a
deployment resource.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260615-001: Separate product goal, roadmap, changelog, and ADR responsibilities

Keep the project goal, roadmap, changelog, and architecture decision log as separate documents with different responsibilities. `docs/goal.md` owns the durable product goal, `docs/roadmap.md` owns milestone scope and current task order, `CHANGELOG.md` owns dated landed changes, and `ADR.md` owns durable product and architecture decisions. Link between these documents instead of duplicating the same planning state in each one.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-14

### ADR-20260614-001: Separate host topology from installed environment capabilities

CloudShell distinguishes host topology from installed environment capabilities. A CloudShell host application is the ASP.NET Core app that hosts the CloudShell UI, the Control Plane, or both. A CloudShell environment is the managed local, team-owned, or on-premise cloud-like environment backed by Control Plane resource state, installed capability packages, and one or more UI hosts. Use capability package for NuGet-distributed installable environment capabilities, and reserve workload for runtime application execution concerns.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260614-002: Make container apps the primary MVP application exposure artifact

The MVP application environment path centers on container applications, app-owned exposure and discovery, virtual networks, public endpoints, load-balancer routes, and logical DNS/name mappings. Normal container app exposure should not require a `cloudshell.service` resource. Keep `cloudshell.service` optional for logical facades, imported services, non-application targets, and advanced routing until the deployment/orchestrator model is clearer.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260614-003: Treat storage and identity as MVP differentiators

Storage and identity are MVP differentiators from Aspire-style local orchestration. CloudShell should model volume resources and volume mappings so stateful services can be managed through Resource Manager, and it should validate the identity model against at least one third-party OIDC/OAuth provider in addition to the built-in development provider.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260614-004: Separate workload crash recovery from host restart recovery

Workload crash recovery is distinct from host restart recovery. Providers should project observed state when a workload crashes, while restart/no-restart/backoff policy belongs to an orchestrator layer or explicit future resource policy. Host restart recovery should reconcile resources that are bound to the host lifetime without treating every workload exit as a restart policy event.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260614-005: Make initial on-premise hosting the first post-MVP target

The first post-MVP target is an initial on-premise hosting scenario. It should prove acceptable Resource Manager operations, provider-backed cross-platform networking, virtual networks, ingress/public endpoint mapping, DNS/name mapping, network-level service discovery, event/integration points, and more complex validation samples.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260614-006: Grow host setup into an environment setup experience

Host setup should grow into a broader environment setup experience for platform operators. The setup flow should cover missing OS/runtime prerequisites and environment-level choices such as the default identity provider, default container host, default networking/DNS/service-discovery providers, and related readiness checks. Per-resource prompts remain useful when one resource requires a disabled or unconfigured capability.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-13

### ADR-20260613-001: Treat public exposure and API stability separately

Public exposure and API stability are separate decisions. Public APIs that are not yet stable must be labeled as preview, experimental, or unstable, with clear ownership, expected change surface, and path to stability.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260613-002: Keep logs text-compatible while adding structured metadata

Provider-owned operational logs remain text-compatible with `severity` terminology and optional structured metadata on `LogEntry`: `category`, `eventId`, `traceId`, `spanId`, `exceptionSummary`, and string-only `attributes`. Resource events, audit records, diagnostics, metrics, traces, and future non-text payloads remain separate concerns.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260613-003: Built-in services dogfood public integration surfaces

CloudShell is an open platform. Built-in services and samples should dogfood the same public integration points, identity model, service APIs, lifecycle contracts, diagnostics, and authorization surfaces that extension authors and third-party service authors use unless a documented transitional exception is needed.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260613-004: Keep ASP.NET Core project endpoints explicit

ASP.NET Core project endpoints have an explicit source order: programmatic endpoint declarations win, `launchSettings.json` is used only when `WithLaunchSettingsEndpoints()` is declared, and the provider otherwise assigns a stable local development endpoint. Resource Manager UI create/update flows remain manual and do not read launch settings.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-10

### ADR-20260610-001: Use a host-first descriptor-driven container host abstraction

The container host abstraction is host-first and descriptor-driven. Providers resolve explicit or default container hosts through a shared resolver, keep provider-owned runtime state behind provider contracts, use host-oriented public naming, and report missing host placement through action capability reasons before orchestration dispatch.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-09

### ADR-20260609-001: Model load balancing as a provider-backed resource

Load balancing should be modeled as a resource abstraction over providers. Traefik is the proposed first provider target, with routes mapped to stable resource endpoints and raw ports treated as authoring convenience.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260609-002: Provider-owned runtime infrastructure selects a host resource

Provider-owned runtime infrastructure should select a host resource, where host means an instance of a runtime or control boundary CloudShell can target. Docker, Podman, containerd, schedulers, process managers, and appliance APIs are host runtime capabilities or provider-owned facts, not separate placement primitives.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-08

### ADR-20260608-001: Consumers use domain managers instead of generated HTTP clients

Consumers should use domain managers, not generated HTTP clients directly.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260608-002: Keep Control Plane stores and providers internal

Internal Control Plane stores and providers remain implementation contracts for the service process, not public client integration contracts.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260608-003: Build servers deploy container apps by immutable image tag

Build-server container app deployments should push an immutable image tag to a registry, then call the authenticated Container Apps revision API with that tag. The Control Plane authorizes the caller, updates the image, creates the revision, and records resource events for traceability.

Related changes: [Changelog](CHANGELOG.md).
