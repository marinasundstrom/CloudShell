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

- [Domain model](domain-model.md)
- [Resource capabilities](capabilities.md)
- [System design guidelines](system-design-guidelines.md)
- [Programmatic resources](programmatic-resources.md)
- [Control Plane API](control-plane-api.md)
- [CloudShell and Aspire](cloudshell-and-aspire.md)
- [Container applications proposal](proposals/containers/container-applications.md)
- [Resource graph import and code generation proposal](proposals/core/resource-graph-import.md)

## Authoritative Milestones

Milestones in this file are the authoritative product scope. Proposal status
tables, progress notes, and the execution plan below should stay aligned with
these milestones instead of redefining release scope independently.

### MVP

Goal: make CloudShell useful as a combined-hosted local and team-owned control
plane while preserving the split-hosting path.

MVP scope:

| Area | Required outcome |
| --- | --- |
| Container Apps, Version 1 | Container app resources can be declared, inspected, started, stopped, updated by image/revision, configured with replicas, connected to the default container-host path, and treated as the managed-service configuration surface described in the [Container applications proposal](proposals/containers/container-applications.md). |
| Application exposure, discovery, and names | Application resources, application-level discovery, and the first logical DNS/name-mapping projection make resource-to-resource and user-to-endpoint access understandable from Resource Manager. `cloudshell.service` remains optional for logical facades, imported services, or advanced routing instead of being required for normal container app exposure. |
| Network primitives | Virtual networks, endpoint requests, endpoint network mappings, configured endpoint mappings, load-balancer routes, public endpoint exposure, and host-local networking provide enough routing to expose common container app scenarios with clear diagnostics. |
| Storage and volume mappings | Mountable volume resources and app volume attachments can be modeled, inspected, and mapped into container apps or executables through provider-neutral storage intent, without forcing object storage, databases, or backups into the same abstraction. |
| Identity, Built-in | The built-in identity provider can provision resource identities, issue scoped resource-permission tokens, and enforce those permissions for Control Plane actions, configuration reads, and secret reads. |
| Identity, external OIDC validation | The identity model is proven against at least one standards-compliant third-party OIDC/OAuth provider, such as Keycloak, without changing the CloudShell resource identity contract. |
| App settings and secrets integrations | App settings, configuration-entry references, and secret references work through programmatic declarations, Resource Manager assignment flows, runtime transfer, redaction, and authorization. |
| UX polish | Resource Manager common workflows are understandable, diagnostics are actionable, generated details are useful, and identity, configuration, secrets, networking, and app controls are discoverable without bespoke sample code. |
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

Required outcome:

| Area | Required outcome |
| --- | --- |
| On-premise host scenario | A deployable combined or split Control Plane/UI setup can manage resources against a team-owned host or host runtime, not only a developer workstation. |
| Management UI | Resource Manager can inspect and operate the environment well enough for platform operators, with permission-aware controls and a read-only mode for environments where UI writes must be disabled. |
| Cross-platform networking | Virtual networks, ingress/gateway/load-balancer providers, public endpoint mapping, and DNS/name mapping work through provider capabilities rather than OS-specific assumptions. |
| Network-level discovery | Services can be discovered through network or provider-level mechanisms such as DNS, service registries, or Eureka-like systems, separate from the Aspire-compatible app environment-variable mapping. |
| Integration points | Providers, CloudShell extensions, webhooks, WebSocket/streaming subscriptions, and API clients can react to resource events and lifecycle state without replacing the core orchestrator path. |
| Validation samples | More complex samples prove multi-resource application topology, public ingress, DNS/name mapping, service discovery, identity-backed configuration/secrets, and operator UI workflows. |

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

The MVP is now primarily a convergence problem, not a concept-discovery
problem. The core resource model, endpoint/network mapping model, container app
surface, local/default container host path, storage/volume bridge,
configuration and secrets resources, built-in identity flow, Keycloak-backed
identity validation, Resource Manager details, and supported sample smoke
coverage all exist.

The remaining MVP work should bias toward release-quality behavior:

- Keep supported samples building and smoke-testing.
- Make the primary Resource Manager path understandable without sample-specific
  knowledge.
- Tighten action capability reasons, diagnostics, and ProblemDetails responses
  around already-supported flows.
