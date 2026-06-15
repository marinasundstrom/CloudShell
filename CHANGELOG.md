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

### 2026-06-15

#### Added

- Added a portable local host networking provider resource, endpoint-mapping
  provisioner contract, Resource Manager UI readiness/provider display, and a
  Host Virtual Network sample.

#### Changed

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
