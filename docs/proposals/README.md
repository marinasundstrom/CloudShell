# Proposals

Proposals are working documents for actionable product increments. They should
track the work that can plausibly be implemented in the current MVP, the next
post-MVP milestone, or a clearly scoped stabilization slice. Far-future ideas
belong in [Future directions](../future/) until they have a near-term
implementation path. Current behavior belongs in the
[Feature and specification index](../features.md) and the linked feature docs.

When a proposal changes direction or an implementation slice completes, update
the relevant proposal together with [Roadmap](../roadmap.md),
[ADR](../../ADR.md), and [Changelog](../../CHANGELOG.md). Durable decisions
belong in ADR; landed implementation changes belong in the changelog. The
repo-local Codex skills also require this synchronization for feature and
stabilization work.

Use the [Refactoring tracker](../refactoring.md) for active cross-cutting
cleanup task lists. Proposals should retain feature shape, value, open
questions, and remaining work; the tracker should carry current refactoring
slices, completed cleanup, and near-term boundary work. Landed behavior should
move into the relevant specification docs, not remain as the main content of a
proposal. When a proposal contains current implementation details, verify the
details against code before moving them. If feature/specification docs were
written from the start, link to those docs from the proposal and keep only the
active proposal work here. Review proposal Mermaid diagrams during the same
pass: move valid current diagrams to feature/spec docs, update stale diagrams
before moving them, and leave proposal diagrams only when they explain active
design work or deferred options.

## Proposal Status

This table is the authoritative active proposal index. Keep it current whenever
proposal status, MVP relevance, implementation order, fit, or remaining work
changes. Milestone scope remains authoritative in [Roadmap](../roadmap.md).

Active proposals should start with a short metadata block before deeper design
detail:

```markdown
## Status

- Status: In progress | Proposed | Migration in progress | Partially implemented
- Strategy fit: High | Medium | Low, with one short reason
- Canonical feature docs: links to implemented/current behavior
- Remaining action: the next concrete work or decision
- Out of scope: deferred or future-direction items
```

Avoid using `Status` to restate the whole implementation history. If the
proposal needs that context, link to the feature/spec docs or changelog and
keep only the delta that affects remaining work.

