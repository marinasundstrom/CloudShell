# Roadmap

This roadmap describes the next product direction for CloudShell. It links to
the detailed proposals and domain docs rather than duplicating every design
detail here.

CloudShell is moving toward an on-premise control plane: a resource shell where
teams can model applications first, then add environment infrastructure,
networking, deployment, and operational control as the solution grows.

## Current Foundation

The current foundation is a resource model with programmatic declarations,
Resource Manager UI, provider-owned configuration, endpoint-network mappings,
identity-backed service integrations, sample smoke coverage, and a Control
Plane API.

Useful references:

- [CloudShell Terminology](terminology.md)
- [Feature and Specification Index](features.md)
- [Domain model](domain-model.md)
- [Resource capabilities](capabilities.md)
- [System design guidelines](system-design-guidelines.md)
- [Programmatic resources](programmatic-resources.md)
- [Control Plane API](control-plane-api.md)
- [CloudShell and Aspire](cloudshell-and-aspire.md)
- [CloudShell CLI](cli.md)
- [Cross-platform support](cross-platform-support.md)
- [MVP sample seam audit](mvp-sample-seams.md)
- [Resource Monitoring and Usage](monitoring-and-usage.md)
- [Orchestration and Deployments](orchestration-and-deployments.md)
- [Container Hosts](resources/container-hosts.md)
- [Storage and Volumes](resources/storage-and-volumes.md)
- [Refactoring tracker](refactoring.md)
- [Cross-language local development proposal](proposals/core/cross-language-local-development.md)
- [Deployment artifacts proposal](proposals/core/deployment-artifacts.md)
- [Intent-first resource authoring proposal](proposals/core/intent-first-resource-authoring.md)
- [Container applications proposal](proposals/containers/container-applications.md)
- [Future directions](future/)

## Current Direction

CloudShell should now move in one clear line:

1. **Ship a coherent local-development MVP.** The user should be able to run a
   realistic app locally, understand it from Resource Manager, operate it,
   diagnose expected failures, and decide whether the graph is ready to
   persist.
2. **Tie off the app-centric Resource Manager loop.** Prioritize application
   overview, endpoints, discovery, names, storage, identity, configuration,
   secrets, logs, traces, monitoring, lifecycle actions, and diagnostics in the
   context of the app.
3. **Keep cross-platform support as a release guardrail.** The platform
   abstraction, command planning, host capability checks, and CI matrix should
   prevent new macOS-only assumptions while the product remains local-first.
4. **Prepare for agents and reconciliation only where it prevents a dead end.**
   The MVP-relevant direction is to push host-local execution and observation
   down behind typed, provider-owned execution boundaries. Do not build
   clustering, remote placement, regions, or multi-host replica scheduling as
   MVP work.
5. **Move to on-premise hosting after the MVP is stable.** The first
   post-MVP on-premise scenario should reuse the same resource model,
   Control Plane boundary, provider contracts, and cross-platform host
   capability model instead of introducing a parallel product shape.

Use this decision filter for new work:

- If it makes the primary app-management loop clearer or more reliable, it is
  a candidate for the active MVP queue.
- If it only expands future platform scope, defer it unless a supported sample
  exposes a blocking gap.
- If it touches host-local execution, prefer the platform abstraction and
  assignment/reconciliation-shaped boundaries over direct Control Plane calls
  into OS tools or provider runtimes.
- If it introduces a new resource concept, first decide whether the concept is
  user-authored, Control Plane operational data, provider-owned runtime state,
  or a future direction.

## Current Focus Stack

Use this focus stack when choosing the next implementation slice. The roadmap
is deliberately biased toward work that improves the current local
app-development loop before broader platform expansion.

| Priority | Focus | Why now | Action |
| --- | --- | --- | --- |
| 1 | MVP loose-end tie-off | The main capabilities exist, but several exposed paths still read as transitional. | Tie off sample-local seams, deferred runtime adapters, routing reactions, diagnostics, UI consistency, and docs so current behavior feels intentional. |
| 2 | Supported sample reliability | Application Topology and the switch-readiness samples are the strongest proof that the model works end to end. | Keep smoke coverage green, run the broad proof regularly, and turn only concrete sample confusion into work. |
| 3 | App-centric Resource Manager experience | The product value is whether a developer can understand and operate the app from Resource Manager. | Keep endpoints, names, storage, identity, settings/secrets, logs, traces, monitoring, activity, and actions in the app context. |
| 4 | Readiness and capability reasons | Supported actions should fail early with clear reasons. | Tighten host, credential, route, port, storage, identity, grant, and provider-readiness diagnostics for paths already exposed. |
| 5 | Container app orchestration and runtime diagnostics | Container apps are the hardest proof of the resource/runtime boundary. | Consolidate first start, image update, replica-slot reconciliation, routing rebinding, cleanup, and readable failure diagnostics only where current samples expose them. |
| 6 | ResourceDefinition apply/export convergence | The old provider path has mostly been retired; remaining compatibility should be explicit. | Remove or document remaining obsolete template/runtime bridges where graph-backed providers can round-trip definitions. |
| 7 | Ecosystem-neutral authoring boundary | CloudShell should not become a C#-only local-development tool, but launchers, deployment artifact loading, and assistant drafting should not distract from MVP stabilization. | Keep CLI, launcher/profile, TypeScript/JavaScript, Java, SDK, deployment artifacts, and future intent-first authoring aligned with the same ResourceDefinition and Control Plane boundary; defer packaging polish and generated-draft workflows that do not improve supported local runs. |
| 8 | Cross-platform support guardrail | The baseline CI and platform abstraction are now in place; they must keep future MVP work portable. | Maintain the cross-platform CI matrix, support-tier docs, command planning, host capability checks, and diagnostics as guardrails around CLI, launcher, app-runtime, container-host, and host-networking changes. |
| 9 | Agent/reconciliation preparation | Agents are the future execution boundary, but the MVP should not become a distributed-systems project. | When host-local execution changes, shape it as typed provider work with desired/observed state, leases, diagnostics, and idempotency in mind; defer remote agents, placement, regions, and clustering. |
| 10 | CoreShell, UI composition, and shell structure | Useful only when it reduces current Resource Manager, Settings, notification, or shell drift. | Use the CoreShell Fluent UI sample as the reference shell and extract only proven common shell building blocks; pause persistence, marketplace, and user-personalized shell-platform work. |

## Authoritative Milestones

Milestones in this file are the authoritative product scope. Proposal status
tables, progress notes, and the execution plan below should stay aligned with
these milestones instead of redefining release scope independently.

### MVP

Goal: make CloudShell useful first as a combined-hosted local-development
control plane while preserving the split-hosting and team-owned environment
paths.

MVP scope:

| Area | Required outcome |
| --- | --- |
| Container Apps, Version 1 | Container app resources can be declared, inspected, started, stopped, updated by image/revision, configured with replicas, connected to the default container-host path, and treated as the managed-service configuration surface described in the [Container applications proposal](proposals/containers/container-applications.md). |
| Application exposure, discovery, and names | Application resources, application-level discovery, and the first logical DNS/name-mapping projection make resource-to-resource and user-to-endpoint access understandable from Resource Manager. `cloudshell.service` remains optional for logical facades, imported services, or advanced routing instead of being required for normal container app exposure. |
| Network primitives | Virtual networks, endpoint requests, endpoint network mappings, configured endpoint mappings, load-balancer routes, public endpoint exposure, and host-local networking provide enough routing to expose common container app scenarios with clear diagnostics. |
| Storage and volume mappings | Mountable volume resources and app volume attachments can be modeled, inspected, and mapped into container apps or executables through provider-neutral storage intent, without forcing object storage, databases, or backups into the same abstraction. |
| Identity, Built-in | The built-in ASP.NET Core identity provider is enough for simple local-development cases: it can provision resource identities, issue scoped resource-permission tokens, expose provisioned resource identity clients through the standard principal lookup hook, and enforce permissions for Control Plane actions, configuration reads, and secret reads. |
| Identity, external OIDC validation | The identity model is proven against at least one standards-compliant third-party OIDC/OAuth provider, such as Keycloak, without changing the CloudShell resource identity contract. |
| App settings and secrets integrations | App settings, configuration-setting references, and secret references work through programmatic declarations, runtime transfer, redaction, authorization, and Resource Manager views that explain runtime impact without leaking values. |
| Ecosystem-neutral authoring path | CloudShell's local-development model is not C#-only. The MVP must preserve the ResourceDefinition and Control Plane boundaries needed for the CloudShell CLI, TypeScript/JavaScript, and future language SDKs to author graphs, launch or attach to a host, and use Resource Manager without becoming parallel Control Plane implementations. First full SDK delivery can land after MVP stabilization. |
| UX polish | Resource Manager common workflows are solid and understandable without being overdone: diagnostics are actionable, generated details are useful, and the app-centric path for identity, configuration, secrets, networking, storage, telemetry, and app controls is discoverable without bespoke sample code. |
| Samples should work | Supported samples build and smoke-test, including combined hosting, split hosting, container host, settings and secrets, host virtual networking, load balancer, project references, third-party identity, application topology, and container app deployment. |

The execution plan and readiness notes below define the implementation order
for reaching this MVP and then expanding beyond it.

### Post-MVP: Initial On-Premise Hosting

Goal: make CloudShell credible as an initial on-premise control plane after the
MVP local/team-owned flows are stable.

The first post-MVP scenario should prove that CloudShell can host and manage a
small on-premise environment with acceptable UI management, provider-backed
networking on more than one operating-system/runtime target, and integration
points that are good enough for real platform experimentation.

This scenario should still use the normal shared-environment shape: one
environment authority, one logical region, and one or more host execution
participants. It should prove the agent/reconciliation boundary for host-local
execution before attempting multi-region placement, clustering, or broad
distributed scheduling.

Required outcome:

| Area | Required outcome |
| --- | --- |
| On-premise host scenario | A deployable combined or split Control Plane/UI setup can manage resources against a team-owned host or host runtime, not only a developer workstation. |
| Agent/reconciliation boundary | Host-local provider execution can run through typed assignments, leases, heartbeats, and observed state while the Control Plane remains the authority for desired state, identity, policy, operation history, and diagnostics. |
| Management UI | Resource Manager can inspect and operate the environment well enough for platform operators, with permission-aware controls and a read-only mode for environments where UI writes must be disabled. |
| Cross-platform networking | Virtual networks, ingress/gateway/load-balancer providers, public endpoint mapping, and DNS/name mapping work through provider capabilities rather than OS-specific assumptions. |
| Network-level discovery | Services can be discovered through network or provider-level mechanisms such as DNS, service registries, or Eureka-like systems, separate from the Aspire-compatible app environment-variable mapping. |
| Integration points | Providers, CloudShell extensions, webhooks, WebSocket/streaming subscriptions, and API clients can react to resource events and lifecycle state without replacing the core orchestrator path. |
| Validation samples | More complex samples prove multi-resource application topology, public ingress, DNS/name mapping, service discovery, identity-backed configuration/secrets, and operator UI workflows. |

### Future Direction: IoT and Edge Devices

This is not an active milestone. It remains a strategic direction until the
local-development MVP and the first on-premise control-plane scenario are
credible.

Goal: let devices and edge workloads securely join a CloudShell environment,
appear in the resource graph, use existing CloudShell-managed services, and be
developed and diagnosed through Resource Manager without requiring CloudShell
to copy a public-cloud IoT service catalog.

The first IoT story should be a provisioning and resource-management story,
not a full IoT Hub replacement. A device should be able to connect with a
pre-issued bootstrap credential, such as a certificate or enrollment token,
ask the environment for provisioning, and be reconciled into the CloudShell
catalog as a device resource. From there, CloudShell can assign a principal,
apply access grants, expose configuration or service connection metadata,
record activity, and observe health and telemetry through the same Control
Plane and Resource Manager concepts used by other resources.

Required outcome:

| Area | Required outcome |
| --- | --- |
| Device onboarding | A provisioning provider can validate a device bootstrap credential and create or reconcile a device resource. |
| Device identity | Devices or device workloads receive principals and access grants through the standard identity and access model. |
| Service access | Device workloads can use existing CloudShell-managed services such as configuration, secrets, databases, message brokers, telemetry, or application endpoints when granted access. |
| Local development experience | Developers can use Resource Manager to understand provisioning status, service access, dependencies, health, telemetry, and failures for simulated or physical devices. |
| Resource Manager visibility | Device resources show health, activity, telemetry, relationships, and provider-owned non-secret attributes. |
| Provider boundary | Certificate validation, hardware-root checks, enrollment groups, and provider-specific device state remain provider-owned. |

This is tracked by the
[IoT device provisioning future direction](future/iot-device-provisioning.md).

### Post-MVP: Extensible Shell Composition

Goal: make CloudShell UI an independently useful extensible shell platform
that Resource Manager can fully exploit, rather than treating Resource Manager
as the shell itself.

Required outcome:

| Area | Required outcome |
| --- | --- |
| Layout contributions | Extensions can add validated navigation nodes and addressable content nodes. Navigation hierarchy uses menus, menu item groups, menu items, and sub-menu items; content hierarchy uses pages, sub-pages, slots, section containers, and sections. |
| Standard settings | Extensions can add settings pages, sub-pages, slots, section containers, and sections to a common settings surface while keeping UI-local, Control Plane-backed, provider-backed, and external-service-backed state boundaries explicit. |
| Notifications | Extensions and Control Plane event sources can produce notifications through CoreShell notification and toast contracts, with CloudShell adapting those contracts to Control Plane-owned storage, durable off-canvas notification history, persistence, per-recipient delivery/read state, and fan-out to users or principals that should receive them. |
| Extension areas | Shell and Resource Manager pages expose slots and section containers so extensions can add scoped content without replacing whole pages or relying on brittle page internals. |
| CoreShell reference shell | A clean CoreShell-only Fluent UI app can use CoreShell modules, navigation, targets, section outlets, Blazor content projection, and a sample extension module without referencing CloudShell Hosting, Resource Manager, or the Control Plane. This app is the reference and testbed for common shell building blocks before CloudShell customizes or consumes them. |
| Standalone composition proof | A clean Blazor app can use `CoreShell.Composition` and `CoreShell.Composition.Blazor` with plain Bootstrap styling for lower-level graph and renderer experiments below CoreShell. |
| Resource Manager alignment | Resource Manager uses the generic shell composition primitives for navigation presenters, addressable content nodes, settings, notifications, slots, and section containers while keeping resource-specific provider UI on Resource Manager contracts. |

## MVP Execution Plan

This section is the current task queue. Keep it focused on implementation
slices that move the MVP forward; proposal documents remain the design
trackers, [ADR](../ADR.md) remains the durable decision log, and
[Changelog](../CHANGELOG.md) remains the landed-change history.

The projected focus order is a planning tool, not a constraint: re-evaluate it
during implementation whenever a different slice better serves the immediate
MVP goal described in [CloudShell goal](goal.md).

### Roadmap Item Classification

Every roadmap issue should carry a primary category so backend capability work
and user-facing experience work can be prioritized independently by impact.

Use these categories:

| Category | Use when | Planning rule |
| --- | --- | --- |
| Feature | The work introduces a new product capability that naturally spans domain/API/provider behavior and Resource Manager or shell UX. | Keep the end-to-end outcome visible, but split backend and UX implementation slices when they can land or be prioritized separately. |
| Backend enhancement | The work improves the domain model, Control Plane, provider behavior, API/client projection, persistence, validation, diagnostics, or sample reliability without requiring a new user workflow. | Prioritize by unblock value, correctness, sample/release risk, and how much UX or automation it enables. |
| UX enhancement | The work improves Resource Manager, shell navigation, generated details, forms, inline views, visual hierarchy, labels, affordances, or workflow clarity without changing the underlying domain capability. | Prioritize by user impact, frequency, confusion removed, and how much it keeps operators in context. UI polish can be high-priority when it materially improves the primary MVP workflow. |

If a roadmap item has both backend and UX value, record the user outcome as the
feature and track separable backend or UX enhancements below it. Do not bury a
high-impact UX improvement behind lower-impact backend work just because it is
smaller or presentation-focused.

### MVP Readiness Snapshot

The MVP is now a working baseline that needs stabilization and focused
adjustments, not another round of concept discovery. The core resource model,
endpoint/network mapping model, container app
surface, local/default container host path, storage/volume bridge,
configuration and secrets resources, built-in identity flow, Keycloak-backed
identity validation, Resource Manager details, and supported sample smoke
coverage all exist.

Recent slices also moved the diagnostic surface forward. SQL Server now reads
as a database service rather than a container app, declared and live databases
are visible from the SQL Server resource, requested-versus-effective database
grant status prevents misleading access-control claims, and trace views
distinguish failed spans from recovered flows that contain error spans. The
shared Traces page can aggregate all sources while trace-detail links return
users to the relevant resource context.

RabbitMQ now follows the same managed-service direction as SQL Server:
`application.rabbitmq` is a broker resource with AMQP and management endpoint
projection, lifecycle actions, and optional volume-backed local state while
the local provider keeps the RabbitMQ container and bootstrap credentials
behind the provider boundary. Specialized broker-native Resource Manager UI is
intentionally deferred; users should continue to use the RabbitMQ management UI
for queues, exchanges, bindings, users, virtual hosts, policies, and cluster
settings until CloudShell models the portable workflows it should own.
The next maturity step for built-in managed services is not only deeper UI:
identity-backed access, requested-versus-effective provider grants,
non-secret audit/resource events, and provider reconciliation diagnostics need
to be planned as one service parity track so built-in resources do not stop at
container-backed lifecycle and endpoint projection.

Recent shell-composition slices proved the generic menu/page/section graph,
composition-backed sidebar migration, composition-backed Settings sections,
route-backed tab links, section address modes, active composition menu links,
and the future router direction. That work is valuable platform direction, but
it is no longer the most urgent MVP track. Further composition work should
pause unless it fixes a regression in the current shell, directly supports the
Resource Manager app-centric path, or removes duplication that is blocking a
supported MVP workflow. CloudShell-specific settings abstractions should layer
on top of the generic composition graph when needed instead of pushing
settings-menu concerns into the core composition model.

Future on-premise scale-out should allow multiple Control Plane API replicas
while coordinating singleton duties through a primary controller lease and
moving eligible subsystems, such as log readers, telemetry ingestion, health
polling, notification fan-out, and provider reconciliation, into independent
workers. The broader direction also includes CloudShell agents for scaling
CloudShell-managed services across hosts, regions, and provider-specific
runtime boundaries; see
[CloudShell agents and clustering](future/agents-and-clustering.md). This is
important platform direction, but MVP relevance should stay limited to the
agent and reconciliation foundation: typed assignments, leases, heartbeats,
observed state, and one provider operation moved behind an agent-side handler.
It should not displace the current local-development MVP stabilization unless
a new subsystem would otherwise bake in single-process assumptions.

The cross-platform baseline is now part of the foundation rather than a
separate discovery track. Keep the CI matrix, support-tier documentation,
host operating-system detection, command planning, and host capability
diagnostics healthy as guardrails around MVP work. New provider, launcher,
CLI, and host-runtime changes should prove their behavior through those
abstractions instead of adding OS-specific branches in higher layers.

The main stabilization lens is now the UI foundation that the MVP already
uses. The priority is to make shell chrome, Resource Manager pages, resource
menus, settings sections, generated details, selectors, tables, alerts,
actions, route-backed tabs, and provider-owned views coherent and
maintainable. This does not mean broadening the shell-composition feature set
for MVP. It means hardening the abstractions and implementations that are
already in the product so they can evolve without rewrites and later become
extension points. Balance internal needs with future opportunity: favor
stable IDs, clear ownership boundaries, validated contribution contracts,
reusable presenters, predictable routing/link behavior, and testable UI
patterns, but do not expose unstable internals as extension APIs before the
Resource Manager and Settings experience proves them.

Consistency in the current UI is part of that foundation. Equivalent concepts
should use equivalent structure and presentation: the same page anatomy,
section hierarchy, local navigation, label rules, component choices,
iconography, table/list/card patterns, alert styling, selector behavior,
action button states, empty/loading/error states, and route-backed navigation
patterns across shell pages, Settings, generated resource details, and
provider-owned Resource Manager views. Icons should reinforce resource kind,
action intent, or navigation target using the same icon language in related
views. Consolidation is valuable when it reduces drift or makes a common
behavior testable; it should not become an abstract component exercise
detached from the active MVP workflows.