- Keep endpoint contracts address-less and require concrete reachability through
  endpoint network mappings.
- Avoid broad IAM, deployment history, autoscaling, advanced service resources,
  and on-premise hosting work unless a supported MVP sample exposes a blocking
  gap.

### Immediate Proposal Order

Work the current proposals in this order. For MVP, implement only the slice
listed here before pulling in broader proposal work.

1. Application environment management path: make container applications,
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
   history, rollback, or traffic-splitting work for the MVP. Container apps
   default to single-instance mode; replicas are an explicit Scaling tab or
   programmatic `WithReplicas(...)` opt-in. Endpoint-bearing apps now prompt
   from the Scaling tab to create a load-balancer route when replicas are
   enabled; deeper app-owned ingress provider diagnostics remain separate
   hardening work.
2. Resource Manager convergence for the same path: keep the app resource page
   as the operator entry point for endpoints, discovery, storage, identity,
   logs, traces, activity, and inbound name mappings. Fix UI consistency and
   generated details when they block understanding of those resources. Add
   resource-scoped Events under the resource Management menu for resource
   history. Resource-scoped Logs and Traces now render inline under the
   resource Telemetry menu group when matching signals exist so users can
   inspect application/runtime telemetry without losing resource context.
   Track provider-supported resource Monitoring separately under the resource
   Management group because resource monitoring is about provider-observed
   resource metrics such as process/container CPU and memory usage rather than
   application telemetry. `management:monitoring` is now the standard
   predefined resource view ID for provider-owned Monitoring tabs; resource
   metrics collection and provider implementations remain separate work. Keep
   shared Telemetry pages for cross-resource investigation
   instead of forcing normal per-resource work through global views. Do not
   start broad new shell areas before the supported samples are stable.
   Keep the current quick-create path as a compact shortcut flow, but plan for
   a Resource Manager resource gallery to become the default Add Resource entry
   point, with future wizard-based guided setup for resource types that need a
   multi-step UX.
3. Stateful application foundation: continue the storage and volume-mapping
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
4. Identity validation beyond the built-in provider: keep the built-in
   identity provider for local development, but prove the same resource
   identity and permission model against one third-party OIDC/OAuth provider,
   and keep the Keycloak-backed workload smoke path green before adding broader
   IAM UI.
5. MVP convergence and Resource Manager reliability: keep supported samples
   green, tighten generated resource details, lifecycle actions, activity
   records, diagnostics, and state transitions around the flows that already
   work.
6. Configuration, secrets, and identity polish: finish the visible
   settings/secrets assignment and reference experience, keep identity opt-in
   for early modeling, and close only the built-in access gaps needed by those
   flows.
7. Lifecycle traceability and audit: harden the common lifecycle procedure,
   dependency-start activity, resource-event filtering, and the first audit
   schemas for MVP operations.
8. Host and runtime foundation: complete the shared host diagnostics needed by
   container apps, Docker Compose, and provider-owned runtime infrastructure,
   building on the Resource Manager shutdown path that now stops host-scoped
   workloads while leaving detached workloads recoverable.
9. Network and routing hardening: tighten host-readiness, provider selection,
   route conflicts, endpoint conflicts, configuration preview, and backend
   diagnostics for the supported samples.
10. Remote Docker host completion: finish concrete remote-host registration and
   credentials if it validates the host model, but do not let it block the
   local/default container-host MVP path.
11. Runtime-managed resources and deployment model: the first ownership,
   visibility, cleanup, and internal orchestrator deployment/revision contracts
   are in place. Container apps now project desired replica/runtime-container
   children as hidden runtime-managed resources parented to the app, with
   Resource Manager visibility controlled separately for hidden resources and
   hidden runtime-managed artifacts. Docker host raw container discoveries use
   the same hidden runtime-managed projection by default, while explicitly
   declared Docker containers remain normal user-managed resources. Generic
   child-resource UI should honor visibility settings; providers should expose
   deliberate host/app-scoped tabs when those artifacts need first-class
   inspection. Container apps now have an app-scoped Replicas tab for projected
   runtime children. Next slices should enrich those children only where
   container apps need provider-observed container IDs, health, placement, or
   materialization diagnostics, not as a broad public deployment product
   surface.
