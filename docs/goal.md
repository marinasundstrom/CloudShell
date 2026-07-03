# CloudShell Goal

CloudShell is an extensible, self-hosted cloud-portal platform for local
development, team-owned platform tooling, and on-premise environments. The
long-term vision is one platform for development and hosting: a developer can
start close to their code, a team can run the same model as a standing
environment, and operators can grow that environment into clustered or
multi-Control Plane deployments without switching products.

The primary goal is to let teams manage resources through the CloudShell UI or
the Control Plane API, while preserving an excellent developer experience
through the Programmatic API. A solution should be able to start as code-first
resource declarations, then grow into a mix of programmatic declarations,
Resource Manager workflows, provider integrations, and API-driven automation.

## Product Themes

- One resource model should be shared by code, UI, and API. Applications,
  databases, containers, networks, storage, identities, permissions, endpoints,
  deployments, logs, traces, and operational actions should feel like parts of
  one environment rather than separate tools.
- Developers should be able to model a distributed application early, before
  they need a full infrastructure platform. Identity, storage, networking,
  DNS, ingress, observability, and deployment controls can be added when the
  environment needs them.
- Resource Manager should be useful for real management, not only inspection.
  Users should be able to register, group, configure, inspect, start, stop,
  expose, connect, and diagnose resources through the UI when permissions and
  provider capabilities allow it.
- The Control Plane API should expose the same domain-shaped resource model
  for automation, remote clients, integrations, and split-hosting scenarios.
- CloudShell should be self-hosted and extensible. Capability packages can add
  providers, resource types, Resource Manager UI, shell views, services,
  diagnostics, and client helpers without owning the whole product.
- The CloudShell UI should become an independently useful extensible shell
  platform. Resource Manager is the first major shell extension, but the shell
  should support other installed product areas through common menu, page,
  settings, notification, and content-area contribution models.
- CoreShell should be useful as a general extensible shell, not only as the
  Resource Manager container. Product teams should be able to assemble
  workspaces, operational views, settings, dashboards, and custom tools on top
  of the shell contracts for their own needs.
- The WebUI and Control Plane remain separate application surfaces even when
  hosted together for local development.
- CloudShell should be ecosystem-neutral for users, applications, providers,
  and host bootstrapping even though the core implementation is C#/.NET.
  JavaScript, TypeScript, Java, C#, and other languages should be able to
  define resource graphs, configure startup, launch the host, and drive the
  Control Plane through supported SDKs, launchers, templates, and API clients.
- CloudShell should be designed for multiple Control Plane topologies. A
  single UI should be able to target remote Control Planes, and future
  environments should be able to federate, cluster, or partition Control Plane
  responsibility while presenting a coherent shell and resource model.
- CloudShell should use established packages, protocols, and container images
  for common infrastructure concerns when they fit the product boundary. The
  platform should not leak provider-native implementation details as stable
  CloudShell concepts.
- CloudShell should expose infrastructure through familiar, standardized
  resource, capability, endpoint, exposure, identity, storage, and
  observability concepts that transfer across local development, on-premise,
  provider-backed use cases, and the systems those environments integrate with.
  It should avoid hiding provider behavior or copying one provider's accidental
  model. Provider-specific details should still be inspectable when they are
  useful for diagnostics or operations.
- CloudShell should organize infrastructure by current cloud-native domain
  concepts and user intent, not by a provider's historical product taxonomy.
  Users should be able to understand applications, endpoints, endpoint
  mappings, networks, DNS/name mappings, storage, identity, deployments,
  health, and telemetry without first learning the vocabulary of Docker,
  Kubernetes, Azure, AWS, or any other backing implementation. Provider-native
  details remain available when inspecting or diagnosing a specific provider
  resource, but they are not the primary CloudShell model.

## Platform Direction