Maintainability and reuse are part of the UI stability target. When several
views solve the same problem, prefer a shared component, presenter, helper, or
view model that preserves localization, accessibility, authorization,
read-only behavior, and loading/error semantics. Reuse should be driven by
real duplication in current MVP surfaces: selectors, resource references,
principal pickers, status pills, alerts, action bars, generated detail rows,
resource tables, and local navigation are good candidates. Avoid extracting
generic abstractions that hide important resource-specific behavior or make
provider views harder to understand.

This stabilization does not eliminate the need for a later shell refactoring.
Once CloudShell starts building shell-owned abstractions and extensibility
points on top of the reusable UI composition library, existing Resource
Manager and Settings presenters will need to be adapted into those contracts.
That should be an intentional layer boundary, not an accidental exposure of
today's internal components. The MVP goal is to make the current UI coherent
and maintainable enough that the later extension layer can be built from
proven patterns.

The supported sample surface remains the main MVP proof. Docker-backed
container-app starts now distinguish host readiness from longer resource
actions, bound image materialization commands with runtime diagnostics, and
publish project-backed images for the current host architecture by default.
The previously failing ReplicatedContainerHealth and SignalR container-app
smoke paths pass individually on the local Docker host, and the broad
`CloudShell.Sample.Tests` suite has been re-run end to end on a healthy Docker
host with 115 passing tests. The remaining MVP work should therefore bias
toward release-quality local-development behavior rather than opening new
platform fronts or repeatedly polishing secondary editor surfaces:

- Keep supported samples building and smoke-testing.
- Keep targeted fake-adapter, in-memory, runtime-handler, and Docker-backed
  sample tests green while closing only concrete failures or confusing sample
  behavior.
- Keep process-backed sample smoke tests serialized until the samples stop
  sharing mutable runtime resources. Current parallel-safe coverage is limited
  to fake-adapter and in-memory sample tests that use recording runners,
  isolated service providers, and unique temporary paths.
- Make the primary application Resource Manager path understandable without
  sample-specific knowledge.
- Continue provider runtime enforcement for modeled routing policy, especially
  container app session affinity/sticky routing now that the resource intent
  and orchestration routing binding metadata exist.
- Stabilize the UI contracts behind the main path before adding new UI
  surface area: resource view registration, menu grouping, route/link
  resolution, shared selectors, action controls, status pills, generated
  details, alerts, resource tables, and page-level empty/error/loading states
  should have reusable patterns and tests where regressions are likely.
- Tighten action capability reasons, diagnostics, and ProblemDetails responses
  around already-supported flows.
- Spend UI polish on the app-centric experience, overview details, readiness
  diagnostics, and clear next actions until the experience is solid. Do not
  overbuild or spend multiple cycles on tabs such as Environment unless they
  block the main app experience or leak sensitive data.
- Use resource display names or resource names in user-facing diagnostics,
  observability summaries, selectors, graph labels, and alerts when that makes
  the UI easier to read. Keep Resource ID visible in canonical identity/detail
  surfaces, deep links, and raw diagnostic fields, but do not lead routine UI
  messages with internal IDs when a readable label exists.
- Keep endpoint contracts address-less and require concrete reachability through
  endpoint network mappings.
- Avoid broad IAM, deployment history, autoscaling, advanced service resources,
  broad shell-composition work, and on-premise hosting work unless a supported
  MVP sample exposes a blocking gap.

### Current MVP Alignment

The next MVP target is a loose-end tie-off pass for the local app-development
loop, not another platform front and not a feature-complete portal push. When
choosing work, prefer the slice that removes a half-baked feeling from a path
that already exists: the developer can run a realistic distributed app,
understand it from Resource Manager, diagnose expected failures, and persist
the graph without needing proposal context.

Most supported samples have already moved to graph-backed
`ResourceDefinition` state and the old provider projects have been removed
from active hosts. The remaining work should therefore stop talking about the
Resource Model POC as if the whole migration is still ahead. Treat it as a
switch-over hardening pass: remove or clearly label the remaining
compatibility bridges, replace sample-local seams only where they are visible
in supported workflows, and keep deferred behavior documented as intentionally
out of scope instead of leaving users to infer whether it is broken.

Application Topology is the broad proof for this milestone. It should not need
to prove every future provider capability, but it should not feel stitched
together from unrelated experiments. SQL credentials, storage and volume
materialization, Configuration Store and Secrets Vault startup, identity
grants, local DNS/name mapping, logs, traces, metrics, resource relationships,
and shutdown cleanup should have clear ownership, readable diagnostics, and
Resource Manager explanations. Where the implementation remains sample-local,
the roadmap should either schedule the provider-owned seam or explicitly mark
the sample-local behavior as accepted for MVP.

Plan and verify MVP work through use cases. The priority use cases are:

1. Declare a realistic distributed app in code, run it locally, and see every
   relevant resource projected without leaking secrets.
2. Open an application resource and understand what it runs, what it depends
   on, what depends on it, how it is exposed, and which names route to it.
3. Start, restart, or update an app and get actionable Control Plane feedback
   before or during failure, surfaced in Resource Manager where the user is
   already working.
4. Verify runtime-impacting settings, secret references, identities, and
   access grants from the app context without turning secondary editor tabs
   into the main experience.
5. Diagnose runtime behavior through logs, traces, metrics, monitoring,
   activity, health, and recent failures from the resource page.
6. Persist the resource graph deliberately and understand what changes from
   transient code-first declarations to durable Control Plane state.

The most urgent near-term slice is loose-end identification and triage:
run the graph-backed switch-readiness samples as the standing proof, list the
sample-local seams and compatibility bridges still required, and decide for
each one whether to fix it now, document it as an accepted MVP bridge, or move
it behind a provider-owned adapter. Do not open a new platform capability just
because a related proposal has more complete long-term design.

The current re-alignment keeps the MVP in stabilization mode. Recent work has
made the main surfaces credible, so the next useful work is not a new feature
front. Focus on the places where the local development loop still feels
surprising: names and exposure links should be navigable and readable, routine
messages should use resource names instead of IDs, lifecycle cleanup should be
visible and reliable when the host shuts down, and resource pages should show
the operational signals needed to understand the running app without jumping
between unrelated global views.

Use this decision filter:

1. Does the work stabilize an existing UI structure, component pattern,
   iconography pattern, navigation pattern, selector, generated detail
   surface, provider-owned view, or shell/Resource Manager abstraction used by
   the MVP?
2. Does it reduce real duplication or drift in current UI code while
   preserving localization, accessibility, authorization, read-only behavior,
   and loading/error states?
3. Does the work make Application Topology or another supported sample more
   faithful, repeatable, or diagnosable?
4. Does it keep users on the application resource page while explaining
   endpoints, dependencies, exposure, storage, settings/secrets, identity,
   telemetry, monitoring, or activity?
5. Does it explain an action failure before dispatch, with stable capability
   reasons, diagnostics, or ProblemDetails instead of provider exception text?
6. Does it clarify the code-first-to-`Persist()` handoff without mixing in
   deployment behavior?
7. Does it remove confusion from labels, generated details, resource
   references, observability labels, or not-found/read-only/transient states in
   the primary app workflow?

If the answer is no, defer the work unless it is a security fix, keeps the
supported sample suite green, or removes a direct blocker from one of the
items above. In particular, do not spend additional MVP cycles on broad
Environment-tab polish, IAM administration, shell-composition implementation,
deployment history, autoscaling, external deployment projection, or
on-premise-hosting features unless they become blocking sample evidence.

### Local Development MVP Target

For the local development experience, the most urgent target is a developer
who starts with programmatic declarations, runs a realistic distributed app
locally, diagnoses failures from Resource Manager, and persists the graph when
it is ready to become Control Plane state. Deployment to a target environment
remains separate and waits for the orchestrator deployment API.

Prioritize the remaining local-dev work in this order:

1. **Stable UI foundation for current surfaces.** Make the UI structure,
   components, iconography, local navigation, generated details, selectors,
   tables, alerts, actions, and status/empty/loading/error states consistent
   across the shell, Settings, Resource Manager, and provider-owned views that
   already exist. Prefer maintainable shared components and presenters when
   repeated behavior is proven in current MVP surfaces. Keep this foundation
   extensibility-aware through stable IDs, ownership boundaries, link
   resolution, and contribution contracts, but do not open broad extension APIs
   until the internal surfaces are stable.
2. **Application Topology confidence.** Keep the smoke suite green and use the
   Application Topology sample as the broad MVP proof. The sample should be
   faithful enough to exercise the real local-development path: container or
   project-backed apps, SQL Server with mounted storage, configuration,
   secrets, identity-backed access, logs, traces, local exposure,
   load-balancer or public endpoint relationships, DNS/name mapping, and a
   deliberate failure path. The UI should explain that topology without
   sample-specific knowledge. Treat this as the next planning anchor: each
   candidate slice should say which Application Topology run, resource page,
   or failure path it improves. Recent execution-boundary work now keeps
   ASP.NET Core project lifecycle actions on the same provider execution path
   as the managed-service and container runtime operations used by this sample.
3. **Resource relationship comprehension.** Keep the focused dependency and
   dependent graph scoped to the current resource. The first graph, related
   actions, and related health/readiness details now exist; next work should
   refine labels, empty states, navigation, and diagnostic correlation only
   where Application Topology or another supported sample proves confusion.
   Do not expand this into broad graph authoring, import, or code-generation
   features for the MVP.
4. **App-centric Resource Manager path.** Make the application resource page
   the operator entry point for endpoints, service discovery, exposure,
   storage, runtime-impacting settings/secrets, identity, logs, traces,
   monitoring, inbound names, immediate dependencies, incoming dependents, and
   related activity. Prefer improvements that keep the user in this context
   over polishing secondary editor tabs.
5. **Readiness diagnostics before failure.** Before Start, Restart, image
   update, configuration update, or provider reconcile fails, Resource Manager
   should explain missing container hosts, unavailable credentials, occupied
   ports, unsupported host capabilities, unsafe volume mappings, unresolved
   setting/secret references, missing identity grants, route conflicts, and
   DNS/name materialization gaps. Prioritize feedback loops that materially
   improve the supported local-dev experience, especially when a sample exposes
   a blocker or confusing failure.
6. **Configuration, secrets, and identity clarity.** The app experience should
   make runtime-impacting settings and secret references understandable:
   references must be visible without leaking secret values, identity grant
   status must be clear where it affects startup or service calls,
   access-control assignment and revocation must remain principal-shaped, and
   failures should produce stable diagnostics and ProblemDetails. Keep this
   solid, not exhaustive; do not treat the Environment tab itself as the MVP
   target unless it blocks this flow.