| Order | Proposal | Status | Fit likelihood | Action |
| --- | --- | --- | --- | --- |
| 1 | [Platform foundations](core/platform-foundations.md) | In progress | High; this is the MVP convergence lens. | Continue action for UI consistency, sample confidence, diagnostics, persisted-state handoff, and release hardening. Move landed foundations into specs as they settle. |
| 2 | [Secrets management](services/secrets-management.md) | In progress | High; required for app settings and secrets integrations. | Continue focused Resource Manager assignment polish, safe reference display, diagnostics, and identity-backed access. |
| 3 | [Identity and access](core/identity-and-access.md) | Current implementation working document | High; required for built-in and external identity validation. | Keep only remaining identity/access work here. Move stable auth, permissions, grants, and sample behavior into specification docs. |
| 4 | [Lifecycle orchestration](core/lifecycle-orchestration.md) | Proposed | High; directly improves Resource Manager operations and traceability. | Take after current action-capability and diagnostics stabilization, starting with common procedure/event semantics. |
| 5 | [Logging infrastructure](core/logging-infrastructure.md) | In progress | High; supports MVP traceability and diagnostics. | Continue audit schema, retention, metrics/traces alignment, and diagnostic display work; keep provider log metadata already landed in specs. |
| 6 | [Resource monitoring](core/resource-monitoring.md) | In progress | High; supports provider diagnostics and app-centric Resource Manager pages. | Use [Resource Monitoring and Usage](../monitoring-and-usage.md) as the landed spec. Continue provider coverage, live updates, and history decisions here. |
| 7 | [Resource recovery](core/resource-recovery.md) | Proposed | Medium-high; valuable after lifecycle and liveness foundations are stable. | Defer implementation until lifecycle procedure, health/liveness, and provider restart semantics are clearer. |
| 8 | [Service telemetry and degradation](core/service-observability-and-degradation.md) | Proposed | Medium-high; valuable for local degradation analysis but easy to overbuild. | Take only slices that improve app-centric diagnosis with existing logs, traces, metrics, health, and monitoring. |
| 9 | [Container applications](containers/container-applications.md) | In progress | High; core MVP proof. | Continue orchestration convergence, deployment/revision clarity, route rebinding, storage/identity/networking integration, and user-facing diagnostics. |
| 10 | [Container host abstraction](containers/container-host-abstraction.md) | In progress | High; required for default/local and future host targets. | Continue credential readiness diagnostics and provider-owned runtime infrastructure without blocking the local/default host path. |
| 11 | [Virtual network resource](networking/virtual-network-resource.md) | In progress | High; required network primitive. | Continue UI-managed exposure, provider diagnostics, and service/name-mapping integration. |
| 12 | [Load balancer resource](networking/load-balancer-resource.md) | In progress | High; required for explicit public routing and replicated app scenarios. | Continue provider selection, validation, host readiness, lifecycle, and public endpoint UX. |
| 13 | [DNS and name mapping](networking/dns-and-name-mapping-resource.md) | In progress | High for MVP naming; provider-backed DNS is later. | Finish name-mapping update/delete UI and local hostname diagnostics; defer provider-backed DNS propagation until network providers are stronger. |
| 14 | [Storage and volume mappings](storage/volume-mappings.md) | In progress | High; required for stateful local apps. | Continue provider-backed materialization, host/storage compatibility diagnostics, relationship visibility, and usage integration. |
| 15 | [Deployment artifacts](core/deployment-artifacts.md) | Proposed | High; required for create/edit UI that works when the Control Plane and workload host are remote. | Define the Control Plane artifact-store contract, upload API, artifact-reference shape, and first Resource Manager deployment artifact section before broad app create/edit UX. |
| 16 | [Remote Docker hosts](containers/remote-docker-hosts.md) | Partially implemented | Medium; useful but should not block local/default host MVP. | Keep behind shared host abstraction. Take concrete slices only when they validate the host boundary or unblock samples. |
| 17 | [Provider-created and runtime-managed resources](core/provider-created-and-runtime-managed-resources.md) | In progress | High; supports replica/resource diagnostics and cleanup. | Use [Provider-created and runtime-managed resources](../runtime-managed-resources.md) as the landed spec. Continue provider-observed IDs, health, placement, ownership traversal, and materialization diagnostics. |
| 18 | [Orchestrator deployments and environment revisions](deployment/deployments-and-revisions.md) | In progress | High for container app internals; later for public rollout history. | Finish controller/reconciliation boundary for first start, routing rebinding, service tear-down, and cleanup. Keep rich rollout history deferred. |
| 19 | [UI composition library](core/ui-composition.md) | Current implementation working document | Medium-high; active only where it stabilizes current UI surfaces. | Avoid broadening for MVP. Use it to reduce current shell, Settings, and Resource Manager drift; keep generic library behavior in [UI composition](../ui-composition.md). |
| 20 | [Resource graph and runtime separation](core/resource-graph-and-runtime-separation.md) | Migration in progress; active migration anchor | High; foundational to templates, graph apply, and orchestration. | Continue ResourceDefinition-based templates, graph-backed provider migration, and retirement of obsolete provider-template paths. |
| 21 | [Cross-language local development](core/cross-language-local-development.md) | In progress | High; required to keep CloudShell ecosystem-neutral. | Prioritize the installed CLI plus default local-development host daemon path, then keep launcher/profile, TypeScript/JavaScript, Java, and SDK hardening aligned with that boundary. |
| 22 | [Managed SQL Server resource](resources/managed-sql-server.md) | Partially implemented | Medium-high; valuable after MVP storage, identity, and database access stabilize. | Keep current SQL Server local-development bridge stable. Defer full managed database surface until provider-backed grants, storage, and backup/restore value are clear. |
| 23 | [Intent-first resource authoring](core/intent-first-resource-authoring.md) | Proposed | Medium-high; broadens authoring without making CloudShell code-centric. | Defer until ResourceDefinition apply, provider diagnostics, and Resource Manager review/apply surfaces are stable; then start with draft-template review rather than autonomous apply. |