12. Advanced app and environment concepts: defer autoscaling, backend pools,
   traffic splitting, provider-backed network-level service discovery,
   provider-backed DNS propagation, external deployment projection,
   external-format resource graph import and code generation, container
   application environments, and the initial on-premise hosting scenario.

### Now: MVP Convergence and Resource Manager Reliability

- Treat the primary MVP management path as:
  container application -> app endpoint/discovery -> virtual network -> public
  endpoint or load balancer -> DNS/name mapping. The UI should let users see
  and operate that path from the application resource configuration experience
  without requiring programmatic-only sample knowledge.
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
  endpoint; richer route editing on existing load balancers remains deferred.
  Next it needs richer provider-specific DNS diagnostics when the MVP
  management flow needs them.
- Keep public endpoint exposure explicit. A resource can expose an endpoint
  directly, through app-owned ingress, through a virtual-network mapping,
  through a load-balancer route, or through an optional service facade when
  that facade is deliberately modeled; Resource Manager should show that
  relationship from both the target resource and the exposure resource.
- Treat storage as part of the same app-environment path, not as deployment
  trivia. The MVP now has basic volume resources and container app volume
  attachments with a dedicated resource Storage tab plus a direct volume
  create/configuration/overview flow, Local Storage resources, and
  Docker/Docker Compose runtime materialization for `FileSystem` mounts.
  Resource Manager volume selectors now only offer mountable volume resources
  and show their storage medium, and Start/Restart action availability now
  preflights unsupported volume and storage-parent media for the current
  container materializers. Application overview pages show attached storage
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
- Identity remains a product differentiator, but it should be proven with a
  standards-based provider instead of staying built-in only. The first Keycloak
  sample validates external OIDC sign-in, CloudShell role claim mapping, and
  sample-scoped resource identity provisioning, and a provider setup/reconcile
  hook with a Control Plane endpoint and a Resource Manager action on the
  provider's provisioning resource. Provider-specific runtime credential
  injection now supplies provisioned Keycloak credentials to workloads through
  the standard `CLOUDSHELL_IDENTITY_*` contract, and protected CloudShell
  services can validate configured external OIDC/OAuth bearer tokens before
  applying CloudShell scoped resource-permission claims. The Third-party
  Identity sample now includes automated smoke coverage for a
  Keycloak-provisioned workload that reads configuration with a provisioned
  resource identity. The generated Identity tab now shows provisioning status
  and status diagnostics for identity-bound resources. Next identity work
  should keep that path stable while improving provider readiness and
  authorization diagnostics that directly affect MVP flows.
- Keep the baseline samples building and smoke-testing as the release gate:
  combined hosting, split hosting, container host, settings and secrets, host
  virtual networking, load balancer, project references, third-party identity,
  application topology, and container app deployment.
- Use the forked Application Topology sample as the broad MVP composition
  sample. ProjectReference remains the focused ASP.NET Core project dependency,
  service discovery, log, and trace baseline; ApplicationTopology is where SQL
  Server with mounted storage, configuration, secrets, identity, structured
  logs, traces, container apps, and networking should converge as those
  primitives stabilize. The first ApplicationTopology SQL/storage slice is in
  place: Local Storage, a storage-owned SQL data volume, and a sample-local SQL
  Server container app are declared, with the backend API resolving SQL Server
  through CloudShell service discovery and exposing a `/database` check that
  the frontend calls through the API. A later SQL/database identity slice
  should let application resources use CloudShell resource identity for
  database authentication in an Azure-like flow.
- Treat the Settings and Secrets sample as the current proof of the developer
  service-integration flow: a resource can model settings and secrets first,
  then opt into identity and resource-scoped grants when access enforcement is
  needed.
- Make Resource Manager generated details predictable for common resources:
  Overview first, resource-specific tabs next, Environment after resource
  configuration, and Identity/Activity near the bottom.
- Keep resource-scoped operations in context. Events should remain under the
  resource Management menu as resource-management history. Logs and Traces now
  render inline from a resource-detail Telemetry menu group when matching
  application/runtime signals are available. Resource Monitoring should be a
  provider-supported, extensible Management tab for resource metrics such as
  process/container CPU and memory usage. `management:monitoring` is now the
  standard predefined resource view ID for provider-owned Monitoring tabs.
  ASP.NET Core resources already report application-level health checks for
  the resource; CloudShell does not currently have a separate resource-level
  health-check model. Cross-resource trace exploration can keep a shared
  Telemetry area with resource-aware links back into the relevant resource
  detail views.
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