7. **Persisted-state handoff.** `Persist()` should stay a clear boundary from
   code-first local declarations to durable Control Plane/provider state.
   Local development can continue after persistence, but deployment remains a
   separate orchestrator concern and should wait for the orchestrator
   deployment API.
8. **Release hardening.** Keep generated details, action availability,
   activity records, not-found states, read-only behavior, transient
   declaration warnings, and sample documentation consistent across supported
   samples.

Defer the following unless a supported MVP sample exposes a blocking gap:
broad IAM administration, deployment history, autoscaling, advanced service
resources, shell-composition implementation, provider-backed DNS propagation,
network-level service registries, external deployment projection,
external-format import/code generation, and initial on-premise hosting.

Active MVP tie-off queue:

1. **Classify exposed seams in supported samples.** Inspect Application
   Topology, ReplicatedContainerHealth, ContainerAppDeployment,
   HostVirtualNetwork, LoadBalancer, ThirdPartyIdentity, SettingsAndSecrets,
   and SplitHosting. Record only user-visible or smoke-critical seams, and mark
   each one as fix-now, accepted MVP bridge, or post-MVP deferred. Track the
   current classification in the
   [MVP sample seam audit](mvp-sample-seams.md).
2. **Tie off Application Topology first.** Keep it as the broad proof for
   storage-backed SQL, Configuration Store, Secrets Vault, identity grants,
   project-backed apps, local exposure, DNS/name mapping, logs, traces,
   metrics, failure responses, and host shutdown cleanup. Focus the next pass
   on DNS/name mapping, local exposure, and any remaining sample-local identity
   bridge that is visible in the app-management loop.
3. **Make exposed container-app runtime behavior coherent.** Keep rich rollout
   history, autoscaling, traffic splitting, and public deployment semantics
   deferred, but make start, stop, restart, image update, replica update,
   readiness, route rebinding, cleanup, and diagnostics read as one local
   provider path for supported samples.
4. **Close routing, name, and readiness rough edges.** For the local/default
   path, DNS/name mappings, load-balancer routes, endpoint mappings, and
   generated links should lead to reachable endpoints when one exists and
   explain provider gaps when one does not. Expected failures should surface
   stable diagnostics, capability reasons, or ProblemDetails instead of raw
   provider exceptions.
5. **Smooth the Resource Manager path and document bridges.** Use readable
   labels in routine UI, preserve resource IDs in canonical details, keep
   app-scoped health/logs/traces/monitoring/activity/relationships coherent,
   and update provider READMEs, sample READMEs, proposal status, and feature
   docs where behavior is intentionally incomplete. Do not broaden shell
   composition or add new global workspaces.

Verify each tie-off slice with targeted tests, relevant sample smoke coverage
when practical, and `git diff --check`. Docker-dependent smoke tests remain
conditional on a healthy local Docker daemon.

### Immediate Execution Alignment

Treat this as an active queue plus an ordered backlog. The active queue is the
first five items. Items after that remain ordered, but they should not displace
the active queue unless a supported sample, security issue, or broken
cross-platform guardrail makes them urgent. For MVP, implement only the slice
listed here before pulling in broader proposal work.

1. MVP convergence and Resource Manager reliability: keep supported samples
   green, keep Application Topology as the broad local-development proof, and
   tighten generated resource details, action availability, explicit not-found
   states, read-only behavior, activity records, diagnostics, and state
   transitions around flows that already work.
2. Resource relationship comprehension: add the focused dependency/dependent
   graph and relationship summary needed to understand the current resource in
   Application Topology. The first graph, related resource actions, and related
   health/readiness summaries now exist, so remaining MVP work should polish
   this only where the app workflow exposes unclear labels, empty states,
   navigation, or missing diagnostic correlation. This remains an app-centric
   Resource Manager feature, not the later external resource graph
   import/code-generation proposal.
3. Application environment management path: make container applications,
   app-owned exposure, application-level service discovery, virtual networks,
   public endpoints, load-balancer routes, and logical DNS/name mappings form
   one understandable Resource Manager workflow. Keep `cloudshell.service`
   optional for logical facades, imported services, non-application targets,
   and advanced routing instead of making it a required MVP hop. Exact local
   development name publishing and name-mapping update UI are now in place;
   wildcard suffixes and public DNS propagation stay provider-specific and out
   of the MVP path unless a sample proves they are required. Do not expand
   `cloudshell.service` semantics further until the shared deployment and
   orchestrator service model is clearer. Container app usefulness depends on
   a thin deployment/revision/runtime-owned resource foundation: keep this
   foundation internal and diagnostic first, and do not pull in broad rollout
   history, restore, revision-management views, or traffic-splitting work for
   the MVP. Container apps default to single-instance mode; replicas are an
   explicit Application > Scale and replicas tab or programmatic
   `WithReplicas(...)` opt-in. Endpoint-bearing apps now prompt from Scale and
   replicas to create a load-balancer route when replicas are enabled; deeper
   app-owned ingress provider diagnostics remain separate hardening work.
4. Resource Manager convergence for the same path: keep the app resource page
   as the operator entry point for endpoints, discovery, storage, identity,
   logs, traces, activity, and inbound name mappings. Fix UI consistency and
   generated details when they block understanding of those resources. Add
   resource-scoped Events under the resource Management menu for resource
   history. Resource-scoped Logs and Traces now render inline under the
   resource Telemetry menu group when matching signals exist so users can
   inspect application/runtime telemetry without losing resource context.
   `telemetry:metrics` is now the standard predefined resource view ID for
   provider-owned application/runtime Metrics tabs. The first metrics slice
   adds in-memory metric points, Control Plane list/ingest APIs, remote-client
   support, shared and inline Metrics views, Project Reference request
   count/duration ingestion, and appsettings-configured resource metric panels
   for live indicators and retained recent-history line charts. Traces and
   telemetry metric points can now opt into database-backed history with
   per-resource retention limits through appsettings; Control Plane
   aggregation, OpenTelemetry metrics ingestion, and provider implementations
   remain separate work. Application provider source logs are memory-only by
   default and can opt into bounded plain-file persistence with optional
   per-day splitting; resource event log retention remains a separate design.
   Keep resource monitoring and usage in context without mixing them into
   application Telemetry. Provider-observed resource metrics belong under the
   resource Management group; retained usage belongs in the Usage workspace and
   resource Usage tabs. Use [Resource Monitoring and Usage](monitoring-and-usage.md)
   for the landed model. The next roadmap action is better provider coverage,
   richer replica diagnostics, summary-first usage dashboards, drill-down by
   resource or metric, and better trend quality. Live telemetry and monitoring
   subscriptions for split-hosted UIs remain a later Control Plane API design
   question. Keep shared Telemetry pages for cross-resource investigation
   instead of forcing normal per-resource work through global views. Do not
   start broad new shell areas before the supported samples are stable. The
   recent shell-composition migration provides useful infrastructure, but new
   composition/router work should wait unless it is needed to stabilize the
   current Resource Manager or Settings surfaces.
   Keep the current quick-create path as a compact shortcut flow, but plan for
   a Resource Manager resource gallery to become the default Add Resource entry
   point, with future wizard-based guided setup for resource types that need a
   multi-step UX.
5. Start/update and provider-readiness diagnostics: prioritize diagnostics
   that explain why already-supported local actions will fail, including host
   resolution, missing or unavailable credentials, route and port conflicts,
   unsupported storage media, unsafe replica-volume combinations, unresolved
   references, missing identity grants, and DNS/name provider gaps.
   Where these diagnostics touch host-local execution, shape the provider
   boundary so the same operation can later run from an agent-side assignment
   handler instead of baking direct Control Plane execution deeper into the
   product.
6. Configuration, secrets, and identity polish: keep the app runtime story
   clear when settings, secrets, identities, and grants affect whether the app
   starts or can call backing services. Keep identity opt-in for early
   modeling, keep Access control principal-shaped, and close only the built-in
   access gaps needed by those flows. Do not keep cycling on editor polish
   once the app-centric experience is understandable and values are not leaked.
7. Persisted-state handoff: keep `Persist()` focused on resource and
   provider-owned configuration persistence, keep deployment separate, and make
   transient startup declarations visible enough that developers understand
   which UI changes are process-scoped. Launcher/profile split scenarios also
   need a host configuration and data-root handoff so a development launcher
   can place CloudShell databases, data files, and provider-owned local state
   next to the launcher project instead of implicitly resolving everything from
   the built-in host profile content root. Track file-based C# launcher support
   as a future extension of the same convention: a single C# launcher source
   file should be able to declare the graph, read an adjacent
   `appsettings.json` as delegated host configuration, and default to the
   known local development host without requiring a project-backed AppHost.
8. Stateful application foundation: continue the storage and volume-mapping
   path now that `cloudshell.storage`, `cloudshell.volume`, `AddVolume(...)`,
   and container app volume mounts exist. Runtime materialization diagnostics
   now project from consuming applications and can be summarized from volume
   views; next slices should continue the temporary Local Storage provider as
   a bridge toward
   capability-based storage resources where provider-owned storage locations
   can contain provider-defined volume sub-items. Storage is the resource
   class, Local Storage is the first concrete storage kind, and `FileSystem` is
   the medium it announces. The first host-level negotiation uses the
   `storage.mount.filesystem` container-host capability. Local Storage
   overview pages now warn when consumers of owned volumes report partial,
   inactive, or unobserved mount materialization, and Local Storage resources
   now project provider-backed filesystem availability through runtime status
   attributes. Next slices should broaden provider-backed storage reporting
   beyond Local Storage root availability.
9. Identity validation beyond the built-in provider: keep the built-in
   identity provider for local development, but prove the same resource
   identity and permission model against one third-party OIDC/OAuth provider,
   and keep the Keycloak-backed workload smoke path green before adding broader
   IAM UI.
10. Lifecycle traceability and audit: harden the common lifecycle procedure,
   dependency-start activity, resource-event filtering, and the first audit
   schemas for MVP operations.
11. Host and runtime foundation: complete the shared host diagnostics needed by
   container apps, Docker Compose, and provider-owned runtime infrastructure,
   building on the Resource Manager shutdown path that now stops host-scoped
   workloads while leaving detached workloads recoverable.
12. Network and routing hardening: tighten host-readiness, provider selection,
   route conflicts, endpoint conflicts, configuration preview, and backend
   diagnostics for the supported samples.