CloudShell should provide cloud-like management primitives without requiring a
public cloud account. The platform should support local development first, then
scale toward team-owned, on-premise, clustered, and multi-Control Plane
environments with the same model. CloudShell is a hosting platform that also
works as a development tool: the same Control Plane, Resource Manager,
CoreShell, resource model, and provider extension patterns can run in a
developer's local combined host, in a standalone on-premise CloudShell
environment, or through split/federated hosts.

The important platform path is:

1. Model applications and backing services.
2. Add configuration, secrets, identity, storage, and observability.
3. Add container applications, virtual networks, endpoint exposure, ingress,
   DNS/name mapping, and service discovery.
4. Operate the environment through Resource Manager, CoreShell extensions, the
   Control Plane API, and provider or extension integration points.
5. Expand from one local Control Plane to remote, clustered, or federated
   Control Plane topologies as the environment becomes shared infrastructure.

CloudShell is not trying to replace public cloud platforms. It should make
cloud-inspired architecture understandable, manageable, and testable in local
or self-hosted environments, while keeping a path to provider-backed
implementations. It should also avoid giving the impression that CloudShell is
only for .NET teams or .NET workloads. The CloudShell core can remain
C#-based while other languages provide launcher entry points that define the
graph, configure host startup, start or target the host, and operate the same
Control Plane through consistent cross-platform tooling.

The platform can learn from existing cloud portals without inheriting their
legacy boundaries. Familiar placement and affordances are useful, but
CloudShell's default model should stay resource-centered and portable:
providers and orchestrators materialize resources, while users operate the
resource graph through CloudShell concepts.

## Local Development MVP Goal

The current MVP target is the local-development flow. A developer should be
able to start with programmatic resource declarations, run a realistic
distributed application locally, use Resource Manager to understand and operate
the graph, and then explicitly persist the resource graph when the environment
is ready to become Control Plane-owned state.

That handoff is not deployment. `Persist()` records resources and
provider-owned configuration so the flow can move from code-first declarations
to durable environment state. Deploying the graph to a target such as an
on-premise CloudShell environment, Azure, or AWS remains a separate
orchestrator concern and should wait for the deployment API.

For this MVP, Resource Manager should feel like a solid developer cockpit for
the application environment, but not a fully finished portal. The application
resource page should connect the things that matter to running and
understanding the app: endpoints, service discovery, exposure, storage,
identity-backed configuration and secrets where they affect runtime behavior,
logs, traces, monitoring, activity, and inbound names. Secondary editing tabs
should be improved only when they block that primary experience.

The current MVP stabilization priority is the UI experience and the
abstractions that support it at every layer. That includes shell composition
where it already backs navigation or settings, Resource Manager page and tab
contracts, provider-owned UI contributions, generated detail rendering,
shared selector and status components, route/link resolution, labels,
empty/error states, and the Control Plane API shapes the UI depends on. New
platform capabilities should wait when the existing UI path is inconsistent,
hard to reason about, or difficult to maintain.

## MVP Proof

The MVP should prove that a common-hosted CloudShell environment can manage a
realistic multi-resource application through both programmatic declarations and
Resource Manager. The strongest proof is an application topology with container
apps or project-backed services, configuration and secrets, identity-backed
access where needed, SQL Server with mounted storage, logs, traces, networking,
endpoint exposure, and DNS/name mappings.

The MVP should also prove the failure path for that topology. When an operation
cannot complete because a container host, dependency, provider runtime, or
local prerequisite is unavailable, Resource Manager should identify the
affected resource, explain the dependency or provider failure, and leave users
with a clear next diagnostic step instead of surfacing only a generic internal
error or a stuck lifecycle transition.

The local-development proof is ready only when supported samples build and
smoke-test, Resource Manager explains the application graph without leaking
secret values, action capability reasons explain failures before dispatch,
transient code-first declarations remain visibly distinct from persisted
Control Plane state, and the main experience feels coherent without being
overbuilt.

[Roadmap](roadmap.md) owns milestone scope and ordering.
[ADR](../ADR.md) owns durable product and architecture decisions.
[Changelog](../CHANGELOG.md) owns landed implementation changes. This document
owns the durable product goal.
