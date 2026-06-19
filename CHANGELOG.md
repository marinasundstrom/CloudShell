# Changelog

This is the dated CloudShell change history. It records implementation slices,
stabilization work, samples, and documentation changes after they land.

Use [ADR](ADR.md) for architectural and product decisions,
[Roadmap](docs/roadmap.md) for milestone scope and task order, and
[CloudShell goal](docs/goal.md) for the durable product goal. Changelog entries
link to ADR entries when a change depends on a recorded decision.

## Changes

Entries are grouped by the date their first bullet line was introduced, based
on `git blame --follow`, and then by the broad type of change.

### 2026-06-19

#### Changed

- Refreshed the MVP execution order around the current local-development
  target: Application Topology confidence, app-centric Resource Manager
  workflows, readiness diagnostics before failure, settings/secrets/identity
  clarity, persisted-state handoff, and release hardening.
- Refreshed the local-development MVP goal and roadmap to frame Resource
  Manager as a solid but not overbuilt app-centric developer cockpit, with
  `Persist()` as a state handoff and deployment left to the future
  orchestrator API.
- Documented the post-MVP shell composition direction: CloudShell UI should
  become an independently useful extensible shell platform with menu groups,
  child items, pages, standard settings, notifications, named content areas,
  and Resource Manager alignment with generic shell primitives. This follows
  [ADR-20260619-002](ADR.md#adr-20260619-002-make-cloudshell-ui-a-generic-extensible-shell).
- Built-in identity persistence is now configured under
  `Identity:BuiltIn:Persistence` with its own provider and connection string,
  and Resource Manager persistence rejects sharing the same database with the
  built-in identity store. This follows
  [ADR-20260619-001](ADR.md#adr-20260619-001-keep-built-in-identity-persistence-separate-from-resource-manager-persistence).
- Resource detail pages and resource-list blades now show the latest
  unsuperseded resource lifecycle/action failure below status and health
  indicators, with a short message and a link to the resource Activity tab.
- Resource Manager primary labels, alerts, and resource reference pickers now
  prefer resolved display names or resource names instead of resource IDs when
  a referenced resource is available.
- Resource monitoring views now refresh automatically while open and expose an
  auto-refresh toggle while keeping manual refresh available.
- Access Control principal search results now render as a vertical list with
  full-width principal rows and disambiguate resource identity display names as
  `<DisplayName> (<resource name>)` when those values differ.
- Resource Manager now includes a top-level Health workspace that polls
  configured resource health checks at the Resource Manager health-check
  interval, summarizes resource status, and links back to resource details.
- Resource list rows now show a warning triangle for resources with the latest
  unsuperseded lifecycle/action failure so operators can scan the list, click
  the row, and inspect the matching blade failure summary.
- Resource Manager severity callouts now use consistent success, info, warning,
  and error iconography and coloring; hard lifecycle/action failures are
  recorded and displayed as errors while dependency startup warnings remain
  warnings.
- Resource severity is now modeled with the shared `ResourceSignalSeverity`
  abstraction, with `ResourceEvent` using typed severity directly and Resource
  Manager diagnostics using the same severity vocabulary.
- The built-in application provider package no longer registers the legacy
  aggregate `applications` resource provider. Executable apps, ASP.NET Core
  projects, container apps, and SQL Server now register as separate provider
  boundaries while sharing internal application infrastructure.
- Application resource providers now advertise type-specific capability facets:
  generic container apps expose image updates, replica updates, and
  orchestrator service procedures, while executable apps, ASP.NET Core
  projects, and SQL Server keep those container-app facets off their provider
  boundary.
- `ApplicationResourceService` is now treated as shared application
  infrastructure instead of a provider-shaped implementation; provider-facing
  lifecycle, template, declaration, orchestration, and availability facets live
  on the concrete application resource providers.
- Dependency auto-start hard failures now record failed start signals on
  intermediate resources whose own dependencies failed, so a resource such as
  an API can show that SQL Server failed underneath it instead of only the
  originally requested frontend showing the failure.
- Resource procedure results now carry structured success/info/warning/error
  signals. Dependency auto-start warn-and-continue results keep the action
  success message separate from warning signals, and Resource Manager action
  surfaces render those warnings explicitly.
- Generated resource overview diagnostics now include lifecycle readiness
  warnings from Start/Restart capability reasons, so resources without custom
  overview pages still explain blocked local-development preflight checks.
- Resource list detail blades now show the same lifecycle readiness warnings,
  making disabled Start/Restart preflight reasons visible without opening the
  full resource details page.
- Application overview environment references now mask literal values for
  common credential-like names such as API keys, tokens, client secrets,
  credentials, and connection strings instead of only password names.
- Generated environment editors now treat credential-like literal setting and
  environment variable names as sensitive, avoid pre-populating stored values,
  and preserve the stored value when the hidden field is left blank.
- Environment setting updates now preserve structured procedure signals from
  app-setting and environment-variable provider results when the Resource
  Manager combines both updates into one apply response.
- Application overview pages now include an immediate dependency graph with
  links, state/type metadata, and incoming dependents so the app-centric
  Resource Manager path explains local resource relationships, not only a
  dependency count.
- Observability now includes separate Request graph and Request map views that
  derive OpenTelemetry-style edges from trace parent/child spans, animate
  active request paths in the map, link mapped services back to CloudShell
  resources and request edges back to traces, and show node status from
  telemetry errors or resource lifecycle state.
- The Telemetry workspace now acts as an observability dashboard with linked
  cards for logs, request graph, request map, traces, and metrics, plus
  resource and service summaries from the current telemetry data.
- The CloudShell dashboard now summarizes environment resources, lifecycle
  state, active resource failures, health-check status, failed request spans,
  and operational quick links, refreshing from Resource Manager on the
  configured health-check interval and linking failed requests into traces.
- The CloudShell dashboard now groups failed request telemetry by trace and
  keeps those entries out of the resource system-state list, avoiding duplicate
  dashboard rows for the same request failure.
- The Application Topology sample now includes an intentional frontend-to-API
  failure route so failed request telemetry, traces, and correlated application
  logs can be exercised from the sample.
- Shell navigation parents with sub-items now show a right-aligned collapse
  chevron, and each user's collapsed navigation groups are persisted through
  environment settings.
- ASP.NET Core project resources now use a serialized build step before normal
  startup and run with `dotnet run --no-build`, using the CloudShell host
  content root for relative project paths and working directories. This
  preserves the existing project-path authoring model while avoiding competing
  implicit builds when local project resources share dependencies.

### 2026-06-18

#### Changed

- The generated Resource Manager Identity tab now shows resource identity
  provisioning status and status diagnostics, including missing
  provisioning-status provider warnings, and refreshes status after invoking
  provisioning.
- Identity provisioning resources now expose a `setupIdentityProvider`
  Resource Manager action that runs provider setup or reconciliation through
  the existing provider-neutral setup hook and requires the identity
  provisioning permission on the provisioning resource.
- Identity provisioning setup actions now report a Resource Manager action
  availability reason when the provisioning resource is not attached to a
  configured resource identity provider.
- Application setting and environment reference displays now distinguish
  unchecked identity grant status from a checked missing grant, so Resource
  Manager does not show a grant-required warning before grant data is loaded.
- Control Plane API ProblemDetails for setting reference resolution failures
  now include `settingName` and `referenceKind` extensions while preserving
  the `resourceActionUnavailable` error code.
- Denied resource actions now record warning resource activity entries using
  the failed action event type before returning the insufficient-permission
  error.
- Resource event queries now support `spanId` filtering alongside `traceId`
  across the in-process store, persisted store, Control Plane API, remote
  client, and Resource Manager related-activity links.
- Resource detail pages now expose resource logs and traces as inline
  `Telemetry` views when matching signals exist, and trace/log links now keep
  users in the resource detail context.
- Resource Manager now defines a standard `management:monitoring` predefined
  resource view ID and icon metadata so providers can contribute resource
  Monitoring tabs under Management.
- Resource Manager now defines a standard `telemetry:metrics` predefined
  resource view ID and icon metadata so providers can contribute application
  Metrics tabs under Telemetry.
- Telemetry metrics now have in-memory Control Plane storage, list/ingest API
  endpoints, remote-client support, shared and inline Metrics views, and
  Project Reference sample request count/duration ingestion.
- Shell navigation now supports parented navigation items, and Logs, Traces,
  and Metrics now appear as child items under Observability.
- Resource Monitoring now has provider-backed snapshot contracts, Control
  Plane API/client support, a generated Management > Monitoring tab, Docker
  container CPU/memory metrics, and a dedicated proposal tracker.
- Resources that support generated resource monitoring now advertise the
  `monitoring` resource capability, and Resource Manager requires both that
  capability and provider monitoring availability before showing the generated
  Monitoring tab.
- Docker resource monitoring snapshots now include network I/O, block I/O,
  process count, restart count, and uptime metrics when Docker reports them.
- Application resources now provide basic process resource monitoring for
  executable and ASP.NET Core project resources, including CPU, memory, thread
  count, process count, and uptime snapshots while the local process is
  running.
- Single-instance container-backed application resources now expose generated
  resource Monitoring snapshots from container-host stats when a static/default
  container host can be resolved; replica-mode container apps remain on the
  planned provider-owned dashboard path.
- Container app resources now have a provider-owned Management > Monitoring
  tab that summarizes single-instance container metrics and aggregates
  replicated app usage by projected runtime replica/container.
- Projected container app runtime replica resources now advertise the
  `monitoring` resource capability and can return resource monitoring snapshots
  from container-host stats when their owner app and static/default container
  host can be resolved.
- Logs views now show explicit not-found states when URL parameters reference
  unavailable log sources or resource log filters instead of silently falling
  back to another log selection.
- Traces and Metrics views now show explicit not-found states when URL
  parameters reference unavailable telemetry resources or scopes instead of
  falling back to the first available resource.
- Resource detail pages now show an explicit not-found state when the `tab`
  query parameter references an unavailable resource view instead of silently
  falling back to another tab.
- The Add Resource page now shows an explicit not-found state when the `type`
  query parameter references an unavailable resource type instead of silently
  falling back to the first installed type.
- Resource detail Traces and Metrics tabs now compose dedicated resource-tab
  wrappers over shared explorer components, matching the Logs tab treatment
  instead of embedding the route page surfaces directly.
- Generated Resource Manager tabs now use a shared ordered section layout so
  provider extensions can append sections to standard views such as Overview,
  Endpoints, DNS, Identity, Access control, Activity, and Monitoring without
  replacing the entire tab.
- Configuration Store and Secrets Vault now contribute provider-owned summary
  sections to the generated Overview tab instead of replacing the standard
  Overview page.
- Resource detail side navigation now omits duplicated resource metadata and
  leaves canonical identity, group, provider, and declaration details in the
  Overview tab.
- Configuration Store and Secrets Vault resources now provide the same basic
  service-process resource monitoring snapshots through their local service
  process runner.
- The generated Resource Manager Monitoring tab now preserves provider metric
  order so primary usage metrics can appear before supporting counters.
- Configuration Store and Secrets Vault resource detail menus now use the
  generated General Configuration tab instead of duplicate Settings tabs, and
  place Entries and Secrets under General with distinct icon metadata.
- Configuration Store Entries and Secrets Vault Secrets views now use more
  specific section headings, compact counts, icon-led editor actions, and
  responsive row layouts.
- Container app Deployment and Scale and replicas tabs now appear under an
  Application resource menu group, and replica diagnostics are merged into
  Scale and replicas instead of a separate Replicas tab.
- Telemetry trace and metric queries now accept provider-neutral scope filters
  for scope resource ID, scope name, scope kind, and deployment revision so
  future Resource Manager views can filter multi-instance resource signals by
  scope.
- Resource observability metadata now includes provider-neutral telemetry
  source and scope declarations, Control Plane API/client resource projection,
  and shared/inline Trace and Metric scope selectors for resources with
  multiple announced telemetry scopes.
- Load balancer resources now have a Resource Manager Configuration tab for
  adding or editing routes on existing load balancers, and application endpoint
  shortcuts now route to that editor when exactly one same-group load balancer
  is available.
- Provider-selected DNS name mappings now show a Resource Manager
  pending-publish diagnostic before the first reconcile observation, pointing
  users to the DNS zone reconcile action.
- Local host-name publishing observations now project hosts-file target and
  resolver-cache refresh details onto name mappings, and Resource Manager
  shows those as materialization diagnostics after reconcile.
- Local host-name publishing now feeds wildcard-host and unpublishable target
  address checks into DNS zone Reconcile name mappings action availability.
- Name mapping generated diagnostics now warn when the target resource,
  target endpoint, or local host-name target endpoint address is unavailable.
- Container app volume assignment and Scale and replicas surfaces now warn
  when replicas would mount volumes that do not advertise access compatible
  with replica fan-out.
- Volume resources now project storage runtime status for direct local paths
  and storage-owned subpaths so Resource Manager can warn about missing paths
  or invalid subpaths before a consuming resource is started.
- Application Topology now includes a built-in development resource identity
  for the backend API, startup identity provisioning, and scoped grants for
  Configuration Store and Secrets Vault read access.
- Application Topology now also provisions its Configuration Store and Secrets
  Vault resource identities on startup so the identity and access-control demo
  shows provisioned identities on both protected target resources.
- Application overview pages now surface Start readiness using existing
  Resource Manager action availability reasons so preflight blockers are
  visible before invoking lifecycle actions.
- Container app Deployment tabs now surface image-update and restart readiness
  diagnostics before enabling the Deploy command.
- Application overview pages now summarize configured app setting and
  environment-variable references, including safe target details and identity
  grant status for configuration and secret references.
- Programmatic resource projections now include declaration persistence
  metadata, and resource details show whether a resource is a startup
  declaration or a persisted declaration.
- Resource Manager detail pages and resource-list blades now warn when a
  resource is a transient startup declaration whose UI changes are not durable.
- Resource Manager inline action buttons now include read-only and
  control-plane action-unavailable reasons in their titles.
- Application overview pages now show configured storage mounts with volume,
  target path, access mode, and runtime materialization status.
- Application overview pages now list inbound network mappings,
  load-balancer routes, and DNS name mappings with target and provider status.
- Application overview pages now summarize resource identity binding,
  provisioning status, and outbound permission grants with a link to the
  Management > Identity tab.
- Resource Manager now includes a generated Management > Access control tab
  for assigning and revoking resource identity grants grouped by target
  resource, with Control Plane API/client commands for grant intent changes.
- The generated Resource Manager Access control tab now treats the current
  resource as the protected target, uses a searchable resource-identity picker,
  and groups assigned access by the resource identity that can access the
  target resource.
- The built-in Identity authentication mode now exposes a rudimentary
  shell Users page for creating local test users with roles and CloudShell
  resource-group, resource, or resource-permission claims.
- The shell Users page now indicates when local users are backed by the
  in-memory identity store so operators know those users are process-scoped.
- The generated Resource Manager Access control tab now filters its permission
  picker to operations relevant to the current target resource while keeping
  custom and all-permission options available.
- Resource Manager now shows generated Identity and Access control tabs when
  the environment has a default resource identity provider. The Identity tab
  reflects identity enablement with an editable `Enable identity` checkbox
  backed by Control Plane registration identity state, and Access control uses
  a Fluent UI autocomplete search box instead of a separate search field plus
  select.
- Access control now projects resource identities as `ResourceIdentity`
  principals, exposes principal metadata on permission-grant API responses,
  and shows assignment controls for protected target resources even when the
  target resource does not have its own identity binding. This follows
  [ADR-20260618-002](ADR.md#adr-20260618-002-model-access-control-as-principal-to-resource-grants).
- Identity providers now have a provider-neutral directory query contract for
  future Entra/AD-style principal lookup across users, groups, service
  principals, managed identities, workload identities, and provider-owned
  identity references.
- Control Plane API/client now expose provider-backed principal lookup through
  `IResourceManager.QueryResourcePrincipalsAsync(...)`; Resource Manager Access
  control uses that principal source for its resource-identity picker, and the
  built-in ASP.NET Core identity provider exposes provisioned resource identity
  clients, persisted local users, and configured in-memory test users through
  the same directory hook for local-development reference behavior.
- Programmatic access grants now use `ResourcePrincipalReference` through
  `resource.Principal` and `Allow(principal, permission)`, keeping resource
  `Identity` focused on identity binding configuration and provisioning.
- The ResourceHost sample now configures the built-in in-memory identity
  provider with an `alice` ASP.NET Core Identity test user, grants that user
  access to the sample database, and verifies that login user can only read the
  granted resource.
- `ConfigureInMemoryIdentity(...)` now uses an in-memory ASP.NET Core Identity
  store for local development, so configured users, roles, claims, and
  grant-derived resource permissions are all cleared on shutdown.
- Programmatic resource declaration startup now names the persisted-declaration
  handoff explicitly and tests that transient declarations are projected
  without creating core resource registration rows.
- Resource Manager-authored DNS name mapping create/update now rejects
  duplicate host/exposure mappings in the same DNS zone before saving.
- Resource Manager DNS name mapping create/update forms now show duplicate
  host/exposure conflicts before submitting.
- The generated Resource Manager Environment tab now uses the
  `management:environment` predefined view ID so it appears under Management
  with other resource concerns.
- Container app Storage now appears under the Application Resource Manager menu
  group, and container app Deployment plus Scale and replicas tabs now carry
  explicit icon metadata.
- Resource Logs now default to an operational `All logs` view instead of
  Activity when application/runtime logs exist, expose an explicit `All
  resources` resource filter for the standalone Logs page, and open log-entry
  details only after selecting an entry so the log feed can use the full width.
  The standalone Logs page and generated resource Logs tab now compose shared
  log explorer/feed/viewer/details components without embedding the route page
  in the resource tab, and the resource tab uses a slimmer log view that omits
  standalone Logs page context.

#### Fixed

- Invalid Resource Manager details URLs now navigate to a dedicated Resource
  not found page that shows the missing resource ID instead of rendering the
  standard resource details layout.
- Application start now resolves service-discovery environment variables from
  the Resource Manager projection instead of resolving scoped resource
  providers from the root service provider.
- Configuration Store and Secrets Vault resource startup now use configurable
  service readiness timeouts with a longer default so sample dependency
  autostart tolerates local child-service startup cost.
- Sample smoke test command failures now include the response body and allow
  longer resource-action requests for dependency startup.
- Detached local process launches now resolve the current `dotnet` host path
  so provider-owned child services and project resources can start when
  non-interactive shells do not have `dotnet` on `PATH`.
- Generated and application-specific Overview pages now show declaration
  persistence again so startup declarations are visible in Resource Manager.

#### Documentation

- System design guidelines now link to the Fluent regular icon catalog for
  shell and Resource Manager icon selection.
- Roadmap and logging-infrastructure planning now call out resource-scoped
  inline Events under Resource Manager Management, plus Logs and Traces under
  a resource-detail Telemetry menu group, while distinguishing application
  Telemetry Events/Metrics from Resource Events/Metrics and placing provider
  process/container resource monitoring under Management.
- Roadmap planning now classifies work as features, backend enhancements, or
  UX enhancements so impact-based ordering can treat UI polish and backend
  capability work independently.
- Resource monitoring planning now calls out provider-owned container app
  Monitoring dashboards for app-level summaries and per-replica/container
  resource metric breakdowns.
- Control Plane API planning now records live telemetry and resource
  monitoring subscriptions for split-hosted UIs as a later design question
  after basic provider monitoring support.
- Telemetry planning now defines stable resource-scoped Logs, Traces, and
  Metrics with an `All instances` scope default for multi-instance resources,
  while keeping provider-observed resource metrics under Management >
  Monitoring.

### 2026-06-17

#### Changed

- Local Storage overview pages now warn when consumers of owned volumes report
  partial, inactive, or unobserved storage mount materialization, using the
  same projected mount materialization attributes shown on application and
  volume views.
- Local Storage resources now project `storage.runtimeStatus` and
  `storage.runtimeStatusReason` for provider-backed filesystem availability,
  and Resource Manager warns when an explicit local storage root is
  unavailable.
- Added `docs/resource-model.md` as the low-level structure reference for the
  projected `Resource` object, covering identity fields, lifecycle state,
  relationships, endpoint descriptors, endpoint network mappings, configured
  endpoint mappings, actions, capabilities, attributes, ownership metadata, and
  resource/service terminology.
- `ResourceEndpoint` no longer carries an address; endpoint factories now
  create endpoint contracts, address-bearing compatibility factories only infer
  target ports, and concrete reachability remains on endpoint-network mappings.
- DNS/name publishing now resolves target addresses from
  `ResourceEndpointNetworkMapping` when available, with `ResourceEndpoint`
  address retained only as a compatibility fallback. The resource endpoint
  factory now supports address-less endpoint contracts.
- Resources now expose endpoint-network mapping lookup helpers so consumers can
  resolve reachable endpoint addresses by endpoint name. Resource health checks
  use those mapped addresses before falling back to legacy endpoint addresses.
- Application service-discovery environment variables now resolve endpoint
  binding addresses from endpoint network mappings before falling back to
  legacy endpoint addresses.
- Application overview endpoint display now resolves projected and DNS-derived
  endpoint addresses through endpoint network mappings before falling back to
  legacy endpoint addresses.
- Load-balancer route resolution now carries target endpoint network mappings
  to providers, and the Traefik provider uses mapped target addresses before
  falling back to legacy endpoint addresses.
- Endpoint mapping provisioning contexts now carry source and target endpoint
  network mappings, and local host networking provisions proxy bindings from
  mapped addresses before falling back to legacy endpoint addresses.
- Container application declaration helpers now accept address-less
  `ResourceEndpoint` contracts with target ports and convert them into service
  ports without requiring a manual host/port mapping.
- Application resources with declared endpoint ports now project address-less
  `ResourceEndpoint` contracts and put concrete local reachability in
  `ResourceEndpointNetworkMapping`.
- Docker runtime container projections now expose published container ports as
  address-less endpoint contracts and project Docker host/container reachability
  through endpoint network mappings.
- Configuration Store and Secrets Vault resources now project service endpoints
  as address-less endpoint contracts with concrete service URLs carried by
  endpoint network mappings.
- Container application declaration now converts address-bearing endpoint
  inputs into endpoint mapping intent instead of keeping the address in the
  legacy application endpoint field.
- Network resources now project network-owned endpoints as address-less
  endpoint contracts and carry host-local resolved addresses in endpoint
  network mappings.
- Service resources now project service ports as address-less endpoint
  contracts and carry host-local resolved service addresses in endpoint network
  mappings.
- Load balancer resources now project entrypoints as address-less endpoint
  contracts and carry host-local entrypoint URLs in endpoint network mappings.
- Static CloudShell and managed sample resource providers now project endpoint
  addresses through endpoint network mappings instead of endpoint contracts.
- Docker host resources now project their `host` endpoint as an address-less
  endpoint contract and carry the configured Docker host URI as an endpoint
  network mapping.
- Legacy application endpoint strings now project as address-less endpoint
  contracts, with the configured application URL carried by an endpoint network
  mapping.
- The sample resource host now projects sample service addresses through
  endpoint network mappings instead of address-bearing endpoint contracts.
- Docker container start availability checks now validate endpoint network
  mappings so occupied published ports are still detected after endpoints
  became address-less contracts.
- Platform endpoint assignment validation now uses only endpoint network
  mappings, removing the obsolete endpoint-address validation path.
- Host-local network resolution now materializes endpoint network mappings
  directly instead of returning address-bearing resource endpoints.
- Application overview endpoint display now uses configured fallback addresses
  directly instead of synthesizing address-bearing resource endpoints.
- Docker container declarations can now create address-less endpoint contracts
  plus endpoint network mappings directly, and the container deployment sample
  uses that mapping-native authoring path for the local registry.
- Endpoint network mappings now have a `ForEndpoint(...)` factory that
  standardizes mapping IDs, target references, and source endpoint names across
  providers and samples.
- `ResourceEndpointNetworkMapping.ForEndpoint(...)` now normalizes whitespace
  on resource IDs, endpoint names, addresses, and optional mapping metadata.
- Endpoint references now have a `ResourceEndpointReference.ForEndpoint(...)`
  factory used by endpoint mapping normalization paths.
- Endpoint network mappings now expose a shared endpoint matching helper used
  by resource lookup, Resource Manager endpoint views, and Docker provider
  validation instead of duplicating target/source/name matching logic.
- Resources now expose a resolved endpoint address helper that prefers
  endpoint network mappings and falls back to legacy endpoint addresses, and
  service discovery, overview, health check, and DNS host publishing paths use
  it consistently.
- Traefik dynamic load-balancer configuration now resolves target endpoint
  addresses through the resource-level endpoint address helper instead of
  reading mapping and legacy endpoint addresses separately.
- Application provider local endpoint availability checks now keep only the
  endpoint-network-mapping path and remove the obsolete endpoint-address
  overload.
- Resource endpoints now expose a shared port-resolution helper that prefers
  `TargetPort` and falls back to legacy endpoint-address parsing; load-balancer
  route resolution uses that helper instead of a Control Plane-local parser.
- Resource endpoints now also expose shared address-string port parsing used
  by built-in CloudShell, application, configuration store, and Secrets Vault
  providers instead of provider-local parsing helpers.
- Endpoint network mappings now expose shared URI and port parsing helpers
  used by Docker endpoint availability checks and Control Plane
  load-balancer/endpoint-assignment resolution.
- Resource endpoints now expose a shared URI parsing helper used by endpoint
  health checks, application provider endpoint setup, and local-host endpoint
  mapping provisioning instead of parsing legacy endpoint addresses directly
  in those paths.
- Endpoint network mappings now require a host-bearing absolute URI when using
  the shared URI parsing helper, and load-balancer backend host resolution uses
  the shared mapping/endpoint URI helpers instead of parsing addresses itself.
- Resources now expose mapping-first resolved endpoint URI helpers, and local
  DNS publishing plus resource health checks use them instead of resolving an
  endpoint address string and parsing it locally.
- Application overview endpoint display now uses the resource-level resolved
  endpoint URI helper when projecting DNS/name-mapped overview addresses.
- Application resource endpoint availability checks now use the shared endpoint
  network mapping URI and port helpers instead of parsing mapping addresses in
  a provider-local helper.
- ASP.NET Core project endpoint normalization now uses the same fixed-endpoint
  to service-port helper as the programmatic registration extensions, removing
  a duplicate provider-local conversion path.
- Resource endpoint and endpoint-network mapping URI parsing now share the
  same host-bearing absolute URI helper.
- Resource endpoint resolution is now mapping-only: `Resource` no longer
  synthesizes endpoint network mappings or resolved endpoint addresses from
  legacy `ResourceEndpoint.Address` values.
- Control Plane API and remote client endpoint projections no longer carry
  endpoint addresses on `ResourceEndpointResponse`; concrete addresses remain
  on endpoint-network mapping projections.
- Application overview endpoint display no longer falls back to legacy
  `ResourceEndpoint.Address` values when projected endpoint mappings are
  missing.
- Docker container builder endpoint contract overloads no longer convert
  legacy `ResourceEndpoint.Address` values into endpoint-network mappings;
  published ports use the mapping-aware endpoint overload.
- Resource model, networking, and application docs now distinguish CloudShell
  resources, the runtime services they provide, the `cloudshell.service`
  resource kind, endpoint network mappings, and configured endpoint mappings
  consistently.
- Predefined Resource Manager tab IDs now use the `ResourceViewId` value object
  with explicit `GroupId`, `Identifier`, and serialized `Value` parts, so
  providers and shell UI use the same hierarchical view vocabulary such as
  `general:overview` and `networking:endpoints`.
- Resource detail routes, generated tab grouping, predefined-view sections, and
  extension registration now treat tab IDs as logical view identities instead
  of free-form strings, with query-string serialization only at navigation
  boundaries.
- Application resources now contribute provider-owned exposure actions to the
  predefined Endpoints tab, giving endpoint-capable applications direct
  Resource Manager entry points for load-balancer routes and DNS/name
  mappings.
- Application exposure actions now deep-link TCP endpoints into the
  load-balancer quick-create flow with `routeKind=tcp`, so SQL Server and
  other TCP-only targets do not default to an HTTP route.
- Contextual Add Resource links now preserve the originating resource-page
  tab through a sanitized `returnUrl`, and registration forms use that return
  path after Cancel or successful creation.
- Resource Manager UI extension guidance now documents the direction for
  predefined views to light up from projected resource shape, capabilities, and
  resource type declarations before provider-owned sections or tabs add
  resource-specific depth.
- Resource Manager predefined view visibility rules now live in a shared helper
  and the resource Identity tab lights up for resources that participate in
  permission grants, even when the resource does not own an identity binding.
- Built-in Resource Manager and provider tab registrations now use
  `ResourcePredefinedViewIds` for predefined Overview, Configuration, Storage,
  and Volumes views instead of repeating ad hoc hierarchical tab IDs.
- Resource Manager detail links now use a shared `ResourceManagerRoutes`
  helper so shell pages and provider-owned views construct escaped resource
  detail, overview, and tab routes consistently.
- Resource Manager view terminology now distinguishes general Resource Views
  from CloudShell-owned Predefined Resource Views, and the public extension
  API now uses `ResourcePredefined*` names for predefined view IDs,
  definitions, sections, and visibility rules.
- Built-in resource tab registrations now use shared `ResourceTabGroupTitles`
  constants for predefined and provider-owned group labels instead of
  repeating presentation strings in each provider.
- Container apps now treat replicas as an explicit scaling mode. Resource
  Manager exposes a dedicated Scaling tab to enable replicas and set the
  desired count, while single-instance apps no longer project a default
  runtime replica child. The Deployment and Replicas tabs now distinguish
  single-instance mode from replicated mode.
  Decision: [ADR-20260617-001](ADR.md#adr-20260617-001).
- The container app Scaling tab now prompts endpoint-bearing replicated apps to
  create a prefilled load-balancer route, reusing the same exposure-link
  behavior as application Overview and Endpoints views.
  Decision: [ADR-20260617-001](ADR.md#adr-20260617-001).
- Resource detail headers now show the resource icon before the resource name,
  while a shared resource status summary component shows lifecycle and health
  status above the canonical resource identity fields instead of repeating the
  same resource identity card. The resource detail rail now also separates the
  identity fields from grouped resource view navigation with a divider.
- Resource view menu items can now display icons. Predefined Resource Manager
  views provide default icon metadata, and provider-owned tabs can opt into
  custom icons through the resource tab contribution API.
- Internal shell navigation now resets the main view scroll position after
  route changes, including query-driven resource view switches.
- Networking and application resource docs now define ingress as a
  provider/runtime-owned exposure path for resource endpoints, keeping
  application resources as endpoint owners while reserving load balancers for
  explicit user-managed routing surfaces.
- Application endpoint helper methods now project explicit assignment metadata:
  fixed endpoint URIs and fixed helper ports become manual local endpoint
  assignments, while helper calls without a fixed port become explicit
  auto-assigned endpoint intents.
- Resource type contributions can now declare endpoint descriptors, and the
  built-in ASP.NET Core project, container app, and SQL Server resource types
  advertise their default resource endpoint names, protocols, and target ports.
- Endpoint descriptors now indicate whether a resource type supports port
  remapping, and application registration flows use descriptor metadata for
  default endpoint names, protocols, and target ports instead of duplicating
  those defaults in each create form.
- Networking docs now clarify that port remapping does not bypass topology:
  the remapped concrete endpoint still belongs to a local, container-host,
  virtual-network, or public exposure path that endpoint mappings materialize.
- Networking and domain-model docs now use the same endpoint vocabulary:
  endpoint descriptors, endpoint requests, resolved endpoints, endpoint
  network mappings, and configured endpoint mappings. They also clarify that
  `network:host` is the default topology boundary while
  `networking:host-local` is the provider resource that materializes
  host-local behavior.
- The CloudShell goal and networking docs now state the platform principle of
  exposing provider behavior through familiar, standardized concepts that
  transfer across use cases and systems, while keeping provider-specific
  implementation details inspectable when useful.
- Networking docs now distinguish endpoint mappings from topology-resolved
  addresses and environment policy, including the implied local network used
  by local development and the direction that managed
  on-premise environments can require tenant virtual networks and gate
  localhost, public exposure, and DNS/host-file changes by permission.
- The Resource Manager Endpoints tab now separates endpoint mapping, current
  topology-resolved address, and topology/exposure context so users can
  distinguish what a resource exposes from how it is currently reachable.
- Resource Manager networking views now present resource endpoints as
  protocol/target-port contracts and move copy/open actions to mapped
  addresses, including provider-owned application exposure actions.
- ASP.NET Core project create and update views now use endpoint assignment
  with an optional fixed local port instead of asking users to enter a raw
  endpoint URI under resource-specific configuration.
- ASP.NET Core project endpoint assignment UI and documentation now describe
  fixed local ports as a local-development mapping convenience, while private
  IPs, internal DNS names, and public exposure remain Networking concerns.
- ASP.NET Core project startup now prefers projected endpoint-network mapping
  addresses when setting `ASPNETCORE_URLS`, falling back to the provider's
  local mapping calculation only when no Resource Manager projection is
  available.
- Networking docs now clarify that application resource endpoints are
  resource-owned port contracts, while virtual-network addresses and private
  DNS names are endpoint/network and name mappings over those ports.
- Endpoint assignment UI can now show network selection and optional manual
  host/address fields. ASP.NET project, container app, and SQL Server create
  flows persist the selected network and manual endpoint metadata on their
  service ports, and ASP.NET project edit flows preserve those values.
- Projected resource endpoints now carry optional target-port metadata, so
  application endpoints can expose the resource-owned port while topology,
  network, exposure, and DNS mappings remain separate primitives.
- Resources now project endpoint-network mappings through the Control Plane API
  and remote client so local/Aspire-like endpoint helpers can expose mapping
  addresses separately from the resource endpoint contract.
- Programmatic resource documentation now treats Aspire-compatible endpoint
  helpers as producing endpoint mappings to the implied local network, not as
  the canonical networking model.

### 2026-06-16

#### Changed

- Resource inventory rows, Resource Manager detail blades, resource detail
  cards, and Docker container lists now use Fluent resource icons instead of
  initial-letter badges for resource and sub-resource identity.
- Resource Manager settings summary cards and custom shell view summary cards
  now use Fluent icons instead of initial-letter badges.
- Resource-like lists in observability, generated resource overviews, DNS
  zones, storage volumes, volume consumers, and container app replicas now show
  consistent Fluent resource icons.
- Resource action buttons and action menus now render lifecycle, logs,
  details, and overflow affordances with Fluent icons instead of CSS-drawn
  glyphs.
- Resource Manager primary actions now use Fluent button icon slots for
  create, add-volume, import/export, and apply commands where the icon
  clarifies the command behavior.
- Resource Manager identity views now show scoped resource Name between
  Resource ID and optional display name, so users can distinguish the canonical
  platform identity from the authored resource name.
  Decision: [ADR-20260615-004](ADR.md#adr-20260615-004).
- Built-in programmatic resource declarations now default projected labels back
  to the scoped resource name when `.WithDisplayName(...)` is not set, instead
  of auto-humanizing resource IDs into implicit display names.
  Decision: [ADR-20260615-004](ADR.md#adr-20260615-004).
- Trace and structured-log detail actions now render direct links for related
  logs, related activity, traces, and resource details, so Fluent UI navigation
  controls remain reliable even when the server circuit is refreshing data.
- Fluent UI navigation actions now use `FluentAnchor` instead of
  `FluentButton Href`, and agent/system guidance links to the Fluent UI Blazor
  documentation for component behavior.
- Trace span detail headers now align the service color indicator with the
  selected span title.
- Shell navigation now renders aligned Fluent UI icons for built-in views
  instead of initial-letter badges.
- Collapsed shell navigation state is now loaded by `MainLayout`, which
  renders `nav-collapsed` directly on the shell element while `NavMenu`
  handles the toggle interaction.
- Shell navigation now uses Fluent UI's `FluentNavMenu` and `FluentNavLink`
  components while preserving CloudShell's grouped menu styling and
  layout-owned collapsed state.
- The shell topbar is now separated from layout state handling so navigation
  persistence, shell chrome, and command-surface UI are easier to evolve
  independently.
- The Add Resource page now uses a compact single-column registration flow
  with a dedicated resource-type header panel and a constrained form surface,
  avoiding horizontal clipping and type-picker overlap across shell sizes.
  The constrained create flow now centers within the shell content area and
  no longer repeats the selected resource type title/description above the
  registration form fields.
- The Add Resource page now uses a custom resource-type picker with compact
  selected labels, rich description rows, and Fluent UI icons for built-in
  resource types instead of initial-letter badges. The Extensions page now
  shows the same resource-type icon mapping.
- Resource detail pages now expose the same capability-gated delete
  confirmation flow as the Resource Manager inventory blade.
- Resource status indicators now show a compact Fluent progress indicator for
  starting, stopping, pausing, and restarting transitions in Resource Manager
  views.
- Resource state now includes an explicit `Stopping` transition, and
  application resources persist that transient state while stop procedures are
  in progress.
- In-process resource action procedures now emit Resource Manager change
  notifications when lifecycle actions start and when they complete, including
  dependency auto-start actions.
- In-process resource action procedures now emit failed action change
  notifications when lifecycle actions fail, including the requested resource
  when its start is blocked because a dependency could not start.
- Resource Manager orchestration settings now expose configurable dependency
  start-failure behavior through appsettings and the Orchestrator settings UI:
  fail the requested action or warn and continue.
- Dependency auto-start now walks transitive dependencies even when an
  intermediate dependency is already running, so missing backing dependencies
  still block or warn the requested action according to orchestrator settings.
- Container-backed application resources now treat immediate container-host
  process exits as start failures, including Docker daemon errors, instead of
  recording a successful start that later projects as stopped.
- Application resources now register separate provider boundaries for
  executable apps, ASP.NET Core projects, container apps, and SQL Server while
  sharing internal application infrastructure inside the applications
  capability package. SQL Server still runs through the container application
  infrastructure today, but now has a provider boundary that can evolve toward
  a managed SQL Server resource type.
- Resource Manager pages now use action-start/action-complete notifications to
  show lifecycle transition indicators for dependency auto-starts and other
  externally triggered in-process resource actions.
- Resource Manager now renders lifecycle transition indicators immediately
  when action-start notifications arrive, before the resource model refresh
  completes.
- Application resource overviews now render dependencies as navigable resource
  links with resolved names and resource types instead of comma-separated raw
  identifiers.
- Application resource overviews now include app-scoped diagnostics links for
  activity, logs, and traces when those resource signals are available.
- Application resource overviews now show add-route and add-name-mapping entry
  points for any application resource with an addressable endpoint, not only
  container-backed applications.
- Resource details now add a shared Networking tab for resources with
  endpoints, networking capabilities, endpoint mappings, load-balancer routes,
  or network resource shape, so endpoint and exposure inspection can move
  into a standard concern view.
- Resource details now split networking concerns into separate Endpoints and
  DNS tabs under the Networking group. Those views provide non-running,
  read-only-aware entry points for endpoint configuration and name-mapping
  creation while overview remains the summary surface. Overview and the
  resource-specific Configuration tab now sit under the General tab group.
- Generated resource overviews now focus on essential identity, runtime,
  diagnostics, relationship, and observability summaries instead of repeating
  detailed endpoint, DNS, load-balancer, attribute, action, and health-check
  lists owned by specific tabs or command surfaces.
- Resource detail tab grouping now normalizes contributed `Overview` and
  `Configuration` groups into `General`, so provider-owned resource tabs match
  the generated resource detail grouping. Normalized groups are aggregated even
  when tab ordering places other groups between contributed tabs.
- Application resource overviews now summarize essentials, container-host
  status, networking, storage, environment, and diagnostics while linking to
  dedicated tabs for endpoint, DNS, storage, and environment details.
- Application resource overviews now display the best available endpoint:
  inbound DNS/name mappings first, then projected resource endpoints, then the
  definition endpoint fallback, so container apps with endpoint ports expose an
  address on the Overview tab.
- Resource Manager UI extensions can now contribute provider-owned sections to
  predefined resource views such as Endpoints and DNS without replacing the
  whole tab. Predefined view IDs are exposed through `ResourcePredefinedViewIds`
  so providers and shell components use the same tab/view vocabulary.
- Predefined resource views now have an explicit extension contract in
  `ResourcePredefinedViews`. The extension builder validates whether a built-in
  view can be replaced by a provider-owned tab and whether it accepts
  provider-owned sections, rejecting unknown or non-extensible predefined-view
  targets during extension registration.
- Local UI-host and Control Plane user-settings providers now serialize access
  to `Data/environment-settings.json` through a shared in-process gate and
  atomic file replacement, preventing shell circuit failures during reloads or
  reconnects when navigation or display preferences are persisted.
- Log and trace source filters now use native select controls in the
  observability views, avoiding Fluent UI popup state during source changes and
  periodic trace refreshes.
- Resource inventory blades now show the generated Details action for
  resources with custom detail routes when the user has read access, instead
  of requiring manage access for an inspection-only navigation path.
- Resource Manager permission boundaries can now combine global and
  resource-scoped permissions, and the Storage volumes tab uses that shared
  boundary for the Add volume action.
- Resource Manager create forms no longer expose display-name editing. UI
  registrations use the resource name as the canonical create command name,
  leaving display names as programmatic/local-development presentation labels.
- Resource registration components no longer receive display-name cascading
  state or display-name field parameters now that Resource Manager create flows
  only ask for resource names.
- SQL Server now projects as a managed service resource instead of a container
  app class, and SQL Server guidance now treats arbitrary image override as a
  temporary sample/container bridge rather than the future managed service API.
  Decision: [ADR-20260615-003](ADR.md#adr-20260615-003).
- Projected resources now carry explicit `DisplayName` separately from the
  scoped `Name`, and the Control Plane API/client maps that field so Resource
  Manager labels can stay friendly while details, logs, and automation keep
  canonical resource names.
  Decision: [ADR-20260615-004](ADR.md#adr-20260615-004).

### 2026-06-15

#### Added

- Added a portable local host networking provider resource, endpoint-mapping
  provisioner contract, Resource Manager UI readiness/provider display, and a
  Host Virtual Network sample.
- Added a Managed SQL Server resource proposal and updated the SQL Server
  resource documentation to clarify that the current container-backed
  implementation is transitional. Future SQL Server UX should be
  database-oriented and should not expose generic container app deployment
  controls by default.
  Decision: [ADR-20260615-003](ADR.md#adr-20260615-003).

#### Changed

- Identity providers that name a provisioning resource now declare that
  resource boundary automatically, and the Control Plane projects
  `identity.provisioning` declarations as stateless infrastructure resources
  for permission checks, setup hooks, and provisioning-status reads.
- Protected-service resource-permission evaluation now accepts both direct
  `cloudshell.resource-permission` claims and nested Keycloak-style
  `cloudshell.resource-permission` JSON claim output.
- The Third-party Identity sample now has automated Docker Compose smoke
  coverage for the Keycloak-provisioned workload path: the test starts
  Keycloak, verifies the provisioning resource boundary and provisioning
  status, starts dependent resources, and confirms the API reads Configuration
  Store with a Keycloak-issued token.
- ApplicationTopology now declares Configuration Store and Secrets Vault
  resources and injects referenced setting/secret values into the backend API,
  with the frontend `/upstream` response including a redacted settings check.
- ApplicationTopology now declares a local-hostname DNS zone and maps
  `app.application-topology.cloudshell.local` to the frontend endpoint so the
  broad MVP sample covers name-mapping projection as part of the app exposure
  path.
- ApplicationTopology now disables startup autostart for its SQL Server, API,
  and frontend resources so the documented manual startup order avoids
  concurrent project builds against the shared ServiceDefaults project.
- Volume overview pages now show reverse storage consumers with declared mount
  target path and read/write mode when the consuming workload descriptor is
  available, while preserving the dependency fallback used for deletion safety.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- Container hosts now have a standard `storage.mount.filesystem` capability.
  Docker-backed hosts advertise it, configured default hosts inherit it, and
  application Start/Restart availability reports when a selected host cannot
  mount a managed `FileSystem` volume.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- The local Docker runner now records runtime-observed volume mount
  materialization facts after successful container app starts. Application
  overview pages show mount source, access, and active/not-active status, and
  projected application resources expose aggregate materialization attributes
  that volume overviews can display for consumers.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- ApplicationTopology smoke coverage now asserts the SQL Server container app
  projects declared-but-not-active volume mount materialization attributes
  before the workload is started, keeping the broad MVP sample aligned with
  the storage diagnostics path.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- Declared Docker container resources now participate in resource action
  availability checks for Start actions and report occupied local TCP/HTTP
  endpoint ports before Docker is asked to start the container. This covers
  local registry resources such as the Container App Deployment sample.
- The Container App Deployment sample now reads
  `ContainerAppDeployment:RegistryPort`, its smoke test runs the sample with an
  allocated registry port, and the local registry helper script accepts
  `CONTAINER_APP_DEPLOYMENT_REGISTRY_PORT` so local registry tests can avoid
  host port conflicts.
- The Container App Deployment deploy helper now also derives its default
  `SAMPLE_REGISTRY` value from `CONTAINER_APP_DEPLOYMENT_REGISTRY_PORT`, so
  the registry creation and mock deployment scripts use the same port
  convention.
- The Container App Deployment README now clarifies that the declared local
  registry resource models and tracks the registry in CloudShell, while
  `create-registry.sh` still materializes the Docker registry container for
  local runs.
- Added the shared `IResourceVolumeMountMaterializationStore` contract for
  runtime-observed volume mount facts. The application runtime state store now
  implements it, and Docker Compose records materialized/not-active mount
  observations through that contract after successful lifecycle actions.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- Resource Manager generated diagnostics now warn when standard storage mount
  materialization attributes report partial, not-active, or unknown status, so
  volume consumers surface runtime storage attachment issues outside
  provider-specific tabs.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- Storage-owned volumes are now hidden from the normal resource inventory by
  default and managed from the parent Storage resource's Volumes tab. Direct
  standalone volumes remain normal inventory resources for local development
  scenarios, while the storage proposal now records that shared on-premise
  environments should be able to restrict host-affecting operations to
  administrators or platform operators.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- Creating a volume under a Storage resource now requires manage permission on
  that parent Storage resource. Resource Manager uses the same rule for the
  Storage Volumes tab and volume create form, and the Control Plane enforces it
  before dispatching the create request to the provider.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- The Storage Volumes tab now keeps owned volumes inspectable while showing the
  explicit Manage action only for volumes the current user can manage.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- Volume creation now defaults an owned volume to the selected parent Storage
  resource's group when the volume is created from the Storage Volumes tab or
  when a Storage resource is selected manually.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- Resource Manager UI now shares helper logic for building resource-to-group
  lookups and checking group-aware resource access. A new resource permission
  boundary component keeps permission-gated UI actions from repeating the
  same authorization and presentation plumbing across management views.
- Resource Manager permission coverage now exercises user-account
  `cloudshell.resource-permission` claims through operation capabilities,
  lifecycle action execution, storage-owned volume creation, and resource
  identity provisioning so the UI-first identity path is covered by the same
  Control Plane service behavior.
- Remote Control Plane authentication coverage now includes a constrained
  bearer credential with resource-scoped read and lifecycle-action claims,
  proving protected API/client calls can inspect resource capabilities and
  execute permitted lifecycle actions without manage/delete permission.
- Resource Manager visibility now treats resource-scoped `resources.manage`
  grants as sufficient to inspect the managed resource and load operation
  capabilities, without broadening data-plane permissions such as secrets or
  configuration value access.
- Remote Control Plane authentication coverage now creates an ASP.NET
  Identity user with a resource-scoped permission claim, obtains a password
  grant token from the built-in authority, and verifies the protected API can
  inspect the managed resource with that user account.
- Applying an existing DNS name-mapping resource in Resource Manager now
  reconciles the parent DNS zone when it exposes the name-mapping reconcile
  action, so local host-name mappings attempt to update the configured hosts
  file from the same UI Apply flow.
- Provider procedure contexts can now emit provider-scoped activity events.
  The application provider records non-secret start/stop process and container
  steps, while DNS name-mapping reconcile records when DNS settings are being
  published and when they have been applied.
- Provider-scoped activity event semantics are now documented in the logging
  infrastructure, domain model, artifact guidelines, and lifecycle
  orchestration proposal. Provider events are resource-scoped procedure
  milestones under `event.provider.<provider-id>.*` and must not include
  secrets or raw credential/configuration values.
- Resource detail Apply failures now stay on the page as an apply error
  message instead of escaping through the Blazor circuit. This keeps local DNS
  permission failures, such as denied writes to `/etc/hosts`, visible without
  breaking the Resource Manager session.
- Resource Manager now makes Resource ID the first identity detail in the
  resource blade, detail sidebar, and generated Overview tab. The UI also
  supports `ResourceManager:EnableDisplayNames` and a Resource Manager
  settings toggle so hosts and users can choose whether display labels or
  canonical resource IDs are primary in Resource Manager.
  Decision: [ADR-20260615-004](ADR.md#adr-20260615-004).
- Programmatic resource declaration APIs now take scoped resource names and
  domain-specific parameters instead of display-name arguments. Providers
  derive canonical resource IDs from those names, and optional labels are
  applied with `.WithDisplayName(...)`.
  Decision: [ADR-20260615-004](ADR.md#adr-20260615-004).
- Added a `ResourceId` value object for typed resource-ID construction and
  validation at normalization boundaries.
  Decision: [ADR-20260615-004](ADR.md#adr-20260615-004).
- Resource Manager create forms now show Name before display name.
  Display name is optional and hidden when display-name presentation is
  disabled, with create flows deriving the canonical resource ID from the
  provided name.
  Decision: [ADR-20260615-004](ADR.md#adr-20260615-004).
- Programmatic resource groups can now be declared with stable IDs. The
  ApplicationTopology sample declares `group:application-topology`, assigns
  its resources to that group, and uses concise display names instead of an
  `Application Topology` display-name prefix.
  Decision: [ADR-20260615-004](ADR.md#adr-20260615-004).
- Added naming-convention guidance for resource IDs, scoped resource names,
  configuration keys, and secret names, including using `--` where a hierarchy
  should map cleanly to JSON configuration or systems where `:` has special
  meaning.
  Decision: [ADR-20260615-004](ADR.md#adr-20260615-004).
- Configuration Store and Secrets Vault `IConfiguration` clients now both map
  `--` to `:` when loading values, so a stored name such as
  `Orders--Api--BaseUrl` is addressable through
  `Configuration["Orders:Api:BaseUrl"]`. The built-in Configuration Store now
  applies broad App Configuration-style key validation, while Secrets Vault
  applies Key Vault-style secret-name validation.
  Decision: [ADR-20260615-004](ADR.md#adr-20260615-004).
- Resource detail pages now use Resource Manager operation capabilities for
  apply-button visibility and apply execution guards, keeping update affordance
  checks aligned with inventory manage/delete/action checks.
- Docker host overview and container tabs now read host/container state,
  container logs, operation capabilities, and container action execution
  through public Resource Manager and log manager APIs instead of internal
  provider stores, keeping provider UI aligned with split-hosting and
  authorization boundaries.
- Resource detail pages now cascade the Resource Manager read-only state to
  provider-contributed tabs. The Docker Containers tab consumes that state so
  container action buttons and execution guards honor read-only mode without
  depending on host UI options directly.
- Added a shared Resource Manager cascading parameter name for read-only
  state so provider-contributed UI can opt into host read-only behavior
  without repeating string literals or depending on the Hosting assembly.
- Docker host configuration now consumes the shared Resource Manager
  read-only cascade, disabling editable fields and guarding apply execution
  when Resource Manager is read-only.
- Configuration Store and Secrets Vault edit tabs now consume the shared
  Resource Manager read-only cascade, disabling metadata, entry, and secret
  editors and guarding apply execution in read-only mode.
- Application configuration and Storage tabs now consume the shared Resource
  Manager read-only cascade, disabling editable workload fields, dependency
  selectors, and volume-mount controls while guarding apply execution.
- Resource Manager read-only UI messages and read-only procedure results are
  now centralized in a shared helper so provider tabs do not repeat the same
  string and result construction.
- Added a reusable `ResourceEditorSection` component for Resource Manager
  editor sections with standard header/action layout, and applied it to
  configuration, secrets, and application storage edit tabs.
- Resource environment variable editing now uses the shared editor section
  component for app settings and environment variable sections, reducing
  repeated Resource Manager section/header markup.
- Service, DNS zone, and load balancer registration pages now reuse the shared
  Resource Manager editor section component for grouped form sections.
- Application, ASP.NET Core project, container image, and SQL Server
  registration pages now reuse the shared Resource Manager editor section
  component for references, dependencies, environment variables, volume mounts,
  and storage sections.
- Configuration Store and Secrets Vault registration pages, plus the
  application update tab, now use the shared Resource Manager editor section
  component for entry, secret, dependency, and network exposure sections.
- Application registration pages now share a raw environment variable editor
  component and input model instead of duplicating add/remove row handling
  across executable, ASP.NET Core project, and container image forms.
- Configuration Store and Secrets Vault create/edit pages now share entry and
  secret editor components with shared input models, including the existing
  masked-secret edit behavior.
- Container image registration and the application Storage tab now share a
  volume mount editor component and input model, preserving disabled-state and
  resource-specific target path placeholder behavior.
- Resource Manager now has a shared resource-selection section component for
  checkbox-based target, network, reference, and dependency selectors, reducing
  repeated selection UI and toggle logic across registration and update pages.
- Added a shared Resource Manager resource-group selector component and applied
  it to Service, DNS Zone, and Load Balancer registration forms.
- Application, ASP.NET Core project, container image, and SQL Server
  registration forms now use the shared Resource Manager resource-group
  selector component.
- Configuration Store and Secrets Vault create/update forms now use the shared
  Resource Manager resource-group selector component, including read-only
  disabling on update tabs.
- Network, Storage, Volume, Docker host, and application update forms now use
  the shared Resource Manager resource-group selector component.
- Added a shared enum select component and applied it to application lifetime
  selectors across application registration and update forms.
- Volume create/update forms now use the shared enum select component for
  access mode selection while preserving custom display labels and locked
  update behavior.
- Core networking, service, DNS, and name-mapping forms now use the shared enum
  select component for protocol and exposure selections.
- Added a shared primary form action component and applied it to matching
  single-action registration forms across Resource Manager, application, and
  configuration pages.
- Resource Manager template export, Resource Manager settings, and application
  deployment submit flows now use the shared primary form action component.
- Added a shared empty-state component and applied it to configuration and
  storage-related resource views with matching unavailable-resource messages.
- Application overview, update, deployment, storage, and replicas views now use
  the shared empty-state component for matching unavailable and unsupported
  resource states.
- Resource Manager update, storage volume, environment, and activity views now
  use the shared empty-state component for matching unavailable and empty
  states.
- Resource Manager resource tabs can now declare named groups. The resource
  detail sidebar renders those group labels instead of relying on divider-like
  padding for Identity and Activity.
- ApplicationTopology is now included in sample smoke coverage. The sample
  host can configure its SQL Server local port, and the smoke guard verifies
  SQL Server, Local Storage, storage-owned volume, project dependencies, and
  grouped resource tabs.
- Host-provided virtual networking now has a portable local host networking
  provider. `networking:host-local` is an activated resource on macOS, Linux,
  and Windows that can materialize virtual endpoint mappings as local TCP
  proxies for HTTP, HTTPS, and TCP endpoints. This is the MVP baseline for
  cross-platform development and team-owned hosts; OS-native Linux, Windows,
  macOS, and runtime-specific providers should plug in through the same
  capability/diagnostic boundary rather than becoming Resource Manager special
  cases. The older `networking:host-macos` helper remains as a macOS-specific
  alias while samples move to the portable provider.
  Decision: [ADR-20260609-002](ADR.md#adr-20260609-002).
- The local host-networking provider now uses the standard
  `network.provisionedMappings` attribute for its active local proxy count, and
  Resource Manager generated networking details display that count when
  available.
- The local host-networking provider now has direct Control Plane test coverage
  that provisions a real localhost endpoint mapping and verifies TCP traffic is
  forwarded through the local proxy.
- DNS/name-mapping reconciliation now records the provider's last runtime
  observation. Name mappings affected by a reconcile action project
  `Published` or `PublishFailed` materialization status, and generated
  Resource Manager diagnostics warn when publishing failed.
- Existing DNS name mappings can now be edited from Resource Manager. The
  update flow preserves the parent DNS zone, uses the existing mapping ID, and
  keeps the parent zone's registration group stable when the child mapping is
  upserted.
- DNS zone detail pages now have a focused overview that lists owned name
  mappings with their target, exposure, provider, and materialization status.
  DNS/name-mapping create and update forms now use CloudShell alert boxes for
  local suffix and local host-name publisher notices instead of unframed
  message bar content.
- Application and generated resource overviews now include DNS/name-mapping
  materialization status when showing inbound DNS-style names, so users can see
  whether a name is logical-only, provider-selected, published, or failed from
  the target resource page.
- Application overview pages now show Aspire-compatible developer service
  discovery references, projected aliases, and representative `services__...`
  environment variable bindings so developers can inspect how referenced
  endpoints will resolve in the local/programmatic flow. The provider and UI
  now share the same display helper for alias and endpoint-key normalization.
- Local Storage overview pages now list owned volumes with consumer counts and
  consumer-reported mount materialization summaries, making storage usage
  inspectable from the storage boundary as well as from individual volumes.
- Container-backed application configuration pages can now change the selected
  container host or return to the default host path, using the same host
  discovery and validation rules as the create flow.
  Decision: [ADR-20260614-002](ADR.md#adr-20260614-002).
- Application overview pages now resolve container-backed resource placement to
  the selected or default container host and show host status, kind, endpoint,
  registry, credentials availability, and advertised capabilities.
  Decision: [ADR-20260614-002](ADR.md#adr-20260614-002).
- Container-backed application overview pages now expose an app-centric
  "Add name mapping" action. The name-mapping registration form can be
  deep-linked with a target resource and endpoint, and it derives a default
  host name from the selected target and DNS zone.
  Decision: [ADR-20260614-002](ADR.md#adr-20260614-002).
- Container-backed application overview pages now expose an app-centric
  "Add load-balancer route" action. The load-balancer registration form can be
  deep-linked with a target resource and endpoint, selects the target resource
  group when needed, and uses the target as the initial route destination.
  Decision: [ADR-20260614-002](ADR.md#adr-20260614-002).
- Resources now project source, management mode, visibility, owner resource,
  and cleanup behavior metadata. The Control Plane API and remote client
  preserve those fields, and Resource Manager hides non-normal resources from
  the standard inventory while keeping them available for parent/detail
  inspection. This prepares container apps to own hidden runtime-managed
  replica/container artifacts.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).
- Added internal orchestrator deployment and revision data contracts so
  container apps, providers, and orchestrators can correlate a stable app
  resource with applied runtime workload versions before rollout history and
  public deployment management APIs are introduced.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).
- Container apps now project desired replica/container runtime artifacts as
  hidden runtime-managed child resources. The child resources are parented to
  and owned by the stable container app, carry replica ordinal/count,
  container-name, revision, and materialization metadata, and stay out of the
  normal Resource Manager inventory. Resource Manager now resolves inventory
  visibility from appsettings defaults and per-user settings: hidden resources
  and hidden runtime-managed artifacts are separate opt-ins, runtime-managed
  inspection requires permission, and non-normal resources remain view-only.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).
- Docker host raw container discoveries are now projected as hidden
  runtime-managed observations instead of normal global inventory resources.
  Explicit `AddDockerContainer(...)` declarations remain normal user-managed
  Docker container resources, and generated child-resource sections now honor
  the same visibility gates as the Resource Manager inventory so provider or
  runtime artifacts do not appear as sub-resources by default.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).
- Container app resources now have a read-only Replicas tab that lists the
  app-owned projected runtime replicas with state, revision, container name,
  materialization, and host metadata. The tab is scoped to the app and does
  not require enabling global hidden or runtime-managed resource inventory
  settings.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).
- Container app image deployment moved from the generic Overview surface into a
  provider-owned Deployment tab. The tab shows the current image, revision, and
  desired replica count, and keeps the deploy-image operation grouped with
  deployment-specific state.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).
- Container apps now project an internal orchestrator deployment view onto the
  stable app resource. The projection includes deployment id, service id,
  status, revision/workload version, desired replicas, and projected runtime
  replicas, and the Deployment tab renders that state without exposing public
  rollout-history or rollback APIs yet.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).
- Container app runtime replica child resources now carry the deployment id,
  service id, and deployment revision they implement. The Replicas tab shows
  the app deployment and service identifiers so expected runtime artifacts can
  be correlated with the Deployment tab projection.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).
- Docker host resources now keep host overview and projected container
  inspection separate. The overview summarizes host status and projected
  container count, while the host-scoped Containers tab lists raw Docker
  container observations and their actions/logs without making those runtime
  observations normal global inventory items.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).

#### Samples

- Updated the Load Balancer smoke test to match the sample's current
  `cloudshell.local` DNS zone, local host-name publisher materialization
  status, and generated Traefik host rules.
- The broad MVP application-topology sample is now forked from
  `samples/ProjectReference` into `samples/ApplicationTopology`. Keep
  ProjectReference focused on the small ASP.NET Core project dependency,
  service discovery, logs, and trace baseline; evolve ApplicationTopology into
  the full frontend/backend sample that composes SQL Server with mounted
  storage, configuration, secrets, identity, structured logs, traces,
  container apps, and networking as those primitives stabilize. The first
  ApplicationTopology composition slice now declares Local Storage, a
  storage-owned SQL data volume, and a sample-local SQL Server container app;
  the backend API references SQL Server through CloudShell service discovery,
  exposes a `/database` check endpoint, and the frontend calls that endpoint
  through the API so the sample exercises frontend-to-API and API-to-SQL
  dependencies. Identity-backed SQL/database authentication is explicitly
  deferred; the intended later goal is for the API to use its CloudShell
  resource identity to access SQL Server in an Azure-like flow.

#### Documentation

- Clarified that orchestrator deployments can be default deployments derived
  from ordinary resource state or configuration changes. Resources remain
  individually manageable by Resource Manager while orchestrators use
  deployments/revisions internally to track what was applied.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).
- Updated the development workflow, agent guide, and repo-local skills to
  distinguish implementation slices from pure documentation slices. Agent-made
  documentation-only changes are now review-first and are not committed or
  pushed automatically unless explicitly approved, and contributors must stage
  only files owned by the current chat or thread.
- Added a resource graph import and code generation proposal. The proposal
  treats Docker Compose YAML as the first external input dialect, keeps
  CloudShell graph drafts as the translation boundary, and makes generated C#
  programmatic declarations the preferred first output while also considering
  UI apply, API, and resource-template scenarios.
- Added `CONTRIBUTIONS.md` to codify the shared CloudShell development workflow:
  make focused slices, add tests when behavior changes, run verification,
  update docs/changelog/ADR where applicable, then commit and push each slice.
  The README, agent guide, and repo-local skills now point to that workflow
  instead of duplicating the general procedure.
- Added `docs/goal.md` as the concise product-goal document for CloudShell and
  made the repo-local skills read it before product or stabilization work.
  `docs/roadmap.md` remains the milestone and task-order document,
  `ADR.md` records durable decisions, and this changelog records landed
  changes. The roadmap now also states that the projected focus order can be
  re-evaluated during implementation when another slice better serves the
  immediate MVP goal.
  Decision: [ADR-20260615-001](ADR.md#adr-20260615-001).

### 2026-06-14

#### Added

- The first mountable-volume domain slices are in place: `resources.AddVolume(...)`
  declares a `cloudshell.volume` resource for a local or addressable storage
  allocation, and container apps can declare `WithVolume(...)` mounts that
  reference either that managed volume resource or an unmanaged local volume
  reference. Application resources project a mount count and storage volume
  consumer capability, volume resources project storage capability and safe
  allocation attributes, and workload descriptors carry each mount plus its
  derived read/write mount permission for runtime providers to materialize and
  enforce. Resource Manager now has the first volume selector UI for container
  app create flows, a dedicated Storage tab for container-backed resources
  that can map volumes, and a basic Resource Manager create/configuration/
  overview flow for direct `cloudshell.volume` resources. Storage mappings
  cannot be changed while the target resource is running, and volume deletion
  is blocked while another resource depends on the volume. SQL Server now
  documents and surfaces its known `/var/opt/mssql` data mount point with a
  persistence warning when no data volume is configured. `cloudshell.storage`
  now provides the first Local Storage resource kind using
  `ResourceClass.Storage`: the resource class defines portable storage
  expectations, the Local Storage kind/provider announces and honors the
  `FileSystem` medium, and storage-owned volumes are modeled as sub-items of
  the provider-managed storage root. Direct
  `resources.AddVolume(...)` volumes remain the lightweight exception: they use
  their own supplied relative or absolute path and are not affected by a
  storage resource location. Other providers may expose different sub-item
  semantics until storage capabilities are formalized. The application
  provider now preflights managed volume mounts during Start/Restart action
  availability and reports an unavailable reason when a referenced volume or
  storage parent uses a storage medium the current container materializers do
  not support.
  The default local Docker runner and Docker Compose generator now materialize
  `FileSystem` volume mounts: managed `cloudshell.volume` resources resolve to
  host bind-mount paths, and unmanaged references remain Docker/Compose named
  volumes. Resource Manager volume selectors now distinguish mountable volume
  resources from storage-provider resources and show the volume storage medium
  in application storage flows, so a Local Storage parent is not presented as a
  directly mountable volume. Application overview pages now show attached
  storage mounts so users can inspect source volumes, target paths, and access
  mode from the managed service page while using the Storage tab for edits.
  Provider-backed volume resources, host-specific compatibility negotiation,
  richer materialization diagnostics, broader UI management, runtime
  enforcement, and usage monitoring APIs remain next storage work. The
  Container Host sample now demonstrates the intended storage graph by
  declaring a Local Storage resource, a SQL Server data volume owned by that
  storage resource, and a SQL Server container mount at `/var/opt/mssql`.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- Load balancers now expose lifecycle state only when a runtime provider can
  manage provider-owned infrastructure. File-config/logical load balancers keep
  their apply action but omit `State` rather than pretending to be `Running`.
  This keeps the stable user-facing resource distinct from future provider-owned
  runtime resources that may be inspectable but hidden from normal views.
  Decision: [ADR-20260613-001](ADR.md#adr-20260613-001).
#### Changed

- CloudShell now distinguishes host topology from installed environment
  capabilities. A CloudShell host application is the ASP.NET Core app that
  hosts the CloudShell UI, the Control Plane, or both. A CloudShell environment
  is the managed local, team-owned, or on-premise cloud-like environment backed
  by Control Plane resource state, installed capability packages, and one or
  more UI hosts. The CloudShell UI and Control Plane remain separate
  application surfaces even when combined in one process. Use "capability
  package" for NuGet-distributed installable environment capabilities that can
  include Control Plane providers, resource type definitions, declaration
  helpers, provider-owned services, Resource Manager UI integrations, shell
  views, and client helpers. The extension entry points inside those packages
  are the in-process registration mechanism used by host applications. Reserve
  "workload" for runtime application execution concerns such as application,
  process, project, or container-backed resources.
  Programmatically declared resources can run from a combined local-development
  host process, but they are managed by the same local Control Plane, which
  remains the owner of declarations, lifecycle policy, provider dispatch, and
  resource projection.
  Decision: [ADR-20260614-001](ADR.md#adr-20260614-001).
- The next MVP product focus is the application environment management path:
  container applications, app-owned exposure and application-level discovery,
  virtual networks, public endpoint exposure, load-balancer routes, and
  DNS/name mapping. The UI should make this path understandable and operable.
  Container applications are now tracked by a dedicated proposal so
  `application.container-app` remains the managed-service resource while host
  placement, deployment/revision history, storage, identity, networking, and
  DNS each keep their own focused proposal boundaries.
  Normal container app exposure should not require a `cloudshell.service`
  resource in the MVP; container apps are the stable deployment, replica, and
  exposure artifacts. Keep `cloudshell.service` optional for logical facades,
  imported provider-native services, non-application targets, or advanced
  routing. Provider-native service objects, such as Kubernetes Services, are
  materialization details unless explicitly projected by a provider.
  This distinction is model-layer separation, not a permanent ban on mapping:
  a future orchestrator may materialize an explicitly modeled
  `cloudshell.service` as its provider-native service primitive, or derive an
  orchestrator descriptor from it, when it represents a service unit.
  Load-balancer route resolution and the Resource Manager load-balancer create
  flow now allow `cloudshell.service` resources as optional facade targets
  while continuing to make direct application targets the normal path.
  The Resource Manager Service registration UI now describes Service resources
  as explicit service units/facades so users do not treat them as required for
  ordinary container app endpoint exposure.
  Application registration UIs now allow explicitly modeled Service resources
  as references, so an app can depend on a deliberate service facade without
  making Service resources mandatory for all app exposure.
  Service resources are also documented as a potential shared frontend for
  manually composed service units or replica sets, such as several web
  application instance resources behind one Service endpoint that a load
  balancer targets. Load-balancer route resolution now expands a
  `cloudshell.service` target to its configured target resources when a
  matching Service definition is available, so providers receive concrete
  backend targets for the manual replica-set pattern. Treat this as bounded
  support for explicitly modeled Service facades, not the next implementation
  focus. Further `cloudshell.service` semantics should wait until the shared
  deployment/orchestrator service model is designed with container apps.
  For MVP, DNS/name mapping can start as logical resource projection,
  relationship display, validation, and provider-materialization diagnostics;
  real public DNS propagation and provider-backed network-level service
  registries remain post-MVP unless a concrete sample needs them sooner. The
  first logical projection slice is now in place: programmatic declarations can
  add `cloudshell.dnsZone` resources and child `cloudshell.nameMapping`
  resources that record host names, target resources, target endpoint names,
  exposure scope, and provider intent for Resource Manager inspection.
  Application overview pages now also surface inbound logical name mappings so
  users can see which internal DNS-style names or custom domain names point at
  a target application endpoint. DNS zones and name-mapping resources now
  project logical conflict status when multiple mappings claim the same host
  name in the same exposure scope, and generated Resource Manager overviews
  surface those conflicts as diagnostics instead of leaving them only as raw
  attributes. Name-mapping resources also now project materialization status:
  mappings without a publishing provider are marked as logical-only and shown
  as diagnostics so users know CloudShell is modeling the name but not
  publishing DNS records for it. The Load Balancer sample now declares a
  logical Local DNS zone for `app.cloudshell.local` and
  `api.cloudshell.local` that targets the public load-balancer frontend,
  demonstrating the distinction between host routing and DNS/name publication.
  Resource Manager generated diagnostics now
  also warn when a selected name-publishing provider resource is missing or
  does not advertise the DNS publisher capability. DNS zones and name mappings
  are registered as inspectable Resource Manager resource types. Resource
  Manager can now create a DNS Zone and optionally include one initial name
  mapping, and it can add standalone name mappings to an existing DNS zone.
  Name mappings are now registered as platform child resources so the normal
  Resource Manager delete flow can remove a mapping from its parent DNS zone
  and refresh the zone dependencies. Update editing for existing name mappings
  remains deferred.
  DNS zones and name mappings do not expose lifecycle status because they are
  logical model resources rather than runtime services. `Resource.State` is
  optional; `null` means no lifecycle status is produced, while `Unknown`
  remains the value for lifecycle-aware resources whose provider cannot
  determine current status. Provider-backed DNS publication should instead use
  an explicit `reconcileNameMappings` action. The initial
  `INamePublishingProvider` contract and DNS zone action are now in place for
  zones with provider intent, including action-availability reasons when the
  selected publisher is invalid or no activated implementation can reconcile
  it. The first concrete local development publisher now supports exact host
  mappings through `local-hostnames`, `UseLocalHostNames()`, and
  `reconcileNameMappings`, writing a CloudShell-managed block to a hosts-file
  style target. System hosts-file reconciliation now attempts a best-effort
  resolver cache refresh with fixed platform commands, while custom
  hosts-file targets skip refresh for safe testing and inspection. The Load
  Balancer sample now uses the explicit `cloudshell.local` suffix and
  documents `CLOUDSHELL_LOCAL_HOSTS_FILE` for safe inspection without
  modifying the system hosts file. Resource Manager DNS zone and name-mapping
  create flows can now choose the local host-name publisher and warn about
  `.local` suffixes before creation. Wildcard
  suffixes, public DNS propagation, provider-backed network-level service
  registries, provider runtime publish diagnostics, and observed applied,
  unknown, drifted, or failed materialization state remain provider-specific
  follow-up work.
  Decision: [ADR-20260614-002](ADR.md#adr-20260614-002).
- Storage and identity are also MVP differentiators from Aspire-style local
  orchestration. CloudShell should model volume resources and volume mappings
  so stateful services can be managed through Resource Manager, and the
  identity model should be validated against at least one third-party
  OIDC/OAuth provider such as Keycloak, Auth0, or Okta in addition to the
  built-in development provider. The first Keycloak sample now validates
  user-facing OIDC sign-in and role claim mapping against the existing
  CloudShell authorization service and declares the external provisioning
  boundary, resource identity binding, and scoped grant so the provider-neutral
  provisioning path is exercised. The sample-scoped Keycloak provisioner now
  creates confidential clients, client roles for declared grants, service
  account role assignments, and token mappers for
  `cloudshell.resource-permission` claims. Identity provider setup/bootstrap is
  now distinct from resource identity provisioning through a provider-neutral
  setup hook and Control Plane endpoint; the Keycloak sample uses it to
  reconcile the UI client's realm-role claim mapper. Runtime credential
  delivery is now separated into a provider-neutral environment hook, and the
  Keycloak sample uses it to inject the standard `CLOUDSHELL_IDENTITY_*`
  contract for sample-created resource clients. Configuration Store and
  Secrets Vault now use shared bearer validation that can accept built-in
  authority tokens or configured external OIDC/OAuth JWT tokens before applying
  scoped `cloudshell.resource-permission` claims. The Third-party Identity
  sample now declares a Keycloak-provisioned ASP.NET Core workload that uses
  `DefaultCloudShellResourceCredential` to call Configuration Store with a
  Keycloak-issued token. The remaining validation step is automated
  end-to-end smoke coverage for that container-backed identity infrastructure.
  Decision: [ADR-20260614-003](ADR.md#adr-20260614-003).
- ASP.NET Core project declarations now have an explicit `AsContainer(...)`
  hook for conversion into `application.container-app` resources. The
  converted resource keeps project metadata in the workload descriptor and
  projects as a container build workload; the default local runner uses the
  .NET SDK container publish path when no Dockerfile is supplied, or a
  Dockerfile build path when the project declares one.
- ASP.NET Core project hot reload is opt-in. Project resources run with plain
  `dotnet run` by default; when `hotReload: true` is declared, CloudShell runs
  `dotnet watch --non-interactive` and sets
  `DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true` so rude edits restart instead of
  blocking on the watch prompt.
- ASP.NET Core project endpoints have an explicit source order: programmatic
  endpoint declarations win, `launchSettings.json` is used only when
  `WithLaunchSettingsEndpoints()` is declared, and the provider otherwise
  assigns a stable local development endpoint. Resource Manager UI create/update
  flows remain manual and do not read launch settings; if a UI launch-settings
  option is added later, it should be disabled when explicit endpoints are
  configured. Broader resource exposure should remain explicit.
  Decision: [ADR-20260613-004](ADR.md#adr-20260613-004).
- Application overview reference rows now evaluate declared resource-permission
  grants for identity-bound configuration and secret references, showing
  granted access separately from missing grant requirements.
- Endpoint ownership is split between Resource Manager and providers. Resource
  Manager prevents CloudShell-owned platform resources from claiming the same
  concrete host/port assignment and now runs advisory local host-port
  availability preflights for platform-owned network, service, and
  load-balancer endpoints. Host/runtime providers still own final bind/publish
  failures. Dangling external processes or containers surface as diagnostics,
  not as platform-owned endpoint reservations.
- Load-balancer apply/start/stop actions now participate in resource action
  availability evaluation. Missing selected providers, host resources, route
  targets, and target endpoints surface as action capability reasons before the
  user invokes the action.
- Resource Manager generated overview pages and the resource list side blade
  now show inbound DNS/name mappings for any target resource, aligning generic
  app-exposure inspection with the provider-specific application overview.
- Resource Manager generated diagnostics also inspect network endpoint mappings
  and name missing provider resources, missing endpoint-mapper capability, and
  unresolved source or target resources/endpoints before a reconcile action is
  invoked.
- Network endpoint-mapping reconcile actions now participate in action
  availability evaluation. Invalid mapping sources, missing target endpoints,
  unavailable mapping providers, missing endpoint-mapper capability, and
  unavailable host-networking provisioners surface as disabled-action reasons
  before the user invokes reconcile.
- The load-balancer fluent API now uses `UseContainerHost(...)` and
  `UseDefaultContainerHost()` for placement so container-host assignment is
  explicit in the user-facing declaration model.
- Docker host resources now advertise the `container.host` resource capability,
  and Resource Manager uses that capability when populating load-balancer
  container-host choices while retaining a fallback for older host resources.
  Decision: [ADR-20260610-001](ADR.md#adr-20260610-001).
#### Fixed

- Control-plane-scoped local process cleanup now waits for captured child
  processes after terminating the process tree. This prevents wrappers such as
  `dotnet run` from exiting while the actual ASP.NET Core child process keeps a
  development port bound.
  Decision: [ADR-20260614-004](ADR.md#adr-20260614-004).
- Local process Start action availability now preflights loopback endpoint
  ports for non-container application resources. If a dangling process already
  owns a configured development port, Resource Manager can show a stable
  "address already in use" reason before the provider attempts to start the
  process.
- Container app Start action availability now preflights local host-published
  endpoint ports, including app-owned ingress ports, so Resource Manager can
  show the same stable occupied-port reason before the Docker-backed runtime
  path attempts to bind the port.
- Load-balancer setup now validates route references and exact route conflicts
  before persisting the platform resource, so routes must reference compatible
  entrypoints, entrypoint names and route IDs must be unique after
  normalization, and duplicate matches on the same entrypoint are rejected
  before provider configuration is written.
- Resource Manager generated diagnostics now surface load-balancer readiness
  issues for missing selected host resources, missing route target resources,
  and missing route target endpoints. The default host marker is treated as the
  implicit container-host selection and displayed as `Default container host`
  instead of a broken resource reference.

#### Samples

- The Project Reference sample now demonstrates distributed tracing across two
  ASP.NET Core web services. The shared ServiceDefaults project uses
  OpenTelemetry ASP.NET Core and HttpClient instrumentation, adds sample
  application spans, and exports span summaries to CloudShell trace ingestion.
  This sample is the current proving ground for a Zipkin-style service-aware
  trace waterfall while keeping traces separate from resource activity and
  logs. The intended trace detail direction is a clickable waterfall with a
  service legend, span details, and links from spans to related logs, activity
  entries, and Resource Manager details.
  Decision: [ADR-20260613-002](ADR.md#adr-20260613-002).
- The host virtual-network sample smoke test now verifies the projected public
  endpoint, endpoint mapping, reconcile action, and reconcile capability state
  so the sample catches API/action drift across macOS and non-macOS hosts.

### 2026-06-13

#### Added

- The first post-MVP target is an initial on-premise hosting scenario. It
  should prove acceptable Resource Manager operations, provider-backed
  cross-platform networking, virtual networks, ingress/public endpoint mapping,
  DNS/name mapping, network-level service discovery, event/integration points,
  and more complex validation samples. Resource Manager read-only mode is now
  available as the `ResourceManager:ReadOnly` UI host setting so
  local-development or programmatic-declaration environments can be inspected
  without letting UI writes override the declared graph.
  Decision: [ADR-20260614-005](ADR.md#adr-20260614-005).
- Resource Manager now makes that bridge navigable: Activity entries and
  structured log metadata link trace IDs to the Traces view, and the Traces
  view can filter retained spans by trace ID.
- Application resources can project transient `Starting` state from
  provider-owned runtime observations while start/restart work is in progress.
  Stale starting observations fall back to stopped so a crashed host does not
  leave an application permanently starting.

#### Changed

- MVP work is now prioritized around convergence of the flows that already
  work: reliable samples, Resource Manager detail polish, settings/secrets
  references, opt-in built-in identity, lifecycle actions, activity records,
  diagnostics, and stable local/default host behavior. Broad IAM, workflow
  automation, remote-host completeness, runtime-managed resources, and rich
  deployment history should not move ahead of those release-shaping slices
  unless they block the supported MVP samples.
- Control Plane resource provider registration and CloudShell UI integration
  registration are separate extension surfaces even when hosted in one ASP.NET
  Core process. User-facing providers are generally expected to ship both the
  Control Plane provider behavior and the matching Resource Manager UI
  contributions.
- CloudShell UI extensions have layered responsibilities: the base UI
  extension architecture contributes shell views and navigation; Resource
  Manager UI extensions build on that architecture for resource-specific UI;
  Control Plane resource providers remain non-UI resource behavior.
- Resource action capability reasons include authorization denial messages with
  the target resource ID, so Resource Manager can explain disabled resource
  actions consistently with action execution failures.
- Authentication-disabled local development still allows all operations by
  default, but hosts and tests can opt into mock-principal permission-boundary
  evaluation with `Authentication:EvaluateClaimsWhenDisabled`. That mode keeps
  the ASP.NET Core authentication pipeline disabled while evaluating normal
  CloudShell permission, resource-group, resource, and resource-permission
  claims on the supplied authenticated principal.
- Resource events can now capture W3C `traceId` and `spanId` from the current
  activity, persist that context, filter activity by trace ID, and project the
  same context into Activity log entries. This gives local distributed-app
  debugging a direct bridge between resource activity, logs, and traces without
  merging those signal types.
- Provider logs and resource events are separate concerns. `ILogProvider` and
  `ILogManager` remain source-oriented operational log abstractions, while
  `ResourceEvent`, `IResourceEventStore`, and `IResourceEventManager` form the
  platform-owned resource activity stream. Resource events are now persisted
  through the Control Plane persistence store and queryable by resource, event
  type, actor, and time range through the Control Plane API and remote client.
  Resource Manager now shows a generated Activity tab backed by
  `IResourceEventManager`, with filters for event type, actor, and time range
  plus action/event group summaries; the generated Activity log remains a
  compatibility view adapter over that stream for log consumers.
  Broader structured logging, audit, diagnostics, metrics, traces, retention,
  and non-text payload decisions are tracked in
  `docs/proposals/core/logging-infrastructure.md`.
- Container host abstraction work now uses host-oriented public names:
  `ContainerHostDescriptor`, `ContainerHostResourceTypes.ContainerHost`,
  `IContainerHostProvider`, `IContainerHostResolver`, `UseContainerHost(...)`,
  `ContainerHostId`, and `WithContainerHost(...)`. `UseDocker()` registers the
  implicit local Docker host through the host provider contract. Control Plane
  container-workload validation and Docker Compose materialization require the
  shared resolver instead of keeping provider-local or engine-compatible host
  lookup paths.
  Decision: [ADR-20260610-001](ADR.md#adr-20260610-001).
- Application app-setting and environment-variable updates now emit
  platform-owned configuration activity events using
  `event.configuration.appSettings.updated` and
  `event.configuration.environmentVariables.updated`, without logging resolved
  values or secret material.
- Standardized image and replica update activity event types as
  `event.deployment.image.updated` and
  `event.deployment.replicas.updated`, with Resource Manager display names and
  grouping aligned to the persisted event type names.

#### Fixed

- During normal Control Plane shutdown, Resource Manager stops running
  host-scoped workloads through the standard lifecycle action path with
  `host-shutdown` as the trigger. Shutdown uses the orchestration catalog
  lifetime signal, skips detached workloads, stops dependents before their
  dependencies, and uses internal system authorization instead of depending on
  the current request user. Provider disposal still terminates any remaining
  control-plane-scoped local process tree as a final safety net, and shutdown
  waits briefly for those processes to exit so host-scoped applications do not
  keep running after the CloudShell host stops. In local development, Ctrl+C
  follows the normal ASP.NET Core host shutdown path. Host-scoped resource
  cleanup uses its own bounded best-effort token instead of propagating the
  host shutdown token, so a cancelled or timed-out server shutdown does not
  crash the Control Plane while it is stopping resources. On startup, the
  Control Plane asks providers to reconcile host-scoped resources before
  declaration auto-start; application process recovery stops stale host-scoped
  PIDs while detached resources remain rediscoverable. Programmatic
  application declarations default host-scoped for local development, while
  UI-created application resources default detached where supported.
- Detached process-backed applications recover by validating persisted PID and
  process start time when the resource definition still exists. Detached
  container-backed recovery is a separate host/runtime concern that should use
  container host identity plus stable container/replica IDs rather than the
  container-host CLI process.
  Decision: [ADR-20260614-004](ADR.md#adr-20260614-004).
- Workload crash recovery is distinct from host restart recovery. Providers
  should project observed stopped/failed state; restart, backoff, and
  provider-native recovery policy belong in the orchestrator/runtime policy
  layer.
  Decision: [ADR-20260614-004](ADR.md#adr-20260614-004).
- Application lifecycle operations, including host shutdown cleanup, emit
  host-console lifecycle log entries in Development environments for local
  diagnostics. Broader operational logging remains a separate policy decision
  so production log volume and persisted resource events/audit can be designed
  intentionally.
- Application Start/Restart capabilities now preflight reference-backed app
  settings and environment variables for missing configuration or Secrets
  Vault target resources and missing identity read grants before dispatching
  orchestration, without resolving or exposing referenced values.
- Added provider-owned Start/Restart capability preflight for reference-backed
  application settings so missing reference targets or missing identity read
  grants disable the action before orchestration dispatch.

#### Samples

- CloudShell is an open platform. Built-in services and samples should dogfood
  the same public integration points, identity model, service APIs, lifecycle
  contracts, diagnostics, and authorization surfaces that extension authors and
  third-party service authors use unless a documented transitional exception is
  needed. Internal capabilities can graduate into public APIs when they become
  generally useful for integrators and the platform owns the contract; the
  resource-permission claim evaluator is now exposed through
  `CloudShell.Abstractions` as a platform-owned authorization integration and
  used by built-in services. The Configuration Store and Secrets Vault backing
  services now use the shared built-in bearer middleware plus that public
  preview claim evaluator instead of validating resource tokens directly in
  each endpoint handler. The same middleware now supports service-bearer
  validation for external OIDC/OAuth JWTs through `Authentication:ServiceBearer`
  settings so built-in and third-party identity providers use the same
  protected-service authorization path. `DefaultCloudShellResourceCredential`
  is now the
  public-preview resource credential chain for authored and built-in services;
  its first source dogfoods the injected `CLOUDSHELL_IDENTITY_*` environment
  contract. Application and container resource providers own injecting that
  credential acquisition environment when they start identity-bound workloads
  or project workload descriptors for container orchestration, while service
  endpoints remain a normal service discovery or explicit configuration
  concern. Container app declarations can opt into the current application-level
  service discovery mapping for referenced resources, and descriptor-based
  orchestrators receive the same `services__...` environment shape. The remote
  Control Plane client can accept the credential directly through SDK-style
  constructors and DI registration so resource-hosted authored services can
  call platform APIs without passing raw bearer tokens. The
  Configuration Store and Secrets Vault service APIs now have matching
  public-preview SDK clients in `CloudShell.Configuration.Client` and
  `CloudShell.Secrets.Client`, backed by the lightweight `CloudShell.Client`
  credential package. They accept the same resource credential without dragging
  in full Control Plane abstractions, own their service-specific
  `IConfiguration` integrations, and are dogfooded by the Settings and Secrets
  sample. `docs/service-discovery.md` now documents the current
  application-level service discovery model, including the
  `Microsoft.Extensions.ServiceDiscovery` dependency required by applications
  that resolve logical service URIs.
  Decision: [ADR-20260613-003](ADR.md#adr-20260613-003).
- Web samples carry `hostsettings.json` with `environment` set to
  `Development`, and load that host setting before creating the ASP.NET Core
  `WebApplicationBuilder` so local sample runs show the development lifecycle
  logs. The helper also adds `hostsettings.json` to builder configuration; the
  pre-builder read is needed because minimal hosting selects the environment
  while the builder is created.

### 2026-06-11

#### Changed

- Resources can project an optional resource identity binding with kind, stable
  name, provider ID when resolved, subject, scopes, and non-secret claim
  metadata. The Control Plane API and remote client expose this as
  `ResourceResponse.identity`.
- Programmatic resource declarations support one optional identity binding per
  resource. Builders can declare a concrete provider binding with
  `WithIdentity(...)` or declare only identity intent with `RequireIdentity(...)`;
  Resource Manager projects the binding and reports unresolved providers
  through diagnostics. Authentication-disabled local development can use a
  mock/development provider, but that is only one development path before
  switching the same resource to Microsoft Entra ID or another production
  provider.
- Programmatic declarations can record permission grants with
  `target.Allow(source.Identity, permission)` and evaluate those grants through
  `ResourcePermissionGrantEvaluator` and the Control Plane API. Resource action
  execution can carry an explicit acting resource identity; Resource Manager
  evaluates declared grants for that identity and does not fall back to the
  current user's permissions in that path. The generated Resource Manager
  overview displays basic identity binding metadata when present, while a
  separate generated Identity tab appears for identity-enabled resources and
  contains declared grants plus the provisioning command. A provider-neutral
  `IResourceIdentityProvisioner` contract and Control Plane
  provisioning planner can group declared identities and matching grants by
  resolved identity provider. Programmatic declarations can call
  `ProvisionIdentityOnStartup()` so the Control Plane asks the provider to
  provision a declared identity during startup, before auto-started or
  manually started workloads need it. A provider-neutral provisioning status
  contract and HTTP endpoint let Resource Manager query provider-owned observed
  state instead of storing that state in resource metadata. The built-in
  development provider can provision an in-memory client-credentials client for
  a resource identity, report whether that client is registered, and project
  declared grants as scoped resource-permission token claims, with compatibility
  permission/resource claims for older callers. The Settings and Secrets sample
  demonstrates a Web API identity with read access to Configuration Store and
  Secrets Vault target resources while preserving reference-backed environment
  variables. The Web API identity is provisioned on Control Plane startup,
  acquires a bearer token from the built-in authority, and calls the
  provider-backed Configuration Store and Secrets Vault HTTP services with
  scoped resource-permission claims instead of configuration-store or
  vault-specific auth secrets. HTTP tests now verify that provisioned built-in
  resource identity tokens respect read, lifecycle action, and
  identity-management permission boundaries through the Control Plane API.
  Provider definitions can now name a separate provisioning resource, and
  provisioning requires
  `CloudShell.Identity/provisioningServices/identities/provision/action` or
  `resources.manage` on that provisioning resource in addition to
  `resources.manage` on the target resource. Provisioning-status reads require
  `resources.read` or `resources.manage` on both the target resource and the
  provisioning resource.
  Configuration and Secrets providers now require matching grants when an
  identity-bound resource resolves configuration entries or secrets. The
  resource owns the identity and permission requirements; the managed
  process/container/service handles safe runtime transfer of the resolved
  values. Identity-provider resource modeling, durable concrete external
  authority registration and status reconciliation, identity management UI,
  multiple identities, and provider-backed managed identity lifecycle remain
  future resource identity work.

#### Documentation

- `docs/resource-identity-and-permissions.md` is the current-state feature
  documentation for resource identity and permissions.
  `docs/proposals/core/identity-and-access.md` is the consolidated proposal
  tracker for open design, decisions, and remaining implementation work.

### 2026-06-10

#### Added

- Added the first built-in Secrets Vault slice: `AddSecretsVault(...)`
  programmatic resources, `vault.Secret(...)` reference helpers, a
  secrets-provider resolver implementation, multiple vault support, and
  template export that preserves secret names without exporting secret values.
- Added Resource Manager UI for creating, inspecting, updating, and deleting
  built-in Secrets Vault resources. Existing secret values are masked in the
  UI and preserved unless replaced.
- Container app replicas can now be updated as an explicit desired count
  through the domain manager and `PUT /api/container-apps/v1/{containerAppId}/replicas`.
  This is not autoscaling: richer replica health, placement, traffic splitting,
  and backend-pool behavior remain future design work. Provider-owned runtime
  containers should be named by convention from the parent container app when
  replicas are materialized.
- Added `ResourceOrchestratorService` as the orchestration-layer service
  artifact for a stable workload. Container apps produce this descriptor today.
  It groups the provider-facing implementation for a service unit, including
  replicas, ports, dependencies, networks, endpoint bindings, and related
  provider-owned runtime services such as app ingress. Docker Compose now
  renders Compose services from that descriptor, including replica count,
  ports, dependencies, and networks, instead of treating workload configuration
  as the service directly.
  The existing `cloudshell.service` resource remains a distinct optional
  platform exposure or facade resource for stable endpoints over non-app
  targets, multiple targets, imported provider-native services, or advanced
  routing; it is not required for normal container app exposure, but future
  orchestrators may intentionally map it to provider-native service concepts
  when the resource is the service unit. Do not expand this area further until
  the deployment and orchestrator service model is settled.
- Added the first settings/reference implementation slice: public
  `AppSetting`, `ConfigurationEntryReference`, and `SecretReference` contracts,
  application builder APIs for literal/reference-backed app settings and
  environment variables, configuration-store entry reference helpers, and
  runtime resolution for non-secret configuration entries.
- Added programmatic host-configuration source resources that expose selected
  host `IConfiguration` keys through configuration-entry references.
- Added built-in Secrets Vault programmatic resources and provider-backed
  secret reference resolution.
- Added built-in Secrets Vault Resource Manager UI for provider-owned vault
  management.

#### Changed

- Standard lifecycle resource actions map to the Azure RBAC-style
  `CloudShell.Resources/resources/lifecycle/action` operation permission.
  Custom actions can declare narrower Azure-style operation permissions and
  otherwise use `CloudShell.Resources/resources/actions/execute/action`.
  `resources.manage` remains a compatibility superset for resource actions.
- Resource identity provider selection now has a catalog abstraction. Concrete
  provider bindings resolve by provider ID; required-but-unresolved bindings
  resolve to the configured default provider, with a single registered provider
  used as the implicit default. Control Plane hosts can register providers and
  the default through `ResourceIdentity` configuration, and programmatic
  declarations can register provider definitions and select a default provider
  with `AddIdentityProvider(...)` and `UseDefaultIdentityProvider(...)`.
  Unresolved identity providers are reported through resource model diagnostics.
  First-class identity-provider resources, resource-group or parent-resource
  inheritance, durable external authority registration, and provider-backed
  managed identity lifecycle remain future resource identity work.
- Settings and secrets are being split into explicit reference-backed resource
  configuration. Application resources now have app-setting metadata,
  configuration-entry references for non-secret settings, and secret-reference
  placeholders for vault-backed values while secret storage remains provider
  owned.
- Host applications can explicitly expose selected `IConfiguration` entries
  through host-configuration source resources for development scenarios. These
  sources resolve through the same configuration-entry reference path as
  configuration stores and do not expose the entire host configuration surface.
- Split the provider-owned runtime service names around product boundaries:
  `CloudShell.ConfigurationStoreService` serves configuration-store entries,
  and `CloudShell.SecretsVaultService` serves Secrets Vault secrets.
- Environment-variable assignment is a resource capability, not an
  application-only UI feature. Resources that advertise the capability can use
  the shared Resource Manager environment tab to assign literal values,
  configuration-entry references, or Secrets Vault references through a
  provider-owned configuration contract.
- Application resource templates preserve reference-backed app settings and
  environment variables by carrying configuration-entry references and Secrets
  Vault references without embedding secret values.
- Secrets Vault registration is available through a separate
  `AddSecretsProvider()` path, while `AddConfigurationProvider()` keeps
  compatibility by registering both configuration stores and Secrets Vault
  support unless the Secrets provider is already registered.
- Orchestrator-specific services, backends, deployments, and runtime
  containers are implementation details below the stable container app
  resource. The app exposes image/revision and replica desired state; providers
  map that state to Docker Compose, Kubernetes, the default local runner, or
  another runtime without exposing those implementation objects as Resource
  Manager targets.
- The default orchestrator now owns replica instance fan-out for container app
  services, and load-balancer route resolution can expand a port-based route to
  a replicated container app into convention-named backend targets for Traefik
  file-provider output.
- The default Docker-backed container app runner now places app instances on a
  shared user-defined Docker network so convention-named replica containers can
  be resolved by provider-owned runtime infrastructure such as Traefik. The
  Traefik provider can optionally manage a provider-owned runtime container on
  the selected Docker host. Managed load-balancer resources now expose standard
  Start/Stop lifecycle actions, persist provider-owned runtime state, apply the
  latest dynamic configuration during Start, and ask the provider to clean
  runtime state during resource Delete. Apply remains the configuration
  reconciliation action.
- Replicated container apps now own app-specific ingress for the default path.
  The default Docker runner starts a provider-owned Traefik ingress container
  automatically during app start/restart for replicated HTTP/TCP endpoints, and
  the Docker Compose generator renders a Traefik sidecar plus labels for
  replicated services with published HTTP/TCP ports. Explicit
  `cloudshell.loadBalancer` resources remain the higher-control gateway
  scenario rather than the normal app endpoint path.

#### Samples

- Added a Settings and Secrets sample for the resource-assignment path: a
  programmatically declared Web API resource receives environment variables
  from configuration-entry and Secrets Vault references, provisions its
  resource identity, and reads provider-backed Configuration Store and Secrets
  Vault services with a bearer token from the built-in authority.
- Added a Settings and Secrets sample that demonstrates assigning
  configuration-entry and Secrets Vault references to a Web API resource's
  environment variables and using a provisioned resource identity to read the
  backing services without service auth secrets.

#### Documentation

- Resource operation permissions must be documented per resource type or class
  as they are added. Network endpoint reconciliation now uses
  `CloudShell.Network/networks/reconcileEndpointMappings/action`, and
  load-balancer configuration apply now uses
  `CloudShell.Network/loadBalancers/applyConfiguration/action`.
  Common operation constants live in `CommonResourceOperationPermissions`;
  resource-type-specific operation constants live in dedicated classes such as
  `NetworkResourceOperationPermissions` and
  `LoadBalancerResourceOperationPermissions`.

### 2026-06-09

#### Added

- Network resources now distinguish host, logical, and virtual network kinds.
  When no network is created, the platform projects a default host network.
  Virtual networks reuse endpoint requests and mappings while advertising
  virtual-network and ingress capabilities.
- Docker now projects configured local and remote Docker runtime connections as
  `docker.host` container host resources. UI language uses container host,
  while `container.host` remains the future generic resource-type direction for
  non-Docker providers.
- The first load-balancer implementation slice adds a platform load-balancer
  resource model, fluent route declarations, API/client projection, generated
  Resource Manager route display, an apply-configuration resource action, and a
  Traefik file-provider implementation that writes dynamic HTTP/TCP
  configuration from stable resource routes.
- Added Docker host definitions for local and remote endpoints, safe host
  endpoint projection, per-host Docker clients, remote host builder APIs, and
  group-scoped duplicate Docker host validation.
- Added host/logical/virtual network primitives, an `AddVirtualNetwork(...)`
  declaration helper, and a replaceable host-local network environment for
  default endpoint assignment across Windows, macOS, and Linux.

#### Changed

- Container app and Docker host configuration UI exposes registry settings,
  and container app details show the latest projected revision.
- Network resources project endpoint mappings as first-class resource data.
  Resource Manager shows mappings on the network resource and read-only network
  exposure on mapped target resources, instead of treating exposure as a
  dependency or encoded attribute.
- Platform-owned network, service, and load-balancer endpoint assignments are
  validated for concrete host/port socket conflicts before registration,
  including conflicts where two endpoints use different protocol labels for
  the same local socket. The same create path now runs an advisory local
  host-port availability preflight so dangling external processes or
  containers fail fast with a stable Resource Manager error instead of
  surfacing only as a later bind failure. Endpoint mapping reconciliation also
  validates that mapping sources belong to the reconciled network and are not
  reused across multiple mappings.
- Provider-owned resources can create and manage implementation containers as
  runtime state or child resources without becoming container app resources.
  The stable resource, such as a load balancer, owns the user-facing lifecycle.
  Decision: [ADR-20260609-002](ADR.md#adr-20260609-002).
- `IResourceManager` publishes coarse `ResourcesChanged` notifications after
  resource-manager mutations. Resource Manager listens for those notifications
  and also polls the inventory so provider-discovered changes, such as runtime
  containers appearing or status changing outside CloudShell, update visible
  resource rows without manual refresh.
- Defined artifact implementation guidelines for resource-model artifacts,
  including ownership, projection, API/client mapping, provider boundaries, UI
  responsibilities, end-to-end resource type implementation, and verification
  expectations.

#### Fixed

- Added platform endpoint assignment conflict validation for network, service,
  and load-balancer resources, plus endpoint mapping source ownership and
  duplicate-source validation during reconciliation.
- Added host-readiness projection for default virtual networks and Resource
  Manager settings warnings when a virtual network is running in logical-only
  host-local mode.

#### Samples

- The load-balancer sample declares a selected container host, mock web/API/TCP
  container-app targets, and a Traefik-backed public load balancer. Its smoke
  test invokes the advertised apply action and verifies the generated dynamic
  configuration file.

### 2026-06-08

#### Added

- Added a remote `IControlPlane` implementation for split hosting.
- Added remote Control Plane authentication coverage.
- Added internal Control Plane resource-state tests.
- Added resource action capability modeling.
- Added hypermedia resource actions to API resource responses.
- Added Resource Manager projection coverage for registered roots, dynamic
  children, declaration-assigned parents, group inheritance, and parent graph
  cycle safety.
- Added delete/action contract-error coverage for missing resources, missing
  actions, unsupported providers, permission denial, dependent warnings, and
  delete capability alignment.
- Added client API helpers for canonical resource action IDs, resource action
  lookup, capability lookup, and manager-driven lifecycle action execution.
- Added a user-scoped CloudShell environment settings provider with selectable
  local or Control Plane-backed storage and theme/navigation preference
  integration.
- Added uniform resource attributes for class-defining, non-secret provider
  details such as workload kind, image, endpoint count, service port count, and
  configuration entry count.
- Added `ResourceClass` filtering to resource queries, the Control Plane API,
  and the remote client.
- Added generic declaration metadata for `ResourceClass` and non-secret
  attributes, and projected that metadata through Resource Manager overlays.
- Added `ResourceClass` and non-secret attribute metadata to resource creation
  commands, HTTP requests, the remote client, and provider creation requests.
- Added generated Resource Manager detail views for resources without
  provider-owned detail routes, tabs, or update components.
- Added first-class dependency auto-start failure details with a stable
  `dependencyAutoStartFailed` Control Plane error code, dependency path, blocked
  dependency, and concrete failure reason.
- Added explicit start-after-create support for resource creation commands and
  runnable application registration UI, with provider policy carrying the
  default checkbox intent.
- Added a domain image update command for top-level container app resources,
  exposed through a Container Apps revision API rather than a resource-specific
  core Resource Manager route, with actor-attributed resource events for
  traceability, application-provider console logs for underlying container
  output, split-host client mapping, and documented registry-push deployment
  procedure.
- Added a Resource Manager overview deployment affordance for container app
  resources that updates the image through the domain `UpdateResourceImageAsync`
  operation and refreshes the projected image/revision.
- Added resource capability projection, networking capability identifiers,
  typed endpoint requests, endpoint mapping definitions, and built-in
  `cloudshell.network` builder helpers for manual or auto localhost endpoint
  assignment.
- Added Resource Manager network registration UI support for manual,
  auto-assigned, provider-default, and predefined endpoint requests.
- Added a shared endpoint assignment UI component and reused it across network,
  service, container image, SQL Server, and ASP.NET Core project registration.
- Added endpoint mapping provider selection for network declarations, a
  platform reconcile action that validates source, target, and mapper
  capabilities, and remote Control Plane contract coverage for invoking it.

#### Changed

- The WebUI is the shell surface; the Control Plane is a separately deployable
  service boundary.
- Resource actions are domain operations on resources, not UI actions.
- Resource API responses expose resource actions as keyed hypermedia
  affordances.
- Resource action capabilities are separate signals that describe current
  executability and reasons.
- Provider-owned resource configuration stays separate from platform-owned
  registration/group state.
- Projected resources use one uniform `Resource` shape. Broad behavior is
  modeled with `ResourceClass`, precise identity with `TypeId`, non-secret
  structural facts with `Attributes`, and runtime behavior through
  provider-owned descriptors instead of resource subclasses.
- Programmatic resource builders are declaration-time abstractions that create
  uniform resources and provider-owned configuration; executable, project, and
  container builders expose different authoring conveniences without becoming
  runtime resource types.
- Common executable, project, and container workload builder contracts live in
  `CloudShell.Abstractions`; provider packages own the concrete factory methods
  and implementations that populate provider-specific configuration.
- ASP.NET Core project resources are project-shaped resources with a
  provider-owned process runner; they do not project executable command
  attributes even though the provider starts them through `dotnet`.
- Resource declaration builder APIs use concise resource-oriented names such as
  `IResourceDeclarationBuilder` and `IResourceBuilder` instead of repeating the
  CloudShell product prefix.
- CloudShell environment preferences are user-scoped, workload-agnostic, and
  use one configured storage backend: local UI-host storage or Control
  Plane-backed storage.
- Top-level container app resources own deployment operations such as image
  updates. Container-host providers such as Docker may project runtime
  container resources for inspection, but consumers should not need those
  runtime resource IDs to deploy a new app image.
- Resource-scoped events are the platform traceability stream for operations
  performed on resources, including who or what triggered the operation.
  Standard lifecycle action events such as `action.lifecycle.start` and
  `action.lifecycle.stop` are separate from resulting lifecycle events such as
  `event.lifecycle.starting`, `event.lifecycle.started`, `event.lifecycle.stopping`, and
  `event.lifecycle.stopped`. Both are recorded on the resource whose action or
  lifecycle changes, including dependencies that are auto-started because
  another resource was started. Authors may define custom namespaced actions
  and events; only standard lifecycle action kinds receive Resource Manager
  lifecycle events automatically. The proposed lifecycle orchestration model is
  tracked in [Lifecycle orchestration](docs/proposals/core/lifecycle-orchestration.md)
  so future extension points and event-triggered workflows build on the same
  deterministic action procedure rather than replacing it.
  Resource-type logs remain available for operational detail such as container
  console output.
- Container app image deployments create and project a new app-owned revision;
  runtime container instances/replicas implement a revision but do not define
  the stable revision identity.
- Container app resources and Docker resources can specify a non-secret
  container registry value, projected as `container.registry`; both default to
  Docker Hub (`docker.io`). Registry credentials are provider-owned
  configuration and use a username plus password environment variable
  reference instead of projecting secrets through resource attributes.
- Networking is modeled through resources and capabilities. Resources can
  advertise endpoint-source and networking-provider capabilities; network
  resources can reserve or auto-assign endpoint requests and record endpoint
  mappings while richer networking behavior remains provider-owned.
- Removed legacy `actions` API compatibility from resource responses.
- Clarified that `CloudShell.Abstractions` is the cloud-plane client API and
  that projected resources expose action discovery while managers execute
  commands.
- Renamed the projected domain entity from `CloudResource` to `Resource` and
  added `ResourceClass` projection through in-process resources, the Control
  Plane API, and the remote client.
- Moved executable and project workload builder contracts into
  `CloudShell.Abstractions` alongside the existing container builder contract.
- Renamed the common programmatic resource builder contracts to
  `IResourceBuilder` and `IResourceDeclarationBuilder`.
- Separated ASP.NET Core project declaration and projection from executable
  command details, while preserving project app arguments, environment
  variables, endpoints, service discovery, and process-backed runtime behavior.
- Improved generated Resource Manager detail views with related-resource links,
  endpoint copy/open affordances, health metadata, logs, observability links,
  and action capability reasons.
- Defined resource attribute conventions: dotted lower-camel names,
  string-only non-secret values for MVP, invariant formatting, generated
  display behavior, and provider-specific prefix guidance.
- Split declaration startup autostart from dependency autostart:
  programmatic declarations now use startup autostart semantics with provider
  defaults, while dependency startup uses `WithDependencyAutoStart(...)` and the
  same provider/default precedence.
- Aligned OpenAPI output with the domain-shaped resource projection for
  resources, action affordance dictionaries, attributes, and creation options.
- Reused the shared endpoint assignment UI for executable application
  registration so the built-in registration flows expose consistent endpoint
  assignment controls.

#### Fixed

- Added API boundary validation and invalid-payload contract tests.
- Added direct `IResourceManager` validation for resource creation,
  registration, group assignment, and dependency updates.
- Added contract-level Control Plane errors with API `ProblemDetails` code
  projection and remote client mapping.
- Added resource model class consistency validation for creation requests,
  provider projections, and declaration metadata, with result/diagnostic-based
  model validation.
- Aligned resource template import with the uniform resource validation model:
  invalid template envelopes now return diagnostics without creating resource
  groups or throwing from the domain API.

#### Samples

- Added split-hosting and sample smoke tests.
- Expanded the ResourceHost sample to exercise provider-backed resource
  actions through advertised hypermedia hrefs.
- Grouped sample projects in the solution by sample scenario so logical
  solution folders match the physical `samples/` layout.
- Added a Container App Deployment sample with a local registry resource,
  stopped mock container app, and `sh` deployment script that simulates a build
  by posting a new image tag to the Container Apps revision API.

#### Documentation

- The domain model should be documented across product concepts, public
  abstractions, internal Control Plane services, provider contracts, API
  projection, and UI projection.
- Split application resource documentation into a `docs/resources` area with
  separate pages for executable applications, ASP.NET Core project resources,
  and container apps.