13. Remote Docker host completion: finish concrete remote-host registration and
   credentials if it validates the host model, but do not let it block the
   local/default container-host MVP path.
14. Runtime-managed resources and deployment model: keep the implemented
   source, management, visibility, owner, cleanup, and internal deployment
   contracts aligned with
   [Provider-created and runtime-managed resources](runtime-managed-resources.md)
   and [Orchestration and Deployments](orchestration-and-deployments.md). Next
   slices should enrich runtime children only where container apps need
   provider-observed container IDs, health, placement, or materialization
   diagnostics, not as a broad public deployment product surface.
   Operational views should remain containment-aware: the stable resource is
   the default row or page, while hidden runtime children such as container app
   replicas appear as expandable details, log sources, telemetry scopes, or
   metric dimensions. The same pattern should be reusable for future
   containment resources such as `cloudshell.service` if they project runtime
   children.
   The first shared application-resource provider seam is public, allowing
   custom application-like providers to reuse common projection, declaration,
   template, lifecycle, descriptor, and action-availability forwarding through
   `ApplicationResourceTypeProvider`. The built-in application providers now
   consume split provider-facing contracts for definitions, procedures,
   templates, declarations, descriptors, projection, monitoring, settings,
   action availability, container-app behavior, and SQL Server permission
   status. The remaining `ApplicationResourceRuntimeOperations` facade is the
   runtime procedure coordinator for actions and container-app orchestration
   hooks, not the shared application resource abstraction. Custom providers can declare
   `ApplicationResourceDefinition` instances with their own provider ID through
   the shared application resource declaration helper and reuse the common
   builder defaults. Future slices should continue moving concrete behavior
   behind dedicated implementations rather than expanding the facade.
   Application setup/update now materializes provider-owned
   `ApplicationResourceDefinition` metadata through
   `ApplicationResourceDefinitionRegistrationService`, while resource identity,
   grouping, and dependency synchronization still flow through the normal
   Resource Manager registration model used by every resource. Definition
   normalization is isolated in `ApplicationResourceDefinitionNormalizer`, with
   provider/type-specific defaults contributed through
   `IApplicationResourceDefinitionNormalizationRule` implementations. Future
   slices should continue tightening the built-in rules into provider-owned
   services where they need runtime dependencies or provider-private behavior.
   The destination is a composable application-resource toolkit: external
   providers should be able to declare their own stable resource type, choose a
   local executable, ad-hoc container, or Resource Manager-managed sub-resource
   runtime shape, and opt into default lifecycle, endpoint, log, telemetry,
   health, liveness, configuration, storage, and cleanup behavior.
   Next slices should extract role-specific provider-facing services and
   adapters for runtime execution, environment resolution, container
   materialization, log projection, monitoring, endpoint/volume helpers, and
   host-scoped cleanup only where extension authors need stable reuse.
   Treat application runtime
   materialization as broader than projection: an application resource may own
   local processes, ad-hoc containers, and Resource Manager-managed
   sub-resources, while projecting only the artifacts whose resource identity
   has operational value.
15. Advanced app and environment concepts: defer autoscaling, backend pools,
   traffic splitting, provider-backed network-level service discovery,
   provider-backed DNS propagation, external deployment projection,
   formal resource definition pipeline integration, external-format resource
   graph import and code generation, IoT device provisioning, edge/device
   resource management, container application environments, and the initial
   on-premise hosting scenario. Cross-language local-development work remains
   active only where it hardens the shared ResourceDefinition, installed CLI,
   default local-development host daemon, launcher, and SDK boundary. Further
   Resource model package experiments should feed back into the active
   ResourceDefinition apply/export and provider seams without reopening a
   separate proof-of-concept track.

### Now: MVP Loose-End Tie-Off

- Treat the primary MVP management path as:
  container application -> app endpoint/discovery -> virtual network -> public
  endpoint or load balancer -> DNS/name mapping. The UI should let users see
  and operate that path from the application resource configuration experience
  without requiring programmatic-only sample knowledge.
