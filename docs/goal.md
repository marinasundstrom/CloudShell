# CloudShell Goal

CloudShell is an extensible, self-hosted cloud-portal platform for local
development, team-owned platform tooling, and on-premise environments.

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
- The WebUI and Control Plane remain separate application surfaces even when
  hosted together for local development.
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

## Platform Direction

CloudShell should provide cloud-like management primitives without requiring a
public cloud account. The platform should support local development first, then
scale toward team-owned and on-premise environments with the same model.

The important platform path is:

1. Model applications and backing services.
2. Add configuration, secrets, identity, storage, and observability.
3. Add container applications, virtual networks, endpoint exposure, ingress,
   DNS/name mapping, and service discovery.
4. Operate the environment through Resource Manager, the Control Plane API,
   and provider or extension integration points.

CloudShell is not trying to replace public cloud platforms. It should make
cloud-inspired architecture understandable, manageable, and testable in local
or self-hosted environments, while keeping a path to provider-backed
implementations.

## MVP Proof

The MVP should prove that a common-hosted CloudShell environment can manage a
realistic multi-resource application through both programmatic declarations and
Resource Manager. The strongest proof is an application topology with container
apps or project-backed services, configuration and secrets, identity-backed
access where needed, SQL Server with mounted storage, logs, traces, networking,
endpoint exposure, and DNS/name mappings.

[Roadmap](roadmap.md) owns milestone scope and ordering.
[ADR](../ADR.md) owns durable product and architecture decisions.
[Changelog](../CHANGELOG.md) owns landed implementation changes. This document
owns the durable product goal.