### Next: Configuration, Secrets, and Identity Polish

- Keep [Resource identity and permissions](resource-identity-and-permissions.md)
  as the current-state feature documentation and
  [Identity and access](proposals/core/identity-and-access.md) as the open-work
  tracker.
- Finish the Resource Manager assignment experience for literal settings,
  configuration-entry references, and vault-backed secret references on
  resources that advertise environment-variable support.
- Show saved references and diagnostics without displaying resolved secret
  values. Application overview now renders app-setting and environment-variable
  references as source labels and target references instead of resolved values
  or raw CloudShell reference strings, and shows basic target availability and
  identity-grant status.
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
  resource, effective permission APIs, durable external authority
  reconciliation, and provider-native requested-versus-effective grants.
- Keep Microsoft Entra ID compatibility as a required contract target, but do
  not block MVP on a full Entra provider if the provider-neutral contract and
  compatibility tests are clear.

### Next: Lifecycle, Traceability, and Audit

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
  Management, and application telemetry belongs under Telemetry.
- Define only the audit event schemas needed by current MVP operations:
  resource actions, host/runtime operations, image deployments, authorization
  decisions, identity provisioning, configuration reads, and secret reads.

### Next: Host and Runtime Foundation

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
  orchestrator policy instead of a side effect of runtime-state recovery.

### Next: Remote Docker Host Completion

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

### Next: Network and Routing Hardening

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

- Decide which runtime artifacts become runtime-managed resources versus
  provider-owned state: replicas, implementation containers, images, endpoint
  registrations, backend registrations, health probes, and revisions.
- Define ownership, visibility, query, authorization, cleanup, and
  garbage-collection rules for runtime-managed resources.
- Design provider-originated resource change streams so providers such as
  Docker can push discovered container/status changes into Resource Manager
  instead of relying only on UI-side inventory polling.
- Design provider-owned replication projection for resources that can
  implement replicas, keeping stable resources separate from runtime instances
  and using parent-derived naming conventions for materialized runtime
  containers.
- Preserve container app current-revision projection for image updates; defer
  rich rollout history, rollback, retention, and first-class deployment
  resources until runtime ownership and traceability are clear.

### Later: Advanced App and Environment Concepts

- Defer container app autoscaling beyond the current explicit replica-count
  API.
- Defer backend pools, TLS binding, traffic splitting, advanced service
  exposure, DNS/name mapping, external deployment projection, and container
  application environments until host, routing, identity, runtime ownership,
  and deployment decisions are stable.
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
  diagnostic until ownership, visibility, cleanup, and traceability are stable.

### Defer Deliberately

The following work should stay out of the MVP unless a release-gating sample
forces a smaller slice:

- Autoscaling, traffic splitting, backend pools, TLS binding, rollout history,
  rollback, and first-class deployment resources.
- Provider-backed network-level service discovery and public DNS propagation.
- Broad IAM features such as inheritance, multiple identities per resource,
  effective permission APIs, durable external authority reconciliation, and
  provider-native requested-versus-effective grant models.
- External deployment projection, resource graph import/code generation, and
  container application environments.
- The initial on-premise hosting scenario beyond the design and sample
  preparation needed to avoid dead-end MVP choices.

### Release Gate

Before treating the MVP as ready, verify:

- The supported sample smoke suite covers combined hosting, split hosting,
  container host, settings and secrets, host virtual networking, load balancer,
  project references, third-party identity, application topology, and container
  app deployment.
- Resource Manager can create, inspect, update, operate, and diagnose the main
  app environment path without relying on programmatic-only sample knowledge.
- API/client projections preserve the same domain-shaped model as Resource
  Manager, including endpoint network mappings, resource actions,
  capabilities, identity, events, and diagnostics.
- Failure states return stable action capability reasons, diagnostics, or
  ProblemDetails codes rather than provider-specific exception text.

## Tracking Work

The current task queue and milestone scope stay in this roadmap. Completed
durable decisions stay in [ADR](../ADR.md). Landed changes and verification
expectations stay in [Changelog](../CHANGELOG.md).
Proposal statuses stay in [docs/proposals](proposals/).