- The Environment page now includes an interactive environment state map that
  projects resources, orchestrator service boundaries, active replica groups,
  load-balancer routes, name mappings, and routing bindings from current
  Resource Manager state. Treat those services, replica groups, replicas, and
  routing bindings as
  [environment artifacts](terminology.md#environment-artifact) in the
  [Runtime model](terminology.md#runtime-model), not merely as deployment
  internals. Follow-up work should feed it richer
  controller state as service routing rebinding becomes reactive.
  Next refinement slices should keep the service boundary as the primary
  visual unit, render managed workload resources such as container apps as
  attached cards on their orchestration service group, nest replica groups and
  replica resources inside that boundary, and show load balancers only when a
  load balancer is represented by an actual CloudShell resource. As the runtime
  model becomes more complete, extend the map with live deployment and scaling
  state: revision switch-over, retained previous replica groups, readiness,
  draining, route rebinding, and failure or rollback progress.
- Treat programmatic declarations as the normal starting point for local
  distributed-app development, and `Persist()` as the handoff into
  Control Plane-owned environment state. `Persist()` is not deployment: it
  records resources and provider-owned configuration, while deployment to a
  target must remain a separate orchestrator deployment API concern. An
  on-premise CloudShell environment is one deployment target: a standalone
  CloudShell cloud environment, potentially for shared hosting, similar in role
  to future Azure or AWS targets. Whether deployment is triggered by CLI,
  Resource Manager UI, or another automation surface is a later product
  decision. The orchestrator deployment API is not ready yet, so near-term MVP
  work should keep the boundary explicit and avoid overloading persistence with
  deployment behavior.
- Optimize for the experience a developer follows repeatedly: inspect the app,
  understand dependencies and exposure, start or restart it, diagnose why it
  cannot start, inspect runtime signals, and decide whether the graph is ready
  to persist. Make that loop solid before widening scope. Improvements outside
  that loop should wait unless they are security fixes or unblock the supported
  sample proof.
- Keep CloudShell's resource addressing layers distinct. Concrete endpoint
  addresses belong to endpoint network mappings; resource endpoints remain
  address-less contracts. Topology-scoped reachability, Aspire-compatible
  developer service discovery, future managed network-level discovery, and
  DNS/name mappings each solve a different part of the application environment
  path. Application overview pages now show projected developer service
  discovery references and aliases for local/programmatic flows; network-level
  discovery remains a later provider capability for host or virtual-network
  scopes.
- Do not require `cloudshell.service` for normal container app exposure in the
  MVP. Container apps are the stable user-facing deployment, replica, and
  exposure artifacts: they represent managed services that can be exposed
  internally on the host or a virtual network, through public endpoints, and
  through internal DNS-style names or custom domain mappings. Keep
  `cloudshell.service` optional/deferred for logical facades, imported
  provider-native services, non-application targets, stable discovery names
  independent of one app lifecycle, and advanced routing.
  Kubernetes Service and similar provider-native objects are provider
  materialization details unless explicitly imported or projected. This is a
  model-layer distinction: a future orchestrator may intentionally materialize
  an explicitly modeled `cloudshell.service` as its provider-native service
  primitive when the Service resource represents the service unit. Further
  `cloudshell.service` behavior should wait for the deployment/orchestrator
  model instead of leading the MVP implementation.
- Keep container host placement visible and editable on the application
  resource. Container app and SQL Server create/update flows now use the same
  selected-or-default host path, so users can keep deployment placement on the
  managed-service configuration surface instead of editing provider runtime
  details. Application overview pages also resolve the selected/default host
  and show host status, kind, endpoint, registry, credential availability, and
  advertised capabilities.
- Bring DNS/name mapping forward as a minimal logical projection and Resource
  Manager experience. MVP does not require CloudShell to publish real public
  DNS records, run an authoritative DNS server, or implement a provider-backed
  service registry, but users should be able to model names, see what endpoint
  they refer to, and understand whether a provider can materialize them. The
  first logical slice projects programmatically declared DNS zones and name
  mappings as ordinary resources and shows inbound mappings on application
  overview pages for internal names and custom domain names. Initial logical
  conflict status and generated overview diagnostics exist for duplicate names
  in the same zone/scope, and logical-only mappings now warn that no provider
  will publish DNS records for them. The Load Balancer sample now declares
  local DNS-style names that target the load-balancer frontend. Generated
  diagnostics also warn when a selected DNS publisher resource is missing or
  lacks the publisher capability, and DNS zones/name mappings now have
  inspectable Resource Manager type registrations. Resource Manager can now
  create a DNS Zone with an optional initial name mapping, and add standalone
  name mappings to existing zones. Name mappings are directly registered
  platform children so they can be deleted through the normal Resource Manager
  delete flow while updating the parent zone. DNS zones with provider intent
  now expose a permissioned `reconcileNameMappings` action through the initial
  `INamePublishingProvider` contract, with availability reasons for missing
  activated publisher implementations. A concrete local development publisher
  now handles exact host mappings under an explicit suffix through
  `local-hostnames`. Resource Manager create flows can choose that publisher
  and warn about `.local` suffixes. Name mappings now project the last
  provider reconcile observation as `Published` or `PublishFailed` so users can
  distinguish selected-provider intent from a recent publish result. Existing
  name mappings can now be edited from Resource Manager without changing the
  parent DNS zone, and DNS zone overview pages list owned mappings with target
  and materialization status. Target resource overviews also show inbound
  name-mapping materialization. Container-backed application overview pages
  now provide an app-centric "Add name mapping" action that opens a prefilled
  Resource Manager create flow for the target app and endpoint. Container
  app overviews also provide a first app-centric "Add load-balancer route"
  action that opens a prefilled load-balancer create flow for the target app
  endpoint. Route-shape validation, duplicate route-match validation, duplicate
  entrypoint validation, host-port conflict validation, and missing
  target/endpoint action-capability reasons are now in place. Resource Manager
  can add or edit routes on an existing load balancer from its Configuration
  tab, and application endpoint shortcuts now prefer the existing load balancer
  in the same group when there is exactly one candidate. Provider-selected
  name mappings now show a pending-publish diagnostic before the first
  reconcile observation. Next it needs richer provider-specific DNS runtime
  diagnostics when the MVP management flow needs them.
- Keep public endpoint exposure explicit. A resource can expose an endpoint
  directly, through app-owned ingress, through a virtual-network mapping,
  through a load-balancer route, or through an optional service facade when
  that facade is deliberately modeled; Resource Manager should show that
  relationship from both the target resource and the exposure resource.
- Treat storage as part of the same app-environment path, not as deployment
  trivia. The MVP now has basic volume resources and application volume
  attachments with a dedicated resource Storage tab plus a direct volume
  create/configuration/overview flow, Local Storage resources,
  Docker/Docker Compose runtime materialization for container `FileSystem`
  mounts, and local process filesystem materialization for executable and
  ASP.NET Core project mounts.
  Resource Manager volume selectors now only offer mountable volume resources
  and show their storage medium, and Start/Restart action availability now
  preflights unsupported volume and storage-parent media for the current
  resource materializers. Application overview pages show attached storage
  mounts, and volume overview pages show reverse consumers with declared target
  path and access mode when workload descriptors are available. Docker-backed
  container hosts now advertise `storage.mount.filesystem`, and application
  Start/Restart availability checks the selected host for that capability when
  managed `FileSystem` volumes are attached; next it needs Resource Manager
  visibility into whether a mapping was actually materialized. The local Docker
  runner now records runtime-observed mount materialization facts after a
  successful container start, application overview pages show source/target
  materialization status per mount, and volume overview pages show aggregate
  materialization status for consumers through projected resource attributes.
  Docker Compose now reports the same observations through the shared
  `IResourceVolumeMountMaterializationStore` contract after successful
  Start/Restart/Stop actions. Resource Manager generated diagnostics now warn
  when standard mount materialization attributes report partial, not-active, or
  unknown status. Local Storage overview pages now show owned volumes with
  consumer counts and consumer-reported mount materialization summaries, and
  the Local Storage overview warns when consumers of owned volumes report
  incomplete or unobserved mount materialization. Local Storage resources now
  project provider-backed filesystem root availability through
  `storage.runtimeStatus` and `storage.runtimeStatusReason`, and Resource
  Manager warns when an explicit local storage root is unavailable.
  Provider-backed storage usage metrics and richer provider-specific Resource
  Manager diagnostics remain next.
- Identity remains a product differentiator. The built-in ASP.NET Core
  provider is the reference implementation for simple local development:
  resource identity clients, scoped resource-permission tokens, provisioning
  status, and provider-neutral principal lookup should all work there first.
  The first Keycloak sample validates external OIDC sign-in, CloudShell role
  claim mapping, sample-scoped resource identity provisioning, and a provider
  setup/reconcile hook with a Control Plane endpoint and a Resource Manager
  action on the provider's provisioning resource. Provider-specific runtime
  credential injection now supplies provisioned Keycloak credentials to
  workloads through the standard `CLOUDSHELL_IDENTITY_*` contract, and
  protected CloudShell services can validate configured external OIDC/OAuth
  bearer tokens before applying CloudShell scoped resource-permission claims.
  The Third-party Identity sample now includes automated smoke coverage for a
  Keycloak-provisioned workload that reads configuration with a provisioned
  resource identity. The generated Identity tab now shows provisioning status
  and status diagnostics for identity-bound resources, while Access control
  gets assignable principals through the Control Plane principal lookup API.
  Next identity work should keep that path stable while improving provider
  readiness and authorization diagnostics that directly affect MVP flows.
- Keep the baseline samples building and smoke-testing as the release gate:
  combined hosting, split hosting, container host, settings and secrets, host
  virtual networking, load balancer, project references, third-party identity,
  application topology, and container app deployment.
- Treat the Resource Graph path as the default path for the supported
  switch-readiness samples. `GraphOnly=false` remains useful as old-provider
  comparison coverage while the remaining provider seams are stabilized, but
  MVP sample confidence should come from the graph-backed defaults.
- Start controlled provider switch-over from those graph-default samples once
  their main workflow runs and documented non-parity is acceptable. Full
  old-provider parity is not required before integration starts; missing UI
  pages, richer diagnostics, revision history, and final runtime placement
  should stay visible in provider READMEs or the Resource Graph proposal and
  be cleaned up after the switch exposes the real seams.
- Use the forked Application Topology sample as the broad MVP composition
  sample. ProjectReference remains the focused ASP.NET Core project dependency,
  service discovery, log, and trace baseline; ApplicationTopology is where SQL
  Server with mounted storage, configuration, secrets, identity, structured
  logs, traces, container apps, and networking should converge as those
  primitives stabilize. The first ApplicationTopology SQL/storage slice is in
  place: Local Storage, a storage-owned SQL data volume, and a provider-owned
  local SQL Server Docker runtime are declared, with the backend API resolving
  SQL Server through CloudShell service discovery and exposing a `/database`
  check that the frontend calls through the API. A later SQL/database identity slice
  should let application resources use CloudShell resource identity for
  database authentication in an Azure-like flow.
- Treat the Settings and Secrets sample as the current proof of the developer
  service-integration flow: a resource can model settings and secrets first,
  then opt into identity and resource-scoped grants when access enforcement is
  needed.
- Treat settings and secrets UI as supporting evidence for the app experience,
  not as the center of the MVP. The important UI outcome is that the app page
  explains what references exist, whether referenced resources and grants are
  available, and why startup or service calls will fail. Assignment/editing
  views should stay functional and safe, but deeper editor ergonomics can wait.
- Make Resource Manager generated details predictable for common resources:
  Overview first, resource-specific tabs next, Management tabs for resource
  concerns such as Environment, Identity, Monitoring, and Activity near the
  bottom.
- Investigate standardized selector components before adding more one-off
  picker UI. Resource selectors, principal selectors, volume/storage selectors,
  host selectors, and future hierarchical selectors should share search, label
  formatting, empty states, permission/read-only handling, validation, and
  typed filtering where the behavior is common.
- Keep resource-scoped operations in context. Events should remain under the
  resource Management menu as resource-management history. Logs and Traces
  render inline from a resource-detail Telemetry menu group when matching
  application/runtime signals are available. Resource Monitoring remains a
  provider-supported Management tab for provider-observed resource metrics;
  Usage remains retained sample history and trend reporting. See
  [Resource Monitoring and Usage](monitoring-and-usage.md) for the landed
  model. Cross-resource trace exploration can keep a shared Telemetry area
  with resource-aware links back into the relevant resource detail views.
- Keep resource-scoped Health focused on the current resource. The common
  Health workspace can later evolve toward a status-page-style system summary
  with timeline rows for services, capabilities, or other health scopes, but
  that requires a grouping model beyond ordinary resource groups. Do not infer
  environment-wide degradation percentages from resource groups for MVP; first
  define explicit health scopes, which checks/resources contribute to each
  scope, and how a degraded segment drills down to the affected resources and
  checks. Curated incident comments, public subscriptions, and operator-owned
  annotations should remain later incident-management work.
- Keep resource identity clear when display names are enabled: Resource ID
  should appear first in details and overview identity surfaces, while create
  flows ask for the scoped resource name. Resource Manager now has a
  display-name preference and projected resources now carry explicit
  `DisplayName`, but UI create and update flows should not edit display names
  for the MVP.
- Keep lifecycle actions and resource activity consistent: `Start` is the
  canonical action, every lifecycle action records the requested action and
  resulting events, and dependencies started by orchestration get their own
  activity records.
- Prefer action capability reasons, resource diagnostics, and stable
  ProblemDetails codes over provider-specific exception text.
- Do not expand broad IAM, workflow automation, runtime-managed resource, or
  deployment-history scope unless the missing piece blocks the supported MVP
  samples.

The sections below preserve ordered backlog detail for areas that frequently
touch MVP work. They are not the active queue by themselves; pull from them
only when the active MVP tie-off queue or a supported sample exposes the need.

### Backlog Detail: Configuration, Secrets, and Identity Polish

- Keep [Resource identity and permissions](resource-identity-and-permissions.md)
  as the current-state feature documentation and
  [Identity and access](proposals/core/identity-and-access.md) as the open-work
  tracker.
- Keep configuration, secrets, and identity work tied to the local developer's
  app loop: understanding references, startup readiness, service-call
  authorization, and clear diagnostics. Avoid spending additional MVP cycles on
  secondary Environment-tab ergonomics unless they block that loop.
- Finish only the Resource Manager assignment pieces needed for the main app
  flow: literal settings, configuration-setting references, and vault-backed
  secret references should remain safe to inspect and apply on resources that
  advertise environment-variable support.
- Show saved references and diagnostics without displaying resolved secret
  values. Application overview now renders app-setting and environment-variable
  references as source labels and target references instead of resolved values
  or raw CloudShell reference strings, shows basic target availability and
  identity-grant status, and masks credential-like literal values.
- Verify assignment flows against identity-backed configuration and secret read
  authorization. Runtime resolution failures now use typed diagnostics and
  project as resource-action-unavailable API errors instead of generic
  operation failures. Resource action capabilities now preflight safe reference
  checks for missing referenced resources and identity grants before
  orchestration dispatch.
- Treat Configuration Store and Secrets Vault endpoints as normal service
  endpoints: use the current explicit configuration or Aspire-like service
  discovery path now, and move toward network-level service discovery when
  that resource capability lands. See [Service discovery](service-discovery.md)
  for the current Microsoft service discovery package requirements. Do not
  special-case these endpoints as part of the resource identity credential
  contract.
- Continue authorization diagnostics where they directly support MVP flows,
  especially configuration updates, secret reads, resource actions, logs, and
  diagnostics.
- Later UI enforcement should disable or hide Resource Manager operations
  based on the current user's permissions, while still explaining the missing
  permission in the same diagnostic style as Azure-style portals.
- Provisioning-resource authorization boundaries now have focused coverage:
  provisioning requires permission on the provisioning resource and manage
  permission on the target resource, while status reads require read
  permission on both the target and provisioning resource.
- Defer broad IAM work unless it blocks the built-in identity MVP: resource
  group or parent-resource identity inheritance, multiple identities per
  resource, effective permission APIs, and durable external authority
  reconciliation. Keep provider-native requested-versus-effective grant status
  narrow when it is needed to prevent MVP flows from implying enforcement that
  does not exist yet, such as SQL Server database grants.
- For SQL Server identity-backed access, follow the Azure-style boundary:
  the identity broker or authority provides an authentication artifact, the
  SQL Server provider materializes SQL-side users, roles, or external mappings,
  and workloads opt into that path without receiving administrator credentials.
- Keep Microsoft Entra ID compatibility as a required contract target, but do
  not block MVP on a full Entra provider if the provider-neutral contract and
  compatibility tests are clear.

### Backlog Detail: Lifecycle, Traceability, and Audit

- Expose transient lifecycle state such as `Starting` while start/restart
  operations are in progress. Application resources now project a fresh
  provider-owned starting observation and fall back to stopped when that
  observation becomes stale.
- Persist resource events and expose filtering by event type, actor, and time
  range. The initial persistence/query slice is in place through
  `IResourceEventManager`, and Resource Manager now has a generated Activity
  tab for resource events with filters and action/event grouping. Resource
  events now carry optional W3C trace/span context so activity can be tied back
  to distributed traces during development, with trace and span filters
  available through the Control Plane API/client and Resource Manager
  related-activity links. Denied resource actions now record warning
  failed-action activity entries with trigger metadata before the access-denied
  response. Next work is event schema polish and broader integration with
  authorization/audit decisions.
- Use [Lifecycle orchestration](proposals/core/lifecycle-orchestration.md) to
  keep dependency startup, lifecycle action execution, resource activity, and
  future event-triggered automation on one common orchestration model.
- Use [Resource recovery](proposals/core/resource-recovery.md) to track
  liveness-signal-driven restart policy, exponential backoff, separate
  liveness and recovery capabilities, structured liveness outcomes, provider
  probe evaluators, per-check polling intervals, generated Recovery UI, future
  operational notifications, dashboard degradation summaries, and
  resource-builder recovery declarations while keeping the Health UI wording
  and normal lifecycle action path.
- Use [Logging infrastructure](proposals/core/logging-infrastructure.md) to
  track structured logging, non-text operational payloads, resource events,
  audit records, diagnostics, metrics, and traces without prematurely merging
  those concerns. Provider log entries now support optional structured
  metadata using familiar logging and OpenTelemetry terms while preserving
  plain text stdout/stderr compatibility.
- Use the Project Reference sample as the current distributed tracing proving
  ground. It runs two ASP.NET Core project resources with OpenTelemetry,
  service discovery, frontend-to-API calls, and CloudShell trace ingestion.
  The UI goal is a Zipkin-style trace experience where users can inspect spans
  across services in a compact clickable waterfall while CloudShell keeps
  resource activity, logs, traces, and future metrics as distinct observability
  signals. Resource detail pages should expose trace entry points through the
  resource Telemetry menu when trace data exists, while shared Telemetry trace
  pages remain the cross-resource investigation surface. The trace-detail
  target has a service legend, span details panel, and links from spans to
  related logs, activity entries, and Resource Manager details. Provider-owned
  resource Monitoring is separate from this application telemetry model and
  belongs under the resource Management group when a provider contributes
  process/container resource metrics such as CPU and memory usage. Providers
  should use the standard `management:monitoring` predefined view ID for those
  Monitoring tabs.
- Keep the observability taxonomy explicit: Telemetry Events and Telemetry
  Metrics are application/runtime signals; Resource Events and Resource Metrics
  are management/provider-observed signals. Resource Events belong under
  Management as resource history, Resource Metrics belong in Monitoring under
  Management, and application telemetry belongs under Telemetry. Providers
  should use `telemetry:metrics` for application/runtime metrics tabs and
  `management:monitoring` for process/container resource metrics tabs.
  Telemetry for multi-instance resources should remain scoped to the stable
  managed resource by default. Resource Telemetry views should show no scope
  selector for one observed telemetry scope, then use an `All instances`
  default plus provider-defined scope options when multiple scopes exist. Logs,
  traces, and telemetry metrics need stable scope dimensions before Resource
  Manager can implement that filtering consistently. Logging needs an explicit
  `ResourceLogSource` declaration in the resource model and a projected
  `LogSource` Control Plane abstraction before adding richer log query,
  streaming, parser, and renderer behavior.
  CloudShell now retains application/runtime telemetry metric points in memory
  for the MVP and can query provider-backed current resource monitoring
  snapshots for Docker containers, local application processes, single-instance
  container-backed applications with a resolvable static/default container
  host, and configuration/secrets service processes. Container app resource
  monitoring now has a custom Management > Monitoring dashboard that aggregates
  app-level usage and breaks metrics down by runtime replica/container when the
  application provider can observe materialized replicas.
  Separate telemetry storage/query backends from the Resource Manager UI before
  expanding retention. The in-memory CloudShell store is the MVP local backend,
  but Trace and Metric views should consume provider-neutral manager/query
  contracts so CloudShell can use standards-based backends such as Prometheus
  for metrics, OpenTelemetry Collector pipelines, or trace stores without
  changing resource Telemetry navigation or scope selection.
  Control Plane API streaming for live telemetry/monitoring remains a later
  design question after basic provider monitoring support is established;
  durable retention and aggregation remain future work. Merged log views now
  support UI-level source filters over projected `LogSource` records for reads
  and older-history queries; live merged streaming should wait for log sessions
  to support provider-owned multi-source fan-in, shared readers, and disposal
  semantics instead of faking it in the UI. Use
  [Service telemetry and degradation](proposals/core/service-observability-and-degradation.md)
  to track the service-first local-development experience that correlates
  load, recent exceptions, structured logs, traces, health, resource
  monitoring, replica scopes, and host/provider capacity context through
  common CloudShell views without replacing Prometheus, Grafana, or other
  specialized observability systems.
- Define only the audit event schemas needed by current MVP operations:
  resource actions, host/runtime operations, image deployments, authorization
  decisions, identity provisioning, configuration reads, and secret reads.

### Backlog Detail: Host and Runtime Foundation

- Add host-oriented descriptors, provider contracts, host registration, and
  builder/settings names. These host-oriented names are now in place for
  declarations, samples, Resource Manager settings, Docker host descriptors,
  and resolver-backed orchestration.
- Implement a shared `IContainerHostResolver` over explicit resource
  descriptors, default host providers, and registered default host descriptors.
  This is in place for Control Plane container-workload validation.
- Migrate Docker Compose materialization to the resolver while preserving
  samples and declarations on the current host-selection model. Docker Compose
  now requires the shared resolver instead of duplicating host lookup.
- Return diagnostics and action capability reasons for host placement failures.
  Missing explicit/default container hosts now disable affected Start/Restart
  actions before orchestration dispatch. The shared resolver also reports
  unavailable host resources and missing required host capabilities, with
  container-image and container-build capability IDs wired into container
  workload validation. These failures now carry structured reason codes for
  API/UI consumers. Host descriptors now carry non-secret credential readiness
  and Docker reports missing configured credential inputs through the same
  resolver diagnostics path.
- Resolver tests now cover explicit host selection, preferred host selection,
  configured default host selection, registered default host descriptors, and
  missing-host, unavailable-host, required-capability diagnostics, and their
  structured reason codes. Resolver tests also cover unavailable host
  credentials.
- Add provider-owned Docker runtime support for owner-scoped implementation
  containers after the resolver lands.
- Continue Traefik container mode beyond apply-time startup. Load-balancer
  resources now expose provider-owned Start/Stop lifecycle when the selected
  provider manages a runtime container, persist the runtime state, and ask the
  provider to clean runtime state during Delete. Remaining work is probe,
  shutdown cleanup, and richer diagnostics on the selected host resource.
- Extend app-owned ingress infrastructure with stop/delete lifecycle
  projection, provider-owned status, and diagnostics for replicated HTTP/TCP
  endpoints.
- Define host/runtime recovery policy separately from host restart cleanup:
  detached container apps should be rediscovered through container host and
  stable workload identity, while crash restart/backoff behavior should be an
  orchestrator or resource recovery policy instead of a side effect of
  runtime-state recovery.

### Backlog Detail: Remote Docker Host Completion

- Continue the remote Docker hosts proposal on top of the shared host model:
  persist provider-owned UI host configuration, wire supported credential
  transports into Docker client creation, and keep credentials out of
  projected attributes, endpoints, logs, and diagnostics.
- Complete duplicate-host validation across local and remote Docker
  registration paths, including compatibility coverage for existing
  `docker.engine` registrations and stable `docker.host` UI/API projection.
- Verify remote-host container discovery, actions, and diagnostics end to end
  against a testable Docker endpoint with credential redaction coverage.
- Keep this path behind local/default container-host stability. Remote Docker
  proves the host abstraction, but the MVP should still be useful with the
  local Docker host and programmatic declarations.

### Backlog Detail: Network and Routing Hardening

- Harden local host-provided virtual networking by deciding how reconciled
  mappings should be persisted or stopped. Reconcile actions now report
  endpoint-mapping validation and missing provisioner reasons through action
  capability evaluation before execution, and the portable
  `networking:host-local` provider has direct Control Plane coverage for
  forwarding traffic through a real local proxy on macOS, Linux, and Windows.
  Local host-name reconciliation writes a managed hosts-file block and now
  attempts a best-effort resolver cache refresh for system hosts-file targets;
  custom hosts-file targets skip refresh for safe inspection and tests.
- Generalize host-provided virtual networking as a provider model beyond the
  portable local proxy baseline: Linux, Windows, macOS, and runtime-specific
  hosts should advertise native capabilities and diagnostics through the same
  provider boundary instead of leaking OS assumptions into Resource Manager.
- Make app-owned exposure first-class in the networking UI flow: create and
  inspect application endpoints, expose ports, connect them to virtual
  networks, load-balancer routes, and name mappings, and show inbound/outbound
  discovery relationships. Keep `cloudshell.service` as an optional facade or
  imported-provider concept until a concrete scenario needs it.
- Continue the endpoint reservation/preflight contract: Resource Manager now
  tracks CloudShell-owned host/port assignments and runs advisory local
  host-port availability checks for platform-owned network, service, and
  load-balancer endpoints. Next, host/runtime providers should report richer
  final bind failures and owning process/container diagnostics for dangling
  external processes or containers where they can observe that safely.
- Add a guarded force-release recovery operation for stale host-scoped runtime
  ownership, such as orphaned containers, processes, port reservations, or
  provider runtime claims that block dependency startup after a previous host
  exit. The operation should explain the resource and provider-owned runtime
  ownership being released, require explicit permissions in team-owned and
  on-premise environments, and prefer provider-supported cleanup before falling
  back to releasing CloudShell's stale ownership record.
- Expand host-readiness warnings beyond the current generated endpoint-mapping
  diagnostics, which now name missing provider resources, missing endpoint
  mapper capability, and unresolved source/target resources or endpoints. Next
  add provider-specific wording for missing gateway, load balancer, DNS,
  service mesh, firewall, or cluster network controller capability.
- Add a host and environment setup UX plan that combines global setup views
  with per-resource setup prompts. A setup view should summarize missing or
  disabled OS/runtime capabilities for the current machine or selected host,
  and it should also guide environment-level platform choices such as selecting
  and configuring an identity provider, choosing the default container host,
  selecting default networking/DNS providers, and preparing other environment
  services needed when CloudShell is used as a hosting platform rather than
  only a local developer shell. Resource pages should still prompt when a
  specific resource needs a feature that is not enabled. This is especially
  important for Windows, where container, virtualization, networking, firewall,
  DNS, and optional OS features may need explicit activation before a provider
  can materialize the requested resource.
- Finish provider-backed endpoint mapping materialization for real host
  networking services, not just logical local networking.
- Add the first DNS/name mapping resource projection and UI path for local
  development names, with provider-materialization warnings when no capable
  name publisher is active. DNS/name resources should remain lifecycle-less,
  and provider-backed DNS publication now has the initial
  `reconcileNameMappings` action and name-publishing provider contract so
  operators can force re-apply expected records. Exact local host-name
  publication under an explicit development suffix is now implemented through
  `local-hostnames`, and Resource Manager create flows expose that provider
  with `.local` suffix warnings. The last provider reconcile result now feeds
  observed name-mapping materialization attributes and publish-failure
  diagnostics. Wildcard suffixes, public DNS propagation, richer provider
  runtime diagnostics, and observed external DNS states such as unknown or
  drifted remain provider-specific follow-up work.
- Continue load balancer support beyond the first Traefik file-config provider.
  Generated Resource Manager diagnostics now cover missing selected host
  resources and missing route target resources/endpoints; next add provider
  validation diagnostics, configuration preview, route conflict checks, and
  richer host/runtime capability checks.
- Finish the provider-resource selection path so load-balancer
  `UseProvider(...)`, explicit host selection, and UI-created resources behave
  consistently.
- Extend endpoint assignment conflict diagnostics beyond platform-owned
  endpoints so provider-projected runtime endpoints can participate in a
  Resource Manager-wide validation pass.

### Later: Runtime Ownership and Deployment Model

- Keep resource templates separate from orchestrator deployments. Users and UI
  flows express desired resource state as `ResourceDefinition` entries in a
  `ResourceTemplate`; deployment planning remains an internal Resource Manager
  and provider/orchestrator concern after graph validation.
- Continue container app orchestration by introducing a provider-owned internal
  deployment planner: accepted container app graph state and current runtime
  state in, normalized `ResourceOrchestratorDeploymentDefinition` out. Image
  updates, requested replica-slot changes, and start materialization should use
  that same deployment-controller path.
- Move `ResourceOrchestratorDeploymentDefinition` toward being the
  authoritative desired runtime state for services and replica groups.
  `ResourceOrchestratorDeploymentSpec.Service` can remain a migration bridge,
  but long-term orchestration should reconcile the versioned definition.
- Same-resource deployment serialization now prevents overlapping in-process
  deployment applies for one container app from both resolving against the same
  latest active revision. Follow-up work should add persisted optimistic
  concurrency for distributed Control Plane instances.
- Track retained, draining, superseded, and deleted replica groups as explicit
  runtime state. Retained previous slots should be inspectable and later
  drainable instead of existing only as omitted tear-down targets.
- Add explicit readiness-gate policy to replica-group definitions before
  expanding rollout controls. The controller should know which materialization
  signal is required before routing rebinding, revision activation, and
  predecessor cleanup.
- Orchestrator-owned service routing binding definitions now connect a service
  endpoint, current replica group, and optional route, endpoint-mapping, or
  load-balancer resource by explicit CloudShell ids, and routing
  reconciliation providers receive the matching binding set in their procedure
  context. The Resource Model graph procedure bridge forwards that binding set
  to container-app orchestrator runtime handlers. Follow-up work should make
  the default orchestrator controller and load-balancer providers react to
  those bindings instead of inferring replica membership from labels or
  container-app-specific runtime names.
- Apply the
  [runtime-managed resource projection rules](runtime-managed-resources.md) to
  the next concrete artifact classes: implementation containers, generated
  images, endpoint registrations, backend registrations, health probes, and
  revisions.
- Define the remaining shared ownership, query, authorization, cleanup, and
  garbage-collection rules that are not covered by the current
  runtime-managed resource spec.
- Design provider-originated resource change streams so providers such as
  Docker can push discovered container/status changes into Resource Manager
  instead of relying only on UI-side inventory polling.
- Design provider-owned replication projection for resources that can
  implement replicas, keeping stable resources separate from runtime instances
  and using parent-derived naming conventions for materialized runtime
  containers.
- Preserve container app current-revision projection for app deployments; defer
  rich rollout history, restore-to-revision-state behavior, revision lineage,
  state merge from revisions, retention, and first-class deployment resources
  until runtime ownership and traceability are clear.
- Orchestrator-level based-on revision tracking now exists as foundation for
  future public restore or merge workflows: deployments default their base to
  the active/latest successful revision and revision outcomes retain
  `BasedOnRevisionId`. The remaining work is deciding how much materialized
  state must be retained to author later based-on deployments.

### Later: Advanced App and Environment Concepts

- Defer container app scaling policies beyond the current explicit
  replica-count API. A future policy can decide when to expand or shrink
  requested replicas from load, capacity, schedule, or provider signals, but it
  should remain separate from recovery policy, which reacts to liveness and
  lifecycle failure.
- Defer backend pools, provider-materialized TLS binding beyond the current
  Traefik PEM certificate path, traffic splitting, advanced service exposure,
  DNS/name mapping, external deployment projection, and container application
  environments until host, routing, identity, runtime ownership, and deployment
  decisions are stable.
- Prepare the first post-MVP on-premise hosting scenario once the MVP release
  path is stable. That scenario should combine provider-backed networking,
  virtual networks, ingress/public endpoint mapping, DNS/name mapping,
  network-level service discovery, integration points, and more complex
  validation samples.
- Resource Manager read-only mode is in place as the
  `ResourceManager:ReadOnly` UI host setting, so local-development or
  programmatic-declaration environments can be inspected without allowing UI
  create/update/delete/action flows to override the declared graph. Follow-up
  work is permission-aware UI enforcement and deciding whether any deployments
  also need Control Plane write blocking.

## Planning Notes

The MVP execution plan above is the current task queue. The notes below explain
the planning boundaries to preserve while the queue is worked.

### Keep Convergence First

The release path should prefer completing and hardening already-started flows
over starting new platform areas. The strongest proof remains a realistic
application topology managed from Resource Manager and the Control Plane API:
container apps or project-backed services, SQL Server with mounted storage,
configuration, secrets, identity-backed access, logs, traces, endpoint
exposure, load-balancer routes, and DNS/name mappings.

Resource Manager reliability is the release gate. When choosing between a new
abstraction and a sharper diagnostic, action capability reason, generated
detail, or smoke-test assertion for an existing flow, choose the existing flow
unless the missing abstraction blocks a supported sample.

Classify roadmap entries before ordering them. A small UX enhancement that
removes repeated confusion in the primary Resource Manager path can outrank a
larger backend enhancement with lower immediate MVP impact, while a backend
enhancement that unblocks several user-visible workflows can outrank isolated
polish.

### Preserve Boundaries

The current model boundaries should hold through the MVP:

- Resource endpoints are address-less contracts; endpoint network mappings
  carry concrete reachability.
- Container apps are the normal application exposure artifact;
  `cloudshell.service` remains optional for logical facades, imported services,
  non-application targets, and advanced routing.
- Storage volumes are mountable storage resources, not a generic bucket for
  object storage, databases, backups, or provider-specific persistence.
- Identity should secure resource actions, configuration reads, secret reads,
  provider setup, and workload-to-platform calls without pulling broad IAM into
  the MVP.
- Runtime-managed resources and deployment/revision contracts stay internal or
  diagnostic according to
  [Provider-created and runtime-managed resources](runtime-managed-resources.md)
  until ownership, visibility, cleanup, and traceability are stable enough for
  broader product surfaces.

### Defer Deliberately

The following work should stay out of the MVP unless a release-gating sample
forces a smaller slice:

- Autoscaling, traffic splitting, backend pools, provider-materialized TLS
  binding beyond the current Traefik PEM certificate path, rollout history,
  restore-to-revision-state behavior, state merge from revisions, and
  first-class deployment resources.
- Provider-backed network-level service discovery and public DNS propagation.
- Broad IAM features such as inheritance, multiple identities per resource,
  effective permission APIs, and durable external authority reconciliation.
  Provider-native requested-versus-effective grant models should stay deferred
  except for narrow resource-provider diagnostics that prevent misleading MVP
  experiences.
- External deployment projection, resource graph import/code generation, and
  container application environments.
- Weak references to provider-projected resources that may not yet exist in the
  graph, such as SQL databases under a SQL Server resource or runtime
  containers under a container host. The immediate dependency/dependent graph
  should visualize current projected resources and strong relationships; weak
  projected references need a later resource-model, serialized-graph, and
  specialized selector design.
- Status-page-style common Health aggregation, including health scopes beyond
  resource groups, uptime/degradation estimates, incident annotations, public
  summaries, subscriptions, and curated comments. MVP Health should stay
  resource-scoped plus a simple common list backed by retained health
  snapshots.
- The initial on-premise hosting scenario beyond the design and sample
  preparation needed to avoid dead-end MVP choices.

### Release Gate

Before treating the MVP as ready, verify:

- The supported sample smoke suite covers combined hosting, split hosting,
  container host, settings and secrets, host virtual networking, load balancer,
  project references, third-party identity, application topology, and container
  app deployment.
- Resource Manager can inspect, operate, and diagnose the main app environment
  path from the application resource page without relying on programmatic-only
  sample knowledge. Create/update flows should support that path, but the MVP
  should not be judged by deep editor completeness.
- API/client projections preserve the same domain-shaped model as Resource
  Manager, including endpoint network mappings, resource actions,
  capabilities, identity, events, and diagnostics.
- Failure states return stable action capability reasons, diagnostics, or
  ProblemDetails codes rather than provider-specific exception text.
- Sensitive values are not rendered in Resource Manager overview or editing
  paths, while references and grant/readiness status remain visible enough to
  explain app behavior.

## Tracking Work

The current task queue and milestone scope stay in this roadmap. Completed
durable decisions stay in [ADR](../ADR.md). Landed changes and verification
expectations stay in [Changelog](../CHANGELOG.md).
Proposal statuses stay in [docs/proposals](proposals/).