## Deferred Strategy Notes

These items fit CloudShell's long-term direction, but they are not active
proposals. Pull one back into `docs/proposals/` only when there is a clear
incremental implementation slice with near-term value.

| Direction | Fit likelihood | Next action |
| --- | --- | --- |
| [Deployment projection](../future/deployment-projection.md) | High long-term fit for portability and target-specific deployment output. | Defer until ResourceDefinition apply, container app orchestration, networking, storage, identity, and on-premise target boundaries are stable. |
| [Resource graph import and code generation](../future/resource-graph-import-and-code-generation.md) | High adoption fit for Docker Compose and existing local app topologies. | Defer implementation; revisit after container apps, volumes, networking, and import/read-only UX are stable. |
| [Shell composition](../future/shell-composition.md) | High strategic fit for the post-MVP extensible shell platform. | Defer broad shell-platform contracts; only extract proven patterns when current Resource Manager, Settings, or shell work needs them. |
| [Resource Manager project structure](../future/resource-manager-project-structure.md) | Medium-high structural fit once Resource Manager UI and CoreShell boundaries are proven. | Defer physical project/assembly restructuring until current UI and shell composition paths are stable. |
| [IoT device provisioning](../future/iot-device-provisioning.md) | Plausible later fit for edge/device environments. | No action now; revisit after local and initial on-premise control-plane flows are credible. |

## Feature Doc Migration Queue

Completed work should move from proposals into feature/specification docs, or
be linked from proposals when the feature/specification docs already contain
the implementation details. Some areas are large enough to process as their
own documentation slice instead of folding into this overview cleanup.

