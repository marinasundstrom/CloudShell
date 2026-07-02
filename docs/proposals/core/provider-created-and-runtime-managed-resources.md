# Provider-Created and Runtime-Managed Resources Proposal

## Status

- Status: In progress
- Strategy fit: High; this is the foundation for replica diagnostics,
  provider-owned child resources, ownership traversal, and cleanup semantics.
- Canonical feature docs:
  [Provider-created and runtime-managed resources](../../runtime-managed-resources.md),
  [Resource model](../../resource-model.md),
  [Container Apps](../../resources/container-apps.md), and
  [Orchestration and Deployments](../../orchestration-and-deployments.md)
- Remaining action: continue provider-observed IDs, placement,
  materialization diagnostics, shared ownership, orphan cleanup, and richer
  provider-created resource policies as focused increments.
- Out of scope: broad public runtime-resource authoring, full deployment
  revision productization, and provider-native resource import.

## Current Implementation

Implemented behavior has moved to
[Provider-created and runtime-managed resources](../../runtime-managed-resources.md).
That spec is now the canonical source for:

- `Resource.Source`, `Resource.ManagementMode`, `Resource.Visibility`,
  `OwnerResourceId`, and `CleanupBehavior`
- Control Plane API and remote-client projection of runtime-managed metadata
- Resource Manager hidden/runtime-managed display settings
- `resources.runtime-managed.read` gating
- container app hidden `runtime.container` replica resources
- storage-owned hidden volume ownership behavior
- provider parity and launcher/language parity expectations

Keep new implementation details in the feature/spec docs when they land, then
update this proposal only for remaining work and decisions.

## Problem

CloudShell resources can cause additional resources or runtime artifacts to be
created during execution, deployment, orchestration, scaling, networking,
provisioning, and reconciliation.

Examples include:

- container replicas and backing containers
- generated images
- volume attachments
- endpoint and backend registrations
- gateway routes
- service discovery records
- health probes
- deployment revisions
- runtime certificates

These artifacts need clear ownership, lifecycle, diagnostics, cleanup, and
visibility rules. At the same time, CloudShell should not expose every
provider-owned implementation detail as part of the user-authored application
contract.

## Goals

- Keep user-authored resources, provider-created resources, and
  runtime-managed artifacts in one coherent resource graph when resource
  identity adds operational value.
- Preserve a clean default Resource Manager inventory.
- Allow advanced and owner-scoped views to inspect runtime/provider artifacts.
- Make ownership, source, management responsibility, visibility, and cleanup
  explicit and independent.
- Give providers guidance for when to project a runtime artifact as a resource
  and when to keep it as provider-owned state.
- Support diagnostics, logs, traces, metrics, health, actions, and dependency
  relationships where child resource identity is useful.

## Non-Goals

- Do not require every provider-created or runtime-created object to be a
  resource.
- Do not expose provider-owned secrets or raw implementation state as resource
  attributes.
- Do not make hidden runtime-managed resources part of the normal inventory by
  default.
- Do not require runtime-managed resources to be authorable.
- Do not standardize every possible provider-created resource type in one
  increment.

## Strategy Fit

Fit is high because this proposal supports several MVP and post-MVP needs:

- replicated container app diagnostics
- app-scoped health, logs, traces, and metrics
- storage-owned and provider-owned child-resource workflows
- runtime cleanup and orphan detection
- future Docker host, Kubernetes, and on-premise provider parity
- clearer Resource Manager filtering and inspection behavior

The near-term value is diagnostic clarity and cleanup correctness, not a broad
new authoring surface.

## Remaining Work

1. Define shared ownership semantics for provider-created resources that are
   used by multiple stable resources.
2. Define garbage-collection behavior for orphaned non-authored resources.
3. Decide when shared resources should use one owner plus references versus a
   future shared-ownership model.
4. Enrich container app runtime replicas with provider-observed backing IDs,
   placement, health, and materialization details.
5. Define event and history projection for provider-created and runtime-created
   resources.
6. Add provider guidance for durable provider-created resources such as
   generated images, deployment revisions, gateway routes, and backend
   registrations.
7. Define projected-resource facade references and out-of-context
   materialization/resolution semantics.
8. Decide whether source and management mode need provider-extensible values
   or whether fixed platform enums should remain the contract.

## Open Questions

- Should provider-created resources be queryable through normal resource APIs
  by default, or should some classes require diagnostic APIs?
- How should versioning and auditing work for durable provider-created
  resources?
- Which provider-created artifacts should become first-class feature docs
  versus provider-owned implementation details?
- What standardized diagnostics should apply to all hidden runtime-managed
  resources?
- How should public Resource Manager views show shared provider-created
  resources without implying single-resource ownership?