| Area | Current source | Feature doc target | Action |
| --- | --- | --- | --- |
| Container apps | [Container applications](containers/container-applications.md) current implementation section and changelog entries | [Container Apps](../resources/container-apps.md), [Application resources](../resources/application-resources.md), [Resource model](../resource-model.md), and [Networking](../networking.md) | Initial drain done for Resource Manager, deployment, replica, monitoring, ingress, relationship, and readiness behavior. Later pass should verify all changelog details are represented and remove any remaining landed detail from the proposal. |
| SQL Server | [Managed SQL Server resource](resources/managed-sql-server.md) current implementation section and changelog entries | [SQL Server resources](../resources/sql-server.md), [Application resources](../resources/application-resources.md), and [Resource identity and permissions](../resource-identity-and-permissions.md) | Initial drain done for builder, database child-resource, grant reconciliation, storage, and local container-backed bridge behavior. Later pass should verify sample-specific behavior and provider caveats. |
| Identity and access | [Identity and access](core/identity-and-access.md) | [Resource identity and permissions](../resource-identity-and-permissions.md) and [Authentication and authorization](../authentication-and-authorization.md) | Initial cleanup done; later pass should verify all Keycloak and protected-service details are represented in feature docs. |
| Resource monitoring and usage | [Resource monitoring](core/resource-monitoring.md) | [Resource Monitoring and Usage](../monitoring-and-usage.md), [Persistence](../persistence.md), and [Control Plane API](../control-plane-api.md) | Initial spec extracted. Later pass should verify provider-specific resource docs link to the spec. |
| Orchestrator deployments and environment revisions | [Orchestrator deployments and environment revisions](deployment/deployments-and-revisions.md) | [Orchestration and Deployments](../orchestration-and-deployments.md), [Container Apps](../resources/container-apps.md), and [Resource templates](../resource-templates.md) | Initial spec extracted for internal deployment records, environment revisions, replica groups, API/client read model, and authoring boundary. Later pass should drain remaining implemented controller/reconciliation detail from the proposal. |
| Observability and logging | [Logging infrastructure](core/logging-infrastructure.md), [Service telemetry and degradation](core/service-observability-and-degradation.md), and changelog entries | [Observability](../observability.md), [Resource Monitoring and Usage](../monitoring-and-usage.md), [Control Plane API](../control-plane-api.md), and [Persistence](../persistence.md) | Initial spec extracted for signal taxonomy, log-source contracts, resource events, traces, metrics, monitoring, usage, health, routes, permissions, and boundaries. Later pass should drain structured logging, UI filtering, retention, and correlation details. |
| Container hosts | [Container host abstraction](containers/container-host-abstraction.md), [Remote Docker hosts](containers/remote-docker-hosts.md), and changelog entries | [Container Hosts](../resources/container-hosts.md), [Container Apps](../resources/container-apps.md), [Programmatic resources](../programmatic-resources.md), and [Orchestration and Deployments](../orchestration-and-deployments.md) | Initial spec extracted for generic host resource shape, descriptors, resolver order, capabilities, diagnostics, runtime boundaries, and provider/launcher parity. Later pass should drain Docker-specific host details as remote host support stabilizes. |
| Storage and volumes | [Storage and volume mappings](storage/volume-mappings.md) and changelog entries | [Storage and Volumes](../resources/storage-and-volumes.md), [Container Hosts](../resources/container-hosts.md), [Application resources](../resources/application-resources.md), and [SQL Server resources](../resources/sql-server.md) | Initial spec extracted for storage/volume resource types, volume-consumer payloads, permissions, materialization observations, Resource Manager views, diagnostics, and provider/launcher parity. Later pass should drain provider-backed storage and usage details as they land. |
| Provider-created and runtime-managed resources | [Provider-created and runtime-managed resources](core/provider-created-and-runtime-managed-resources.md) and changelog entries | [Provider-created and runtime-managed resources](../runtime-managed-resources.md), [Resource model](../resource-model.md), [Container Apps](../resources/container-apps.md), and [Orchestration and Deployments](../orchestration-and-deployments.md) | Initial spec extracted for source, management mode, visibility, ownership, cleanup behavior, Resource Manager filtering, API/client projection, container app runtime replicas, storage-owned hidden volumes, and provider/launcher parity. Later pass should drain provider-created durable-resource examples as they land. |
| Cross-language local development | [Cross-language local development](core/cross-language-local-development.md) and changelog entries | [Launchers](../launchers-and-app-hosts.md), [CloudShell CLI](../cli.md), [SDK clients](../sdk-clients.md), and language resource docs | Initial drain done for C# launcher, TypeScript/JavaScript, Java launcher, CLI `--data-dir`, SDK-client, and sample behavior. Later pass should verify package status and generated-client guidance. |
| Resource model providers | [Resource graph and runtime separation](core/resource-graph-and-runtime-separation.md) provider sections and diagrams | [Resource model providers](../resource-model-providers.md), [ResourceDefinition structure](../resource-definition-structure.md), and [Extensions](../extensions.md) | Initial spec extracted for type providers, class/type definitions, capability and operation providers, graph validators, apply providers, projection providers, provider package shape, and parity expectations. Proposal diagrams that still mention generated wrappers or persistence strategies are now marked proposal-only. |
| UI/CoreShell composition | [UI composition library](core/ui-composition.md), [Shell composition](../future/shell-composition.md), and changelog entries | [UI composition](../ui-composition.md), [Shell customization](../shell-customization.md), and extension docs | Initial proposal cleanup done; current package split and CloudShell/CoreShell consumption now live in feature docs. Later pass should verify landed CoreShell services, Blazor adapter, route/link resolution, Settings, and navigation behavior against code and changelog entries. |

## Current proposal order

Work proposal areas in the current product order:

For MVP work, pull from these proposals only when the slice improves the
current local app-development loop: sample confidence, app-centric Resource
Manager understanding, preflight diagnostics, runtime-impacting
configuration/secrets/identity clarity, persisted-state handoff, or release
hardening.

1. Resource model migration through
   [Resource graph and runtime separation](core/resource-graph-and-runtime-separation.md)
   and [Resource Definition Structure](../resource-definition-structure.md).
   Keep resource templates ResourceDefinition-based, use Resource Manager
   apply as the create/update/import path, retire old provider-template
   serialization where graph-backed providers can round-trip definitions, and
   document non-parity instead of preserving obsolete compatibility wrappers.
2. Container app orchestration convergence through
   [Container applications](containers/container-applications.md) and
   [Orchestrator deployments and environment revisions](deployment/deployments-and-revisions.md).
   Container apps remain the user-facing resource; internal orchestrator
   services, replica groups, replicas, routing bindings, deployment attempts,
   and environment revisions are Resource Manager/orchestrator artifacts
   derived from accepted resource state.
3. Application environment management path through
   [Container applications](containers/container-applications.md), app-owned
   exposure/discovery, virtual networks, public endpoints, load balancers, and
   logical DNS/name mapping through [Virtual network resource](networking/virtual-network-resource.md),
   [Load balancer resource](networking/load-balancer-resource.md), and
   [DNS and name mapping](networking/dns-and-name-mapping-resource.md). Keep
   `cloudshell.service` semantics bounded until
   [Orchestrator deployments and environment revisions](deployment/deployments-and-revisions.md)
   clarifies the shared deployment/orchestrator service model.
4. MVP convergence and Resource Manager reliability through
   [Platform foundations](core/platform-foundations.md), keeping supported
   samples green and Application Topology as the broad graph-backed
   local-development proof. Stabilize UI structures when they explain the
   resource graph, app-centric operations, or runtime artifacts already used
   by the MVP; do not turn this into broad shell-composition or extension API
   work before the graph/runtime boundary is stable.
5. Readiness diagnostics for already-supported start, update, storage,
   identity, route, DNS/name, and provider-reconcile paths.
6. [Configuration and secrets access](services/secrets-management.md) with
   targeted [identity and access](core/identity-and-access.md) polish
7. Stateful application foundation through
   [Storage and volume mappings](storage/volume-mappings.md)
8. Identity validation beyond the built-in provider through
   [Identity and access](core/identity-and-access.md), including a third-party
   OIDC/OAuth validation sample
9. [Lifecycle orchestration](core/lifecycle-orchestration.md),
   [traceability/logging infrastructure](core/logging-infrastructure.md),
   [resource monitoring](core/resource-monitoring.md),
   [resource recovery](core/resource-recovery.md), and
   [service telemetry and degradation](core/service-observability-and-degradation.md)
10. Host and runtime foundation through
   [Container host abstraction](containers/container-host-abstraction.md)
11. [Remote Docker host completion](containers/remote-docker-hosts.md), behind
   the local/default host path
12. Runtime ownership decisions through
   [Runtime-managed resources](core/provider-created-and-runtime-managed-resources.md)
13. [UI composition library](core/ui-composition.md), only where it fixes
    regressions or directly supports the current shell, Resource Manager, and
    Settings experience. Broader CoreShell shell contracts and Resource Manager
    project restructuring remain [future directions](../future/).
14. Cross-language local-development hardening through
    [Cross-language local development](core/cross-language-local-development.md)
    only where it keeps the local-development model ecosystem-neutral without
    distracting from MVP stabilization. The first distribution slice is the
    installed CLI starting or reusing the default local-development host
    daemon; launcher packages remain optional authoring layers over that same
    CLI/API path.
15. Deployment artifact loading through
    [Deployment artifacts](core/deployment-artifacts.md) before
    broad Resource Manager create/edit UI for application resources. The first
    slice should define the Control Plane artifact-store setting, upload API,
    artifact revision reference, and local mode versus deployment artifact UX
    boundary.
16. Intent-first resource authoring through
    [Intent-first resource authoring](core/intent-first-resource-authoring.md)
    only after the ResourceDefinition apply, provider diagnostics, and
    Resource Manager review/apply path are stable enough to make generated
    drafts trustworthy. The first slice should draft a ResourceTemplate for
    review and validation, not execute autonomous deployment or provider-native
    infrastructure generation. External-format import, deployment projection,
    IoT provisioning, and edge/device resource management remain
    [future directions](../future/) until a concrete near-term value slice is
    accepted.

Use [Roadmap](../roadmap.md) for the reasoning behind this order and the
concrete MVP execution plan.
