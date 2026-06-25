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

### 2026-06-25

#### Changed

- Docker container reference resources now mark `endpoints.count` as read-only
  provider-projected state, so deployment definitions cannot author endpoint
  counts while resolved Resource projections still expose the default count.
- ResourceDefinition rendering and validation now treat read-only attributes as
  outside the interchange document surface: read-only Resource state can be
  resolved, but authored and rendered ResourceDefinition attributes omit it.
- Configuration store, host configuration source, load balancer, and Secrets
  Vault reference providers now mark count attributes as read-only provider
  facts instead of ResourceDefinition-authored desired state.
- Resource attributes now carry `ResourceAttributeMutability`, allowing the
  POC to mark read-only count attributes as provider-managed while preserving
  `ReadOnly` as the caller-write enforcement rule.
- Resource change apply now permits provider apply results to update read-only
  attributes only when the resolved attribute is provider-managed, and keeps
  those accepted provider-managed values out of rendered ResourceDefinition
  interchange output.
- ResourceDefinition filtering now uses effective class/type attribute
  metadata, so provider-managed read-only values are omitted even when the
  attribute has no default value in the resolved projection.
- Resource graph references can now declare an expected resource type, and
  graph resolution reports a diagnostic when a resource-ID reference resolves
  to a resource with a different `ResourceTypeId`.
- Provider-produced graph dependencies now preserve typed reference diagnostics
  during dependency-closure resolution, and the volume-consumer dependency
  provider emits expected local-volume references.
- Resource Manager bridge projections now use graph-reference resolution for
  provider-produced dependencies, keeping missing staged dependencies visible
  while filtering and diagnosing existing targets with the wrong resource type.
- Resource Manager bridge reference resolution now binds capability and
  operation projections only for successfully resolved references, while still
  returning wrong-type targets for diagnostics and debugging.
- Resource Manager bridge dependency projection now derives dependency IDs and
  typed-reference diagnostics from the same graph-reference resolution pass.
- Resource Manager bridge operation availability and execution now block on
  typed graph-reference mismatches, without turning broader dependency
  validation into procedure policy.
- Graph dependency resolution now lets provider-produced typed references
  refine older untyped dependency declarations for the same resource ID.
- The executable application start operation now delegates runtime-facing start
  behavior to an injected provider-owned runtime controller with a no-op POC
  default.
- ASP.NET Core project start/restart operations now use a provider-owned
  process runtime controller by default, keeping runtime execution behind the
  new provider seam without adapting old application-provider concepts into the
  Resource model.
- ASP.NET Core project process runtime command construction now lives in a
  provider-local command factory with focused tests for graph-backed project
  path, arguments, hot-reload, and Resource model environment variables.
- The ASP.NET Core project process runtime controller now cleans up tracked
  child processes when the DI container or host disposes the provider service.
- The ProjectReference sample now registers a graph-backed ASP.NET Core
  project resource through the Resource model bridge provider, giving the POC a
  concrete host path for starting a project resource through the new provider
  model.
- Resource Manager bridge projections now mark graph resources with lifecycle
  operations as `Unknown` lifecycle state, allowing registered graph-backed
  resources to dispatch Start through the graph procedure provider.
- The Resource model proposal now records the POC objective that existing
  providers are behavioral references, while the new model should resolve old
  inconsistencies through consistent graph attributes, provider-owned
  capabilities/operations, apply hooks, and Resource Manager dispatch.
- The Resource model proposal now tracks future cleanup work for redundant
  resolved-model concepts, old-provider adapter leakage, graph lifecycle-state
  projection, premature graph context/transaction APIs, and compatibility
  bridge layers that do not fit the new provider seams.
- The Resource model proposal now records the revised POC plan and progress
  tracker, focusing on one graph-backed ASP.NET Core project replacement path
  before broad provider porting continues.
- The local host networking reconcile operation now delegates endpoint-mapping
  work to an injected provider-owned reconciler with a no-op POC default,
  matching the provider-integration pattern without moving runtime logic into
  the Resource model.
- The virtual-network reconcile operation now delegates endpoint-mapping work
  to an injected provider-owned reconciler with a no-op POC default, keeping
  virtual network materialization behind the provider boundary.
- The generic network reconcile operation now delegates endpoint-mapping work
  to an injected provider-owned reconciler with a no-op POC default.
- The macOS host networking reconcile operation now delegates endpoint-mapping
  work to an injected provider-owned reconciler with a no-op POC default.
- The load balancer apply-configuration operation now delegates runtime
  materialization work to an injected provider-owned applier with a no-op POC
  default.
- The DNS zone reconcile name-mappings operation now delegates runtime DNS
  materialization work to an injected provider-owned reconciler with a no-op
  POC default.
- The service reconcile operation now delegates runtime endpoint/service
  materialization work to an injected provider-owned reconciler with a no-op
  POC default.
- The local volume provision operation now delegates runtime volume
  materialization work to an injected provider-owned provisioner with a no-op
  POC default.
- The CloudShell volume provision operation now delegates storage-backed
  runtime volume materialization work to an injected provider-owned
  provisioner with a no-op POC default.
- The storage inspect operation now delegates runtime storage inspection to an
  injected provider-owned inspector with a no-op POC default.
- The Docker host inspect operation now delegates runtime Docker host
  inspection to an injected provider-owned inspector with a no-op POC default.
- The generic container host inspect operation now delegates runtime host
  inspection to an injected provider-owned inspector with a no-op POC default.
- The configuration store inspect operation now delegates runtime
  configuration inspection to an injected provider-owned inspector with a
  no-op POC default.
- The Secrets Vault inspect operation now delegates runtime vault inspection
  to an injected provider-owned inspector with a no-op POC default without
  storing secret values in the Resource graph.
- The host configuration source inspect operation now delegates runtime host
  configuration lookup to an injected provider-owned inspector with a no-op
  POC default.
- The identity provisioning setup operation now delegates runtime identity
  provider setup to an injected provider-owned handler with a no-op POC
  default.
- The SQL Server reconcile-access operation now delegates runtime database
  access reconciliation to an injected provider-owned reconciler with a no-op
  POC default, and the proposal records provider-local `Runtime/`,
  `Operations/`, and `Capabilities/` folder boundaries.
- The SQL database ensure-created operation now delegates runtime database
  materialization to an injected provider-owned creation handler with a no-op
  POC default.
- SQL Server reference-provider operation code now lives under the provider
  `Operations/` folder, keeping runtime seams under `Runtime/`.
- SQL database reference-provider operation code now lives under the provider
  `Operations/` folder, keeping database materialization seams under
  `Runtime/`.
- Host configuration source operation code now lives under `Operations/`, and
  the runtime inspector seam lives under the provider `Runtime/` folder.
- Identity provisioning operation code now lives under `Operations/`, and the
  runtime setup-handler seam lives under the provider `Runtime/` folder.
- Configuration store operation code now lives under `Operations/`, and the
  runtime inspector seam lives under the provider `Runtime/` folder.
- Secrets Vault operation code now lives under `Operations/`, and the runtime
  inspector seam lives under the provider `Runtime/` folder.
- Storage operation code now lives under `Operations/`, and the runtime
  inspector seam lives under the provider `Runtime/` folder.
- Local volume and CloudShell volume operation code now lives under
  provider-local `Operations/` folders, and their runtime provisioner seams
  live under provider-local `Runtime/` folders.
- Generic container host and Docker host operation code now lives under
  provider-local `Operations/` folders, and their runtime inspector seams live
  under provider-local `Runtime/` folders.
- Service, DNS zone, network, virtual network, host networking, and load
  balancer operation code now lives under provider-local `Operations/`
  folders, and their runtime reconcile/apply seams live under provider-local
  `Runtime/` folders.
- Executable application, ASP.NET Core project, container application, and
  Docker container operation code now lives under provider-local `Operations/`
  folders, and the executable runtime-controller seam lives under
  `ExecutableApplication/Runtime/`.
- Provider-local graph validators now live under `Validators/` folders, while
  shared capability validators remain under their shared capability boundary.
- Provider-local graph dependency providers now live under `Dependencies/`
  folders, and the proposal documents that shared capability dependency
  providers stay with their shared capability.
- Provider-owned configuration records now live under provider-local
  `Configuration/` folders.
- ASP.NET Core project start/restart operations now delegate runtime behavior
  to an injected provider-owned runtime controller with a no-op POC default.
- Resource graph records now have test coverage proving typed
  `ResourceReference` dependencies survive the record persistence projection.
- `ResourceReference` now has JSON round-trip coverage for expected resource
  type and provider metadata.
- Resource model provider integration tests now include an
  ApplicationTopology-inspired graph that composes local volume, SQL Server,
  SQL database, configuration store, Secrets Vault, and executable application
  resources through the Resource Manager bridge.
- Resource model provider integration tests now also cover an
  ApplicationTopology-inspired exposure graph across container application,
  network, service, DNS zone, and name-mapping reference providers.
- Resource model provider integration tests now cover the ApplicationTopology
  ASP.NET Core project workload shape with typed references to SQL database,
  configuration store, and Secrets Vault resources.
- Resource model provider integration tests now include a SettingsAndSecrets
  sample-inspired graph across identity provisioning, configuration store,
  Secrets Vault, and ASP.NET Core project reference providers.
- Resource Manager store projection tests now cover persisted Resource model
  records for the SettingsAndSecrets-shaped graph, proving the bridge can
  project identity, configuration, secrets, and project resources from stored
  graph records.
- Resource Manager orchestration tests now route persisted SettingsAndSecrets
  graph-record operations through the Resource model procedure bridge for
  identity setup and configuration/secrets inspection.
- Resource Manager orchestration tests now block persisted SettingsAndSecrets
  graph-record operations when a typed identity dependency resolves to the
  wrong resource type.
- Resource Manager store projection tests now report persisted
  SettingsAndSecrets graph-record diagnostics and omit invalid typed identity
  dependencies from actionable Resource Manager dependency lists.
- Resource attribute definitions now separate a small `ValueType` contract
  from complex `ValueShape` metadata, keep reusable shapes local to the owning
  class/type definition, and declare collection intent with optional min/max
  size expectations.
- Resource definition default validation now resolves locally reusable
  attribute value shapes when checking complex defaults and collection size
  expectations.
- `ResourceAttributeValue` can now map concrete CLR objects such as
  `ResourceReference` into structurally navigable attribute values and back
  again.
- The SQL database reference provider now declares `database.server` as a
  read-only, provider-managed `ResourceReference` attribute for the owning SQL
  Server relationship.
- SQL database validation now rejects caller-authored values for
  `database.server`; the current POC still uses existing `DependsOn` inputs as
  temporary validation plumbing, but not as the long-term ownership model.
- The SQL database typed wrapper now projects its owning server as a
  `belongsTo` `ResourceReference`, keeping ownership separate from startup
  dependency traversal.
- Resource references now distinguish generic `resourceId` addressing from
  `dependsOn` dependency semantics so future `belongsTo` references can be
  resolved without becoming startup dependencies.
- `ResourceReference` now has explicit `DependsOnResourceId` and
  `BelongsToResourceId` factories so the current POC qualifier is visible at
  call sites.
- Current dependency providers, Resource model tests, and Control Plane
  projection tests now use the explicit `DependsOnResourceId` factory instead
  of relying on the generic `ResourceReference` default relationship.
- The resource definitions proposal now clarifies that `ResourceReference` is
  an addressing primitive that may carry a relationship qualifier in the POC,
  not a complete relationship model on its own.
- The resource definitions proposal now marks service, load-balancer, and
  name-mapping target dependencies as temporary POC encodings pending concrete
  provider-specific reference requirements.
- Resource definitions now expose `StartupDependencies` and
  `StartupDependencyIds` aliases for the historical `DependsOn` record field
  so internal code can distinguish orchestrator startup metadata from broader
  references.
- The generic `ResourceReference.ResourceId` factory now requires an explicit
  qualifier instead of defaulting to `dependsOn`.
- The Resource Manager graph resource provider now projects only `dependsOn`
  references into Resource Manager dependency lists, leaving ownership
  references out of startup-order metadata.
- Resource Manager store projection tests now cover persisted Resource model
  records for an ApplicationTopology-shaped graph, proving the bridge can
  project stored graph records alongside Resource Manager registrations.
- The resource definitions proposal now clarifies that versioned Resource
  graph state must map to graph primitives, while runtime-only and Control
  Plane operational state stay outside the graph unless deliberately promoted
  to provider-managed attributes.
- The resource definitions proposal now clarifies that a Resource graph record
  may be stored beside the Control Plane resource record, while the resolved
  `Resource` remains a short-lived working projection over stored graph state.
- The resource definitions proposal now narrows the POC scope to stored
  graph-state records and projection-on-demand, avoiding new graph context,
  session, transaction, or control-service abstractions until Resource Manager
  integration proves they are needed.
- The resource definitions proposal now records the near-term POC path:
  stabilize the current model enough to port real provider behavior, and
  propose new abstractions only when provider ports expose concrete gaps.
- The resource definitions proposal now clarifies that resource type providers
  are integration points that may receive injected services, but should not own
  recurring runtime tasks, watchers, polling loops, or reconciliation schedulers
  in the POC.
- The Resource definitions POC removed the experimental graph transaction and
  exclusive-lock APIs, keeping graph versions, change tracking, and commit
  contexts as the minimal write boundary while the proposal refocuses
  integration on custom projection from Resource Manager operational records.
- Resource model provider integration tests now include a HostVirtualNetwork
  sample-inspired graph across local host networking, virtual network, and
  ASP.NET Core project reference providers, keeping the next POC path focused
  on simpler networking resources before container app orchestration.
- Name mapping reference-provider validation now requires mappings to compose
  a DNS zone reference and a target resource reference, keeping the simpler
  networking provider path aligned with the DNS/name-mapping proposal.
- The resource definitions proposal now frames the POC as a resource graph and
  configuration model: stored graph/configuration state lives in the model,
  while capabilities and operations are behavior integration points over that
  state.
- CloudShell volume reference-provider validation now requires volume
  dependencies to resolve to `cloudshell.storage` resources, keeping storage
  relationship semantics in the provider boundary instead of generic graph
  infrastructure.
- Service reference-provider validation now treats typed network dependencies
  as provider-owned graph semantics and rejects service definitions whose
  network reference resolves to a non-network resource.
- Load balancer reference-provider validation now keeps declared host/backend
  dependencies as typed `ResourceReference` entries and rejects network or
  infrastructure provider resources when they are used as backend targets.
- Resource model provider integration tests now include a ContainerHost
  sample-inspired graph across storage, CloudShell volume, and SQL Server
  reference providers, and volume-consumer graph validation now accepts both
  direct local volumes and storage-backed CloudShell volumes.
- SQL Server reference-provider coverage now includes optional typed
  container-host references, validating selected container hosts through the
  SQL Server provider boundary while leaving default/preferred host resolution
  to later Resource Manager integration.
- The resource definitions proposal now records centralized versus
  distributed graph storage as a future projection concern, keeping the POC
  focused on the logical Resource model and provider integration contracts.
- Container application and SQL Server reference providers now accept typed
  Docker host references as container-host bindings when the resolved host
  advertises the required container-image capability.
- Container application reference resources now model `container.registry`,
  and integration coverage includes a ContainerAppDeployment-inspired Docker
  host, registry container, and container app graph.
- The resource definitions proposal now includes a Resource model layer-stack
  diagram that separates integrations, behavior, resolved projections,
  interchange, state records, future transactions, and persistence.

### 2026-06-24

#### Changed

- The resource definitions POC now includes a resource definition graph and
  deployment definition shape so proposed deployments can carry desired
  resource state before providers validate and apply it.
- The resource definitions POC now includes resource definition apply planning
  so validated graphs can resolve resource type apply providers and return
  explicit definition/runtime materialization steps before mutation.
- The resource definitions POC now includes a string-keyed
  `ResourceDefinitionRecord` persistence projection that rehydrates into the
  domain `ResourceDefinition` before validation and provider behavior.
- The resource definitions POC now includes a record-backed in-memory resource
  state provider, proving that Resource Manager bridge projections can resolve
  from stripped `ResourceRecord` persistence data instead of storing resolved
  Resource model projections.
- The resource definitions POC now has an end-to-end model flow test covering
  document serialization, persistence projection, graph validation,
  type-specific projection, capability resolution, and apply planning.
- The resource definitions POC now includes graph-to-resource projection
  resolution so validated definitions can be listed as generated-style upper
  domain wrappers without deciding the final `IResourceProvider` contract.
- The resource definitions POC now removes the redundant
  `ResourceDefinitionProjection` wrapper so capability providers, operation
  providers, apply planning, and typed resource wrappers operate on resolved
  `Resource` projections, while `ResourceDefinition` remains the interchange
  document applied to or rendered from resource state.
- The resource definitions POC now binds projected capability and operation
  work units to their owning `Resource`, adds a `ResourceOperationResolver`,
  and exposes projected behavior through `Resource.Capabilities.Get<T>()` and
  `Resource.Operations.Get<T>()` so wrappers can resolve volume capability
  behavior and start operation behavior from the same resource projection.
- The resource definitions POC now gives projected capabilities and operations
  a resource-local execution context, so they can create scoped resource
  changes from the target resource without owning graph scope or commit
  boundaries.
- Resource graph transactions now expose whether they are optimistic or
  exclusive, while projected capabilities and operations remain resource-local
  and leave graph scope, locking, apply, and commit decisions to the caller.
- The Resource model Resource Manager bridge now includes a graph resource
  resolver that resolves a resource by ID, optionally includes dependencies,
  binds registered capability and operation projections, and returns the graph
  snapshot version while leaving apply and commit policy to the caller.
- The Resource model Resource Manager bridge can now resolve a Resource
  Manager action ID to the matching Resource model operation projection,
  returning diagnostics when the operation projection is not registered instead
  of executing or owning the operation boundary.
- The Resource model Resource Manager bridge can now resolve declared
  capability IDs to registered capability projections and report diagnostics
  when the consuming boundary has not registered a capability implementation.
- The Resource definitions POC now documents and tests the current rule that
  capability and operation work units may perform integration logic but should
  stage direct Resource model graph changes only for their attached resource
  until a future scoped graph isolation model is defined.
- Resource model operation projections can now opt into a generic executable
  operation contract, giving Resource Manager and orchestrator integrations a
  provider-neutral way to check and invoke resolved operations.
- The Resource model Resource Manager bridge now has an explicit
  procedure-capable graph provider that can evaluate and execute Resource
  Manager actions by resolving executable Resource model operation projections,
  while keeping the read-only graph provider available separately.
- The procedure-capable Resource model bridge registration now wires the same
  scoped bridge instance as both an `IResourceProvider` and an
  `IResourceActionAvailabilityProvider`, so normal Control Plane composition
  can surface operation availability reasons without host-specific plumbing.
- Resource model bridge projections now carry bridge-provider metadata, and
  the procedure-capable bridge uses that metadata when evaluating Resource
  Manager actions so it does not claim unrelated provider resources with the
  same action IDs.
- Resource model graph service registration can now compose from
  provider-registered `ResourceClassDefinition` and `IResourceTypeProvider`
  services, while still allowing explicit host class-definition registrations
  to override provider defaults by class id.
- Resource definition validation now applies the same class-definition
  override rule as graph service composition, so duplicate class ids are
  resolved consistently before provider validation runs.
- The resource definitions reference providers now include a separate local
  volume resource type provider, proving a second provider boundary with its
  own class defaults, type defaults, validation, change apply handling, and
  apply planning.
- The Resource Manager bridge tests now apply a deployment with a local volume
  and executable application across separate provider boundaries, then project
  both resources through the Resource Manager bridge and resolve the executable
  dependency closure from the graph.
- The resource definitions POC now has graph-level validators that run against
  resolved proposed graph state before commit, and the reference volume
  consumer validator rejects missing or non-volume mount targets without
  moving graph scope into the resource-local capability projection.
- Resource graph dependency resolution can now compose provider-derived graph
  dependencies with explicit `DependsOn` entries, and the reference volume
  consumer provider contributes mounted volumes to dependency closure without
  duplicating those relationships into every resource definition.
- Resource definition and state dependencies now use `ResourceReference`
  objects with relationship and addressing-mode metadata instead of raw
  resource ID strings, while the current resolver only follows `dependsOn`
  references addressed by `resourceId`.
- Resource graph dependency providers now contribute `ResourceReference`
  objects as well, keeping provider-owned graph relationships on the same
  reference model as explicit `DependsOn` entries.
- The resource graph resolver can now resolve individual `ResourceReference`
  values into projected `Resource` results and exposes followed reference
  resolutions alongside dependency-closure resources.
- The resource graph resolver also keeps direct resource-id lookup as a
  first-class path for consumers that already hold a graph resource address.
- The Resource Manager bridge can now register an in-memory graph from custom
  store records through `IResourceGraphStoreProjector<TRecord>`, proving that
  Resource Manager-owned rows can keep operational fields while persisting the
  Resource model graph payload.
- Resource Manager graph resolution now carries followed resource-reference
  resolutions alongside the resolved dependency closure, preserving the
  relationship object and target resource projection for topology consumers.
- The Resource Manager graph resolver can now resolve a `ResourceReference`
  directly and bind capability and operation projections on the resolved
  resource when the reference targets a graph resource.
- The Resource Manager graph resolver now routes direct graph resource lookup
  through the core `ResourceGraphResolver`, keeping missing-resource
  diagnostics and graph lookup behavior in one place.
- Resource Manager graph resource projection now includes provider-derived
  graph dependencies, so capability-owned relationships such as volume mounts
  can appear in projected `DependsOn` without duplicating them in every
  resource definition.
- The local volume reference provider now owns a custom
  `storage.volume.provision` operation provider and typed projection wrapper,
  proving that a second provider boundary can project and execute Resource
  model operations through the Resource Manager bridge.
- The resource definitions reference providers now include a narrow container
  application resource type with image and replica attributes, start/restart
  operation providers, a typed wrapper, and shared volume-consumer capability
  support across executable and container application providers.
- The container application reference provider now owns a typed
  `container.image.update` operation projection that stages image and replica
  attribute changes on its attached resource before the provider apply hook
  accepts or rejects the proposed state.
- The reference volume-consumer capability provider now attaches to resolved
  capability declarations instead of hard-coding the resource types that may
  consume volumes, keeping capability behavior independent from concrete
  application provider implementations.
- Capability declarations without a registered capability provider are now
  accepted as passive capability markers; registered capability providers still
  validate and attach behavior when present.
- The resource definitions reference providers now include a narrow SQL Server
  resource type with version/edition attributes, declared database
  configuration, shared volume-consumer support, a reconcile-access operation
  provider, typed wrapper, and Resource Manager bridge coverage.
- The resource definitions reference providers now include a narrow ASP.NET
  Core project resource type with project path, arguments, hot reload, and
  launch-settings attributes, shared volume-consumer support, start/restart
  operation providers, a typed wrapper, and Resource Manager bridge coverage.
- The resource definitions reference providers now include a narrow SQL
  database resource type with database attributes, server `ResourceReference`
  graph validation, an ensure-created operation provider, typed wrapper, and
  Resource Manager bridge coverage.
- The resource definitions reference providers now include a narrow container
  host resource type with host kind/endpoint/registry/default attributes,
  passive container image/build/filesystem-mount capability markers, an
  inspect operation provider, typed wrapper, and Resource Manager bridge
  coverage.
- Reference provider implementations now keep provider-owned configuration
  records and operation provider services in separate files next to the owning
  resource type provider, keeping type providers focused on definition shape,
  validation, and apply planning.
- `ResourceAttributeDefinition` now carries read-only metadata, resolved
  attributes preserve that effective flag, unset type metadata inherits the
  class-level read-only policy, and Resource model apply rejects
  caller-authored create/update changes for read-only attributes before
  dispatching to type-specific apply providers.
- The container application reference provider now validates optional
  container-host placement references declared as typed `ResourceReference`
  dependencies, and the proposal records `database.server` as the future
  structured `ResourceReference` attribute replacing
  `database.serverResourceId`.
- The resource definitions reference providers now include a narrow
  configuration store resource type with endpoint and entry-count attributes,
  an inspect operation provider, typed wrapper, and Resource Manager bridge
  coverage, proving a non-application provider boundary in the POC.
- The resource definitions reference providers now include a narrow host
  configuration source resource type with source and entry-count attributes,
  an inspect operation provider, typed wrapper, and Resource Manager bridge
  coverage without storing host configuration values in Resource model state.
- The resource definitions reference providers now include a narrow identity
  provisioning resource type with provider attributes, a passive
  identity-provisioning capability marker, a setup operation provider, typed
  wrapper, apply planning, and Resource Manager bridge coverage.
- The resource definitions reference providers now include a narrow local host
  networking resource type with host-readiness, OS, and networking-mode
  attributes, passive networking capability markers, an endpoint-mapping
  reconcile operation provider, typed wrapper, apply planning, and Resource
  Manager bridge coverage without persisting live mapping counts as graph
  attributes.
- The resource definitions reference providers now include a narrow macOS host
  networking resource type with OS-specific host networking attributes,
  passive networking capability markers, an endpoint-mapping reconcile
  operation provider, typed wrapper, apply planning, and Resource Manager
  bridge coverage while leaving platform support checks to the operational
  provider.
- The resource definitions reference providers now include a narrow Docker
  container resource type with workload, image, registry, replica, and endpoint
  count attributes, passive monitoring and log-source capability markers,
  lifecycle operation projections, typed wrapper, apply planning, and Resource
  Manager bridge coverage while leaving Docker API execution and log streaming
  to the operational provider.
- The resource definitions reference providers now include a narrow Docker
  host resource type with Docker host kind, endpoint, registry, default-host
  attributes, passive container capability markers, an inspect operation,
  typed wrapper, and Resource Manager bridge coverage.
- The resource definitions reference providers now include a narrow load
  balancer resource type with provider, host, route, entrypoint, and endpoint
  count attributes, passive networking capability markers, an
  apply-configuration operation, typed wrapper, and Resource Manager bridge
  coverage.
- The resource definitions reference providers now include a narrow network
  resource type with kind, host-readiness, and mapping-provider attributes,
  passive networking capability markers, a reconcile-endpoint-mappings
  operation, typed wrapper, and Resource Manager bridge coverage, while the
  proposal clarifies that calculated or fetched views should live on resolved
  capability members or operation plans instead of normal attributes.
- The resource definitions reference providers now include a narrow DNS Zone
  resource type with zone/provider attributes, a passive DNS-zone capability
  marker, a reconcile-name-mappings operation, typed wrapper, and Resource
  Manager bridge coverage while keeping derived record/conflict/materialization
  summaries out of normal attributes.
- The resource definitions reference providers now include a narrow name
  mapping resource type with host/endpoint/exposure attributes,
  `ResourceReference` dependencies, a passive name-mapping capability marker,
  typed wrapper, apply planning, and Resource Manager bridge projection while
  keeping derived status and DNS publishing observations out of normal
  attributes.
- The resource definitions reference providers now include a narrow storage
  resource type with provider/medium/location attributes, passive
  storage-provider and mount-provider capability markers, an inspect
  operation, typed wrapper, apply planning, and Resource Manager bridge
  coverage while keeping volume counts and runtime availability out of normal
  attributes.
- The resource definitions reference providers now include a narrow
  CloudShell volume resource type with provider/medium/location/subpath/
  access-mode/persistence attributes, `ResourceReference` storage dependencies,
  a passive storage-volume capability marker, a type-specific
  `storage.volume.provision` operation provider, typed wrapper, apply
  planning, and Resource Manager bridge coverage while keeping runtime
  availability out of normal attributes.
- The resource definitions reference providers now include a narrow service
  resource type with service kind/routing-mode attributes, `ResourceReference`
  target/network dependencies, a passive endpoint-source capability marker,
  reconcile operation, typed wrapper, apply planning, and Resource Manager
  bridge coverage while keeping port, endpoint, and target collections out of
  normal count attributes.
- The resource definitions reference providers now include a narrow virtual
  network resource type with virtual/default/readiness/provider attributes,
  passive virtual-network and ingress capability markers, a type-specific
  `reconcileEndpointMappings` operation provider, typed wrapper, apply
  planning, and Resource Manager bridge coverage while keeping endpoint and
  mapping observations out of normal attributes.
- The resource definitions reference providers now include a narrow Secrets
  Vault resource type with endpoint and secret-count attributes, an inspect
  operation provider, typed wrapper, and Resource Manager bridge coverage
  without storing secret values in Resource model state.
- Reference resource provider files are now grouped into provider-specific
  folders, with shared capability behavior in a dedicated capability folder,
  so provider-owned constants, validators, operations, projections, and
  registration stay inside clear management boundaries.
- The resource definitions POC now tracks pending resource projection changes
  through `ResourceChangeSet`, supports explicit `ApplyChanges()`, and can
  render either full proposed or incremental `ResourceDefinition` change
  documents without implying graph-wide commit semantics.
- Applying `ResourceDefinition` changes to existing resource state now merges
  incremental interchange data into the current `ResourceState` while
  preserving persisted revision and timestamp metadata until graph commit.
- `Resource` projections can now turn incoming `ResourceDefinition` overlays
  into `ResourceChangeSet` instances so provider apply hooks and graph commits
  can validate and persist interchange-driven updates.
- Resource definition overlays now return a target-mismatch diagnostic instead
  of creating changes when the interchange definition points at another
  resource identity or type.
- The resource definitions POC now routes staged `ResourceChangeSet` instances
  through provider-owned change apply providers, so a resource type can accept
  or reject proposed projection state before any future Resource Manager or
  persistence layer treats it as committed state.
- The resource definitions POC now includes a graph-level definition change
  applier that stages incoming `ResourceDefinition` overlays against a graph
  snapshot, runs type-owned apply providers, and returns one commit-ready
  `ResourceGraphChangeSet`.
- The resource definition graph change applier now preflights incoming
  definition batches and rejects duplicate resource IDs or missing dependency
  targets before type-owned apply providers stage resource-local changes.
- The resource definition graph change applier now passes environment and
  principal context into resource resolution, allowing attribute validators to
  evaluate the same caller context used by type-owned apply providers.
- The Resource Manager bridge now exposes a definition apply service that
  applies incoming `ResourceDefinition` overlays through the graph model and
  returns staged changes plus the graph commit result for integration callers.
- The resource definitions POC now supports explicit create-missing behavior
  for deployment-definition apply flows, representing new resources as graph
  change sets and routing their initial state through type-owned apply
  providers before commit.
- The resource definitions proposal now documents a store-backed graph
  projector option where Resource Manager-owned resource records can carry the
  Resource model graph payload and hydrate it through the same graph boundary.
- The resource definitions POC now includes a generic graph store projector
  and in-memory projected state provider, proving that Resource Manager-owned
  records can preserve operational fields while JSON graph payloads are loaded
  and committed through the Resource model boundary.
- The resource definitions POC now groups accepted resource changes into a
  versioned resource graph change set and proves persistence through an
  in-memory state provider that commits all accepted resource states under one
  graph version.
- The resource definitions POC now includes an in-memory `ResourceGraphModel`
  for server-hosted graph state that stays synchronized with the state
  provider through explicit reload and commit boundaries.
- The resource definitions POC now distinguishes graph version from persisted
  resource revision, with `ResourceRevision` mapped through the existing
  serialized resource `Version` field and advanced only for committed changed
  resources; committed resources also expose creation and last-modified
  timestamps through the resource projection and persistence record.
- The resource definitions proposal now documents a hybrid event-history
  direction where graph commits can append durable events for audit,
  changelog, debugging, and future replay without making pure event sourcing
  the POC source of truth.
- The resource definitions POC now returns a structured graph commit summary
  with commit status, changed resource counts, changed attribute and
  capability counts, and per-resource revision movement so consumers can act
  on committed, rejected, no-op, or stale changes.
- The resource definitions POC now refreshes the stored resource graph version
  before a server-hosted graph model commits changes, keeping per-resource
  edits staged until the graph commit boundary can reject stale state or write
  through the state provider.
- The resource definitions POC now exposes explicit graph refresh semantics on
  `ResourceGraphModel`, including full refreshes that advance the cached graph
  version and selected-resource refreshes that update resource data without
  making stale graph commits valid.
- The resource definitions proposal now documents staged changes as
  unversioned transaction proposals, with graph versions and resource
  revisions assigned only when the resource graph commit boundary accepts
  changes.
- The resource definitions POC now includes a small `ResourceGraphTransaction`
  facade that stages accepted resource changes against a graph snapshot and
  commits them once through `ResourceGraphModel`.
- The resource definitions POC now supports an opt-in exclusive graph change
  boundary that holds the in-process `ResourceGraphModel` lock until the
  boundary commits or is disposed, while the proposal keeps the final
  transaction/change-context terminology open.
- The resource definitions POC now separates graph change tracking, graph
  commit results, graph model transactions, graph state snapshots, and
  persistence providers into focused infrastructure files, and the proposal
  clarifies that Control Plane resource manager state remains a complementary
  operational model around the resource graph.
- The resource definitions proposal now clarifies that the Resource model owns
  graph structure and resolvable behavior declarations, while Resource Manager
  owns the Control Plane operational model and composes API projections from
  both models when graph-aware behavior is needed.
- The resource definitions proposal now documents the expected advantages of
  the new Resource model, including cleaner provider boundaries, lazy graph
  resolution, deliberate interchange formats, typed wrapper support, better
  persistence choices, and a safer Resource Manager replacement path.
- The resource definitions POC now includes a separate Resource Manager bridge
  project that maps resolved Resource model resources to the existing
  `CloudShell.Abstractions.ResourceManager.Resource` projection and exposes
  them through `IResourceProvider`, proving the first integration seam without
  replacing Resource Manager storage or orchestration.
- The Control Plane tests now prove the Resource Manager bridge provider can
  participate in the existing `ResourceManagerStore` composition path, so
  registered Resource model resources flow through current registration
  filtering, metadata composition, capabilities, and actions.
- The Resource Manager bridge now includes a graph-backed provider that
  resolves `ResourceGraphSnapshot` state through `ResourceResolver` at the
  provider boundary before projecting resources into the existing Resource
  Manager shape.
- The resource definitions POC now includes a graph resolver that resolves a
  target resource and its declared dependency closure from a
  `ResourceGraphSnapshot`, returning resolved resources and diagnostics for
  missing graph nodes or dependency cycles.
- The resource definitions proposal now documents identity and authorization
  hooks as Resource model graph data, such as a `principal` field or a
  structured `attributes.principal` value that identity capabilities can
  interpret while operational realization stays in Resource Manager or the
  broader Control Plane.
- The resource definitions POC now lets `ResourceClassDefinition` and
  `ResourceTypeDefinition` carry `ResourceAttributeDefinition` declarations
  for scalar default values and required-attribute rules, while keeping custom
  validation in provider or platform validator hooks.
- The resource definitions POC now adds serializer-neutral value type and
  `ResourceAttributeValueShape` metadata so attribute definitions can describe
  primitive values and complex nested attribute definitions without making JSON
  the core definition contract.
- The resource definitions POC now represents `ResourceClassDefinition` and
  `ResourceTypeDefinition` attributes as definition maps keyed by attribute
  ID, keeps `ResourceDefinition` attributes as value maps, and documents `:`
  as the stable ID namespace separator with `.` reserved for local hierarchy.
- The resource definitions POC now validates class/type attribute default
  values against declared `ResourceAttributeValueShape` metadata, returning
  diagnostics for mismatched scalar kinds and missing required object fields.
- The Resource Manager bridge for the resource definitions POC now exposes
  resolved Resource model diagnostics through the existing
  `GetResourceModelDiagnostics()` Control Plane store surface.
- The Resource Manager bridge for the resource definitions POC now includes
  `IServiceCollection` registration helpers so hosts can register a graph
  backed Resource model provider through the existing `IResourceProvider`
  composition path.
- The Resource Manager bridge for the resource definitions POC now includes a
  DI helper that builds `ResourceResolver` from registered class definitions,
  `IResourceTypeProvider` implementations, and attribute validators.
- The resource definitions proposal now documents the expected provider
  migration path: port each resource type boundary completely, including
  definitions, validation, capabilities, operations, and provider behavior,
  before removing the older resource provider infrastructure.
- The resource definitions reference provider POC now registers the executable
  application resource type as a singular provider boundary, including type,
  capability, operation, projection, apply, and change handlers, without
  introducing a broad application-provider aggregate.
- The Resource Manager bridge for the resource definitions POC now includes a
  generic graph-service DI helper that composes validation, projection, apply,
  operation, capability, and change services from separately registered
  resource-type providers.
- The Resource Manager bridge for the resource definitions POC now includes an
  in-memory Resource graph registration helper so hosts can back the bridge
  with `ResourceGraphModel` instead of ad hoc snapshot delegates.
- The resource definitions proposal now clarifies that capabilities and
  operations are integration points whose implementations may be owned by
  Resource Manager, orchestrators, provider packages, or other Control Plane
  services when those owners need to inject their own services and logic.
- The resource definitions POC now includes a capability resolver so
  provider-owned capability behavior can be composed into type-specific
  resource projections without making the definition stop being the persisted
  data container.
- The resource definitions POC now includes a validation pipeline that resolves
  definitions from registered resource type providers and then runs
  type-provider, capability-provider, and operation-provider validation with
  combined diagnostics.
- The resource definitions POC now uses strongly typed class, type,
  attribute, capability, and operation IDs, adds an isolated resource type
  provider validation path, and introduces a separate
  `CloudShell.ResourceDefinitions.ReferenceProviders` project for the
  executable reference provider. The infrastructure project no longer
  references the broad `CloudShell.Abstractions` project just to borrow
  resource classes or attribute constants, keeping the experiment aligned with
  provider-boundary detangling.
- Added the experimental `CloudShell.ResourceDefinitions` infrastructure
  project and `CloudShell.ResourceDefinitions.Tests` POC test project for
  resource-definition envelopes, class/type inheritance, effective
  attribute/capability/operation resolution, diagnostics, and attached
  capability/operation provider dispatch. This follows
  [ADR-20260624-001](ADR.md#adr-20260624-001-prove-resource-definitions-in-an-isolated-experimental-project).
- Added a resource definitions, capability providers, and operation providers
  proposal to track the distinction between projected `Resource` instances and
  persisted `ResourceDefinition` intent, including DI-backed capability and
  operation providers as attached behavior over definition payloads and
  resource commands.
- The resource definitions proposal now tracks inherited
  `ResourceClassDefinition` and `ResourceTypeDefinition` expectations,
  effective attribute/capability/operation resolution, and common plus
  provider-owned attribute validators.
- The resource definitions proposal now clarifies that operations are declared
  resource behavior resolved like attributes and capabilities, with operation
  providers implementing matching resolved operations and optional source
  generators as a future facade/builder aid.
- The resource definitions proposal now distinguishes capabilities from
  operations and sketches resource type provider change planning for
  definition updates using resolved diffs and runtime state.
- SQL Server declared database child-resource projection now lives in
  `SqlServerDatabaseResourceProjector`, moving another resource-type concern
  out of `ApplicationResourceService`.
- Application infrastructure projection profile selection now lives in
  `ApplicationResourceProjectionProfiles`, making top-level projection
  decisions reusable outside `ApplicationResourceService`.
- Application local port allocation now lives in `ApplicationResourcePortResolver`
  with a shared stable hash helper, reducing endpoint/projection coupling in
  `ApplicationResourceService`.
- Removed the unused SQL database normalization partial from
  `ApplicationResourceService` after SQL database projection moved to a
  SQL-specific projector.
- Application provider UI pages now depend on focused operation contracts
  instead of injecting `ApplicationResourceService` directly.
- Container app deployment and revision history reads now live in
  `ApplicationContainerHistoryService` behind
  `IContainerApplicationHistoryOperations`.
- SQL Server database inspection now lives in
  `SqlServerDatabaseInspectionService` and shares SQL connection creation with
  provider reconciliation code.
- The SQL Server credential API now depends on
  `ISqlServerCredentialResolutionOperations` instead of the full application
  resource service.
- SQL Server managed credential naming now lives in a focused helper shared by
  credential resolution, grant status, and reconciliation.
- SQL Server credential resolution now lives in
  `SqlServerCredentialResolutionService` behind the existing focused
  operation contract.
- SQL Server permission grant status inspection now lives in
  `SqlServerGrantStatusService` behind the SQL Server provider operation
  contract.
- SQL Server database and access reconciliation now lives in
  `SqlServerDatabaseReconciliationService`, leaving the application service to
  trigger reconciliation from lifecycle actions.
- Application resource definition lookup now has a focused
  `ApplicationResourceDefinitionSource` for resource-type providers.
- Application running-state checks now have a focused
  `IApplicationResourceRunningStateOperations` contract used by SQL services.
- Application configuration pages now use a focused
  `IApplicationResourceConfigurationOperations` contract for definition edits
  instead of the broader application management operations.
- Application registration pages now use a focused
  `IApplicationResourceRegistrationOperations` contract for definition setup
  instead of the broader application management operations.
- Remaining application provider UI pages now depend on focused definition,
  running-state, and configuration contracts instead of broad application
  management operations.
- Removed the unused broad `IApplicationResourceManagementOperations`
  provider contract after UI and provider services moved to focused
  application operation contracts.
- Application registration operations now use a dedicated focused adapter over
  definition lookup and definition registration instead of the shared
  application resource service.
- Application configuration operations now use a dedicated focused adapter over
  definition lookup, running-state checks, and definition registration instead
  of the shared application resource service.
- `ApplicationResourceService` no longer advertises itself as the application
  definition-source contract; `ApplicationResourceDefinitionSource` owns that
  provider-facing boundary.
- Application declaration application now uses a dedicated focused adapter over
  declared provider options and registration operations instead of the shared
  application resource service.
- Application resource template import/export now uses a dedicated focused
  operation service instead of the shared application resource service.
- Host-scoped application process cleanup now uses a dedicated cleanup
  provider instead of the shared application resource service.
- SQL Server reconcile-access action IDs now live outside the shared
  application resource service so projectors do not reference the service for
  constants.
- Application app-setting and environment-variable configuration providers now
  use a dedicated settings provider instead of the shared application resource
  service.
- Application process and container monitoring now use a dedicated monitoring
  provider instead of the shared application resource service.
- Application orchestration descriptor operations now use a dedicated
  descriptor provider backed by `ApplicationWorkloadConfigurationProvider`
  instead of the shared application resource service.
- Container runtime process tracking now lives in
  `ApplicationContainerProcessTracker`, and application running-state checks
  use a focused operation instead of the shared application resource service.
- Application resource graph projection and runtime container child-resource
  projection now live in `ApplicationResourceProjectionSource` instead of the
  shared application resource service.
- The remaining application runtime/procedure coordinator is now
  `ApplicationResourceRuntimeOperations`; the old `ApplicationResourceService`
  type is no longer required by provider-facing application infrastructure.
- Application configuration-entry and secret setting resolution now lives in
  `ApplicationResourceSettingResolver` instead of the runtime/procedure
  coordinator.
- Application resource action availability and start/restart preflight checks
  now live in `ApplicationResourceActionAvailabilityOperations` instead of the
  runtime/procedure coordinator.
- Application workload and runtime environment-variable composition now lives
  in `ApplicationResourceEnvironmentVariableResolver`, shared by workload
  descriptors and process/container startup.
- Application volume mount materialization and validation helpers now live in
  `ApplicationResourceVolumeMounts` instead of the runtime/procedure
  coordinator.
- Container app image and replica update operations now live in
  `ContainerApplicationUpdateOperations` behind
  `IContainerApplicationUpdateOperations`; the runtime/procedure coordinator
  keeps lifecycle execution and container-app orchestration hooks.
- Container app deployment description now lives in
  `ContainerApplicationDeploymentDescriptionOperations`, separating
  Resource Manager deployment shape projection from runtime service execution.
- Container app orchestrator service description now lives in
  `ContainerApplicationOrchestratorServiceDescriptionOperations`, separating
  Resource Manager service capability and service shape creation from runtime
  service instance execution.
- Container-backed application service preparation now lives in
  `ApplicationContainerOrchestratorServicePreparationOperations`, separating
  registry login, container network preparation, and replicated-app ingress
  stop preparation from runtime service instance execution.
- Container image materialization now lives in
  `ApplicationContainerImageMaterializer`, separating project container
  publish, Dockerfile build, and shared replica build caching from runtime
  procedure coordination.
- Container app deployment outcome handling now lives in
  `ContainerApplicationDeploymentOutcomeOperations`, separating post-apply,
  failed-apply, and tear-down planning from runtime service execution.

### 2026-06-23

#### Changed

- Default orchestrator deployment apply now materializes service instances with
  revision-scoped runtime names so container app image deployments can start new
  replica containers beside the currently serving revision before routing
  cutover.
- Orchestrator services now derive an explicit revision-scoped replica group
  for materialized resource instances, and container app runtime replica
  resources expose the group id alongside deployment, service, and revision
  metadata.
- Orchestrator revisions and internal deployment history now retain the
  materialized replica group snapshot produced by deployment apply, making the
  applied replica set inspectable across revision records.
- Default orchestrator and container app runtime paths now use replica groups
  as the primary replication abstraction inside an orchestrator-managed service
  shape, while still allowing providers to manipulate individual materialized
  resource instances where required.
- Replica groups now expose a change model for active revision scaling, and
  Resource Manager deployment apply uses it to add or remove group members
  instead of making container app providers manually diff replica counts.
- Default deployment apply now reconciles capacity changes for the active
  versioned replica group through Resource Manager deployment history, so scale
  changes produce Environment revisions while preserving unchanged group
  members.
- Resource Manager lifecycle start now applies provider-described deployments
  for deployment-capable resources, giving container apps an initial
  deployment/environment-revision baseline instead of treating first start as
  an unversioned provider side effect.
- Container app replica scaling no longer starts or removes replica containers
  inside the shared application service. The provider updates app intent and
  Resource Manager deployment apply reconciles the replica group.
- Resource procedure results now distinguish runtime reconciliation from
  restart-required prompts, so container app image and replica updates can
  request deployment apply without telling the UI that the whole resource must
  restart.
- Container app revision numbering and revision-history behavior now lives in
  a dedicated revision service with direct unit tests, starting the application
  service split by moving revision tracking away from runtime process/container
  helper code.
- Application workload configuration mapping now lives in a dedicated factory
  with direct unit tests for workload kind selection, common runtime
  attributes, and replica-mode behavior, reducing the mapping responsibility
  inside `ApplicationResourceService`.
- Log selection links now use the `logSourceId` query parameter in the global
  and resource-scoped log views, aligning the UI route contract with the
  source-first logging model.
- Application container-host resolution now lives in
  `ApplicationContainerHostResolver`, giving application provider units a
  focused boundary for host selection and capability validation.
- Application log-source discovery and reads now live in `ApplicationLogProvider`
  instead of `ApplicationResourceService`, continuing the split between shared
  application infrastructure and provider-owned concerns.
- Container app orchestrator deployment shape now lives in a dedicated factory
  with direct unit tests for service identity, deployment inputs, revision
  scoping, and status mapping, while deployment-history decisions remain in
  `ApplicationResourceService`.
- Application runtime state projection and transient lifecycle tracking now
  live in a dedicated tracker with direct unit tests for fresh/expired
  starting/stopping state, running fallback, and clear-starting/clear-stopping
  behavior.
- Application resource attributes and capabilities are now projected through a
  dedicated projection factory with direct tests for replica deployment
  attributes, volume materialization status, and capability flags.
- ASP.NET Core Web project runtime environment and dotnet run/watch argument
  policy now live in ASP.NET Core project provider-owned units with direct
  tests, while shared application infrastructure delegates to them.
- ASP.NET Core Web project endpoint defaulting and launch-settings endpoint
  discovery now live in ASP.NET Core project provider-owned units with direct
  tests.
- ASP.NET Core Web project definition normalization now lives in an ASP.NET
  Core project provider-owned rule, separating legacy dotnet argument parsing
  and hot-reload policy from generic project-backed application cleanup.
- Container app revision and replica normalization now lives in a
  container-app-owned rule, while the shared application normalizer fallback
  only constructs provider-neutral project/container-backed cleanup rules.
- Container app image-deployment planning now lives in a container-app-owned
  planner with direct tests for produced app revisions, deployment history, and
  image-only replica-mode preservation.
- Container app replica-scaling planning now lives in a container-app-owned
  planner with direct tests for requested replica intent and validation.
- Container app deployment failure planning now lives in a container-app-owned
  planner with direct tests for base-revision lookup and rollback state.
- Container app deployment tear-down planning now lives in a container-app-owned
  planner with direct tests for superseded revision and legacy stable replica
  group cleanup decisions.
- Container app runtime revision scoping now lives in a container-app-owned
  policy with direct tests for environment revision and active app revision
  history.
- Container app deployment-applied planning now lives in a container-app-owned
  planner with direct tests for recording materialized environment revisions.
- Container app deployment store, revision service, and orchestrator deployment
  factory files now live under the `ContainerApp` provider directory.
- Top-level application resource projection now lives in a reusable projector
  helper with direct tests for endpoint mapping, SQL actions, and replicated
  container app health-check projection.
- The refactoring tracker now clarifies that resource providers are an umbrella
  for separate resource-type concerns such as projection/listing, lifecycle,
  logs, monitoring, and change application, while shared application
  infrastructure remains a toolkit rather than an inventory owner.
- Resource model documentation now separates declared resources from
  provider/runtime projections, calling out that `GetResources()` currently
  returns a unified graph that can include artifacts which were not explicitly
  declared.
- Resource model documentation now clarifies that declared and
  projected/listed resources can both be referenced, while projected resources
  remain read-only unless the owning provider exposes operations or change
  handling for them.
- Resource model documentation now describes resource definitions as identity,
  type, typed payload, and provider-owned attributes, with serialized formats
  such as JSON treated as projections of that structure.
- Resource Manager projections now include `resource.graph.membership`
  metadata so consumers can distinguish declared/registered resources from
  provider-projected artifacts without splitting the resource graph.
- Application resource stable identifier and runtime container resource ID
  generation now lives in a tested helper shared by projection, logs,
  observability, and runtime child-resource concerns.
- `ILogProvider`, `ILogStore`, `ILogManager`, the Control Plane API, and the
  remote client now use `LogSource` as the log discovery contract; legacy
  `LogDescriptor` discovery support and `/logs` descriptor endpoints have been
  removed.
- `ILogProvider` runtime access methods are now source-addressed as
  `ReadLogSourceAsync` and `StreamLogSourceAsync`, aligning provider access
  with `LogSource` discovery.
- Application resource log discovery now returns `LogSource` records directly
  instead of manufacturing `LogDescriptor` compatibility metadata for
  application and runtime-container sources.
- Resource activity log discovery now stays source-first and no longer
  projects activity sources back into descriptor-shaped compatibility metadata.
- Configuration Store and Secrets Vault service log discovery now returns
  `LogSource` records directly instead of descriptor-shaped compatibility
  metadata.
- Docker host diagnostics and container log discovery now returns `LogSource`
  records directly instead of descriptor-shaped compatibility metadata.
- The Control Plane log-source catalog now aggregates resource declarations
  and contributed `LogSource` records directly.
- The deployment proposal now clarifies that deployment definitions describe
  resource intent through resource definitions and provider-owned attributes,
  with serialized formats treated as projections of that model.
- Application resource documentation now includes a provider layering diagram
  that separates raw Resource Provider infrastructure, the Application Resource
  Provider toolkit, and the built-in dogfooded application resource providers.
- Development workflow docs now call out local Docker daemon crash handling for
  Docker-backed sample verification, including using `docker info` to
  distinguish host/runtime failures from product regressions.
- Environment and container app scaling views now show deployment-record
  replica-group details, making scale reconciliation visible through runtime
  revision, replica group, requested replica slots, and materialized replicas.
- Container app projection now preserves revision-scoped replica group and
  runtime container names after deployment state is persisted without
  provider-owned deployment history, keeping health and log sources aligned
  with the containers the orchestrator materialized.
- Liveness lifecycle projection now degrades a parent resource when only some
  runtime scopes fail to respond, instead of marking the whole replicated
  resource stopped while healthy runtime scopes are still serving.
- Replica scale deployments now run post-apply cleanup, and container apps can
  retire legacy stable replica groups after a revision-scoped group is
  materialized even when provider-owned deployment history is missing.
- Default deployment apply now returns replica groups retired by a replacement
  runtime revision, so post-apply cleanup follows from the deployment outcome
  instead of provider-specific predecessor inference.
- Container app replica-group tear-down now removes retired local Docker
  containers instead of only stopping them, preventing stale retired replicas
  from remaining visible as resources and log sources.
- Deployment and container app proposals now define replica groups in terms of
  requested replica slots, with slot-focused restart/replacement policy owned
  by a Resource Manager orchestration reconciler instead of provider-specific
  loops.
- Replica groups now expose requested replica slots and occupied slot count in
  the domain model, allowing future reconciliation to distinguish vacant slots
  from an intentionally reduced replica group.
- Container app declarations can now configure replica-group management policy,
  and deployment metadata distinguishes requested replica slots, materialized
  slots, and occupied replica count.
- Resource Manager orchestration now performs an initial liveness-driven
  replica slot replacement for unhealthy container app slots, emitting
  replica-management events for the slot decision and replacement outcome.
- Resource health refresh now queues unhealthy replica slot observations for a
  replica-group reconciliation service instead of running replacement work
  inline with health evaluation.
- Replica slot reconciliation now processes unhealthy slot observations while
  a container app remains degraded, so an app that was already degraded can
  still replace a killed replica after a later health refresh.
- Replica slot reconciliation now targets the latest active materialized
  replica group from deployment history when one exists, instead of deriving
  the repair target from provider default service declarations.
- Replica slot replacement now restarts the failed slot occupant without
  rerunning full service preparation, avoiding project container rebuilds during
  local replica repair.
- The container app Scale and replicas view now displays requested replica
  slots as the primary rows, with optional occupant details and auto-refresh so
  slot repair transitions are visible.
- The container app Scale and replicas view now prefers parent runtime-scope
  health observations when rendering slot health, so a failed replica slot is
  visible even when the last projected runtime occupant still exists.
- Replica slot reconciliation now retains queryable slot runtime state for
  unhealthy, repairing, repaired, and repair-failed slots, including attempt
  counts and provider results, so future UI/API surfaces do not need to infer
  repair status from health events or container listings.
- Resource Manager now exposes replica slot reconciliation state through the
  Control Plane manager/API/client surface, and the Environment page shows
  slot repair status alongside deployment history and materialized runtime
  state.
- The Environment page now shows active replica groups from deployment records
  rather than only counting projected resource attributes, correlating each
  group with requested slots, occupied slots, materialized replicas, and slot
  reconciliation status.
- Replica slot reconciliation state now records the targeted service, replica
  group, and runtime revision when repair is processed, and Environment
  replica-group diagnostics only correlate slot states for the same materialized
  group so scale changes are not presented as repairs.
- Environment revisions on the Environment page now prefer deployment-record
  history over projected resource attributes, showing revision number,
  based-on revision, provisioned-by, and created time when the deployment
  record provides them.
- The repository now includes `ControlPlane.http` as a quick manual endpoint
  scratchpad while the Control Plane OpenAPI document remains the served API
  specification.
- Architecture and orchestrator deployment documentation now include diagrams
  that show Resource Manager as the facade over deployment, orchestration,
  environment revisions, replica groups, and runtime providers.
- The Resource Manager resource table now refreshes its projected resource
  model immediately after lifecycle actions complete, keeping row state aligned
  with the resource detail view after starts and stops.
- Resource Manager orchestration now has an internal service tear-down boundary
  so orchestrator services can stop their materialized runtime resources
  separately from incremental deployment setup.
- Resource Manager orchestration now tears down superseded container app
  replica groups as a separate post-apply operation instead of hiding cleanup
  inside deployment apply.
- Default orchestrator deployment apply now logs rollback events and attempts
  best-effort teardown of the candidate replica group when setup fails before a
  revision is produced.
- Container app deployment apply failures now notify the provider so failed
  candidate app deployments/revisions are marked failed and the previously
  active app revision remains active.
- Local Docker container app deployment now preserves local/Docker Hub image
  tags, passes the inspected image platform when starting containers, scopes
  automatic replica probe ports by revision, and uses short network aliases for
  revision-scoped ingress targets.
- Container app container startup now waits for declared HTTP
  startup/readiness checks, or HTTP health checks when no explicit
  startup/readiness check is present, before reporting a replica as
  materialized for deployment revision activation.
- Container app deployments now surface recent orchestrator deployment,
  readiness, rollback, and cleanup activity on the Deployment tab, while
  post-apply cleanup of superseded replica groups is best-effort and reported
  as warning activity instead of failing an already-applied revision.
- Container app Resource Manager UI now separates the deployment operation
  from revision inspection: the Deployment tab focuses on deploying an image
  and reading deployment events, while the new Revisions tab shows current and
  previous app configuration revisions.
- The deployment/revision and container app proposals now define the internal
  MVP as a generalized Resource Manager orchestration deployment model, with
  container apps as the first validation path while leaving public deployment
  APIs, restore, retention, traffic strategies, and adapter mappings flexible.
- Deployment/revision documentation now defines restore as a new deployment
  based on the materialized state captured by a selected revision, with the
  resulting revision recording the based-on revision relationship instead of
  reactivating the old revision object, while still allowing additional
  deployment input before the new revision is materialized. Ordinary
  deployments default the based-on revision to the current active or latest
  successful revision.
- Deployment/revision documentation now describes future state merge from
  revisions as a deployment-authoring workflow that composes selected
  materialized-state snapshots, using deployment records for diff context, into
  a final deployable state.
- Deployment/revision documentation now defines the orchestrator-level
  implementation boundary for `BasedOnRevisionId`: orchestrators track the
  metadata and materialized revision state, while Resource Manager or
  provider-owned workflows author restore and merge deployments.
- Orchestrator deployments and revision outcomes now carry `BasedOnRevisionId`.
  Resource Manager deployment apply defaults it to the active/latest
  successful revision for the same resource service and preserves explicit
  based-on revision ids for restore-like deployments. Revision outcomes also
  carry `ProvisionedBy` so the materialized snapshot records who provisioned
  the deployment that produced it.
- Orchestrator revision `Id` is documented as the CloudShell-wide unique
  identifier, while `RevisionNumber` is now treated as the service-scoped
  chronological ordinal for revision history.
- Container app revision records now carry app-local revision numbers and the
  provisioned-by actor, and the Revisions tab uses the revision number as the
  visible identifier while retaining the unique revision id in metadata.
- The deployment/revision proposal has been split by ownership: container app
  revisions are documented as app-owned configuration-management snapshots,
  while orchestrator revisions are documented as environment-history records
  produced by applying desired runtime state. The orchestrator deployment is
  the primary operational object; the environment revision is historical
  traceability.
- Orchestrator environment revision ids are now represented by
  `ResourceOrchestratorEnvironmentRevisionId` and generated separately from
  deployment/runtime revision ids, so container app revision ids are not reused
  as Resource Manager environment revision identities.
- Orchestrator deployment specs now expose a deployment-definition structure
  with typed service definitions, grouped resource definitions, and standalone
  resource definitions, separating the domain structure from any eventual JSON,
  builder, database, or API projection format.
- Orchestrator environment revisions now retain the deployment-definition
  snapshot that produced the successful environment change, so revision history
  can track the service/resource structure independently of deployment format.
- Resource Manager now has an Environment page in the main menu for inspecting
  the current host environment's deployments, environment revisions, replica
  groups, materialized resources, and recent deployment events without moving
  orchestrator concepts into the container app-specific UI.
- Container app deployment apply success now records the produced orchestrator
  environment revision id on the app definition and projects it onto the stable
  app resource and hidden runtime replica resources for environment-level
  diagnostics.
- The Environment page now shows a `baseline-current` revision for the current
  declared resource graph alongside deployment-produced environment revisions,
  so a freshly started host still has an inspectable logical environment
  revision.
- Environment inspection now gives revision and materialized deployment tables
  the full page width and labels the event feed as environment activity, so
  scale-only replica updates are not presented as deployment operations.
- Resource Manager now exposes read-only deployment records through the
  Control Plane API/client and Environment page, showing deployment attempts,
  status, produced environment revision, based-on environment revision,
  provisioned-by actor, and replica-group outcome without adding public
  deployment execution commands.
- The deployment proposal and Environment page now describe the current rows as
  host-scoped environment revisions, while deferring a fuller
  Control Plane Environment model and its tenancy, authorization, isolation,
  retention, and placement semantics.
- Resource Manager deployment coordination now lives separately from
  orchestration execution under dedicated Deployment and Orchestration
  namespaces, with the default deployment service reusable for orchestrators
  that do not expose a native deployment concept.
- Resource Manager implementation files are now organized by concern across
  Health, Recovery, Identity, Networking, Observability, Templates, Platform,
  Deployment, and Orchestration namespaces while keeping common programmatic
  declaration extensions on the general Resource Manager authoring surface.
- Deployment replica projection now distinguishes requested replica slots
  (`deployment.replicas.requestedSlots`), materialized slots
  (`deployment.replicas.slots`), and occupied replica count
  (`deployment.replicas.count`).
- FileSystem volumes can now be attached to executable and ASP.NET Core
  project resources through the same `ResourceVolumeMount` model used by
  container apps, with Resource Manager Storage tabs and local process
  filesystem-link materialization that keeps durable state owned by the volume
  resource.

### 2026-06-22

#### Changed

- Resource health checks now carry a shared probe source abstraction, with HTTP
  as the built-in source and a provider-facing probe evaluator interface for
  non-HTTP resource health signals.
- Resource recovery now has an initial Control Plane policy and status surface
  for resource-scoped automatic restart configuration, with restart execution
  deferred to a later recovery controller slice.
- Resource recovery refresh now evaluates liveness health signals, tracks
  failure thresholds and backoff state, and invokes the normal Restart
  lifecycle action when recovery is due.
- Resource recovery now has an opt-in local polling host that enumerates
  enabled recovery policies and calls the shared recovery refresh path without
  making request-serving Control Plane hosts the long-term polling owner.
- Resources now project separate liveness and recovery capabilities, with
  recovery derived only when a resource has a liveness signal and lifecycle
  action support that can restore it.
- Unhealthy liveness now projects active resources as `Degraded`, keeping
  liveness visible in the resource lifecycle status that recovery policy can
  react to.
- Liveness check results now carry structured outcomes so
  responding-but-unhealthy signals can keep active resources `Degraded`, while
  no-response liveness signals can project active resources as `Stopped`.
- Liveness-driven lifecycle projection now waits for the configured failure
  threshold, records degraded or unexpectedly stopped resource activity when
  the threshold is reached, and passes recovery causes into restart lifecycle
  events. Liveness and recovery now wait for resources to be running, so
  intentionally stopped resources are not probed for liveness or automatically
  restarted.
- Docker host resources now project provider-owned liveness for Docker API
  reachability. Unavailable Docker hosts are observed as `Degraded` rather than
  recovered by CloudShell, and degraded resources continue liveness probing so
  restarting Docker can return the host to `Running`.
- The Application Topology sample now enables recovery for its API web project
  through a resource-builder recovery declaration so liveness and recovery can
  be validated against a real application resource.
- Resource Manager generated details now include a Management > Recovery tab
  for resources with recovery support, showing recovery policy and runtime
  status, with the resource Health tab linking to Recovery.
- Resource health checks now support per-check polling interval overrides and
  per-check result timestamps, and SQL Server resources declare a
  provider-native liveness check through their TDS endpoint.
- Resource health check results can now include scoped observations so
  provider-owned aggregate checks can report per-replica, dependency, route,
  or runtime-scope details without adding scope metadata to resource health
  declarations.
- Replicated container app HTTP health checks now project onto the hidden
  runtime replica resources, keeping the declaration on the container app
  definition while preparing the Control Plane to poll replica health directly
  when providers can expose replica probe addresses.
- Runtime replica health observations now roll up into an aggregate health
  assessment on the stable container app resource, and a Replicated Container
  Health sample verifies the parent assessment and scoped replica observations.
- Container app Logs, Traces, Metrics, and Monitoring views now stay anchored
  on the stable container app resource while projecting contained runtime
  replica log sources, telemetry scopes, and replica-tagged resource metrics
  for overview and drill-down.
- Container app deployments now track app-owned revision history entries with
  image, requested replicas, based-on revision, timestamp, and trigger
  metadata, and the Deployment tab surfaces that history.
- Container app image deployments now also record provider-owned app
  deployment history outside the desired application definition, correlating
  each deployment request to the produced app revision and orchestrator
  deployment metadata for the Deployment tab.
- Container app revision history now has a provider-owned operational
  projection alongside app deployment history, allowing the Deployment tab to
  show active and superseded app revisions without treating revision history
  as desired application configuration.
- The Control Plane now has an internal orchestrator deployment-apply boundary
  so deployment specs can be dispatched to the selected orchestrator without
  resource domains directly manipulating runtime replicas, ingress, or cleanup.
- Running container app image deployments now ask the provider for the current
  orchestrator deployment spec and apply it through the selected orchestrator
  instead of surfacing a restart-required result when runtime reconciliation is
  available.
- Container app orchestrator deployment description is now advertised only by
  the container app provider boundary, keeping the shared application
  infrastructure from registering provider-facing deployment facets directly.
- Default orchestrator deployment apply now records service reconciliation and
  replica materialization activity events, giving container app deployments
  rollout milestones that can support future live deployment visualization.
- Orchestrator deployment apply now produces an orchestrator revision result
  and records internal Control Plane deployment history for apply attempts,
  successful revisions, and failures without exposing orchestrator deployments
  as a public management API.
- Default orchestrator deployment apply now records routing update milestones
  for endpoint-bearing services after replica materialization, clarifying the
  handoff from an uploaded image deployment to remapping service traffic onto
  the new replica revision.
- SQL Server database declarations now use `DeclareDatabase(...)`, and local
  development or test resources can opt in to missing database creation with
  `DeclareDatabase(...).EnsureCreated()` instead of creating declared
  databases by default.
- The shared application-resource projection seam is now public: custom
  application-like providers can subclass `ApplicationResourceTypeProvider`
  and provide an `ApplicationResourceProjection` while reusing the common
  application declaration, template, lifecycle, descriptor, and action
  availability path.
- Built-in application resource providers now consume split provider-facing
  contracts for definitions, procedures, templates, declarations, descriptors,
  action availability, container-app behavior, and SQL Server permission status
  instead of taking a direct dependency on the full application provider
  facade.
- Custom application-like providers can now declare an
  `ApplicationResourceDefinition` with their own provider ID through the shared
  application resource declaration helper, reusing the common builder defaults
  for environment variables, endpoints, probes, recovery, log formats, storage,
  and other application settings.
- Application registration and update UI now builds application definitions
  through `ApplicationResourceDefinitionBuilder` instead of directly invoking
  the definition constructor, reducing UI coupling to the full definition
  shape.
- Application setup/update now materializes provider-owned application
  definitions through `ApplicationResourceDefinitionRegistrationService`, with
  definition normalization isolated in `ApplicationResourceDefinitionNormalizer`
  while Resource Manager registration, grouping, and dependency synchronization
  remain on the same path used by other resources.
- Application definition normalization now supports provider/type-specific
  `IApplicationResourceDefinitionNormalizationRule` implementations, with the
  built-in project, ASP.NET Core endpoint, container-backed, and SQL Server
  defaults moved out of the shared normalizer.
- Application resource docs now frame the provider direction as a composable
  toolkit for custom resources backed by local executables, ad-hoc containers,
  or Resource Manager-managed sub-resources with default lifecycle,
  observability, endpoint, health, configuration, storage, and cleanup wiring.
- The Replicated Container Health sample now configures replica-aware logs,
  trace spans, and request metrics so the container app's Logs, Traces,
  Metrics, and Monitoring views can be verified against a realistic demo API.
- Runtime replica log sources now resolve before generic application log
  sources and parse JSON console records into structured log entries, so
  replica Logs views can show app-emitted structured fields.
- Application log sources now default stdout/stderr streams to plain text and
  expose programmatic source-format remapping; replicated container apps
  project the parent source format to runtime replica log sources and only
  parse replica JSON console output when that format is declared.
- Application runtime log parsing and resource log source projection are now
  factored out of `ApplicationResourceService`, starting the shared
  application infrastructure cleanup without changing provider behavior.
- `ApplicationResourceService` is now documented as the built-in application
  provider facade rather than the desired final abstraction; future shared
  application infrastructure should split role-specific provider-facing
  services only where extension authors need stable reuse.
- Container-host command execution and capture are now factored out of
  `ApplicationResourceService`, keeping Docker/Podman process invocation,
  environment setup, command logging, and output capture behind a smaller
  application infrastructure helper.
- Local process definition construction for application resources is now
  factored out of `ApplicationResourceService`, and obsolete process
  start-info helpers left behind by the `LocalProcessRunner` split were
  removed as the shared provider primitives continue moving away from
  provider-specific service concerns.
- SQL Server startup no longer creates missing declared databases. Declared
  databases are projected expected child resources; database creation remains
  an application or migration responsibility until an explicit SQL
  management action is added.
- Container app deployment attributes now include
  `deployment.replicas.materialized` for app-owned runtime replica resources,
  while the previous `deployment.replicas.projected` attribute remains
  populated as a compatibility alias.
- Runtime replica materialization diagnostics now use
  `orchestratorMaterialized` for app-owned replicas, while the provider-created
  resources proposal records future projected-resource facade resolution as a
  separate concept.
- Resource-scoped Logs views now keep navigation anchored on the selected
  parent resource when choosing projected runtime replica log sources.
- The global Health page now includes visible resources with aggregate health
  assessments even when the concrete probe declarations live on projected
  runtime resources, while default summary counts stay focused on visible
  resources instead of hidden runtime projections.
- The global Health page now groups contained runtime resources under their
  parent resource with an expand affordance, keeping container apps as the
  primary row while still allowing replica health inspection.
- Container app Scale and replicas now shows each projected replica's health
  assessment, most relevant check detail, contributing check counts, and last
  observation time so runtime observations are visible next to replica state
  and placement.
- Container app replica count changes now run through a dedicated Scale and
  replicas command, and running replicated apps can scale between replica
  counts without forcing a full resource restart.
- Resource Health now links aggregate runtime-scope observations to the
  resource-owned Scale and replicas view when the resource type exposes one.
- The global Health page now links resources directly to their resource-scoped
  Health tab instead of sending users back to the resource overview.
- Replicated container app runtime replicas now materialize probe-only
  endpoint mappings for active local Docker replica health checks, allowing
  HTTP health and liveness probes to be evaluated per replica while the stable
  app endpoint remains the normal traffic ingress.
- ASP.NET Core project-to-container declarations can now pass an explicit
  `AsContainer(tag: "...")` image tag, and the Replicated Container Health
  sample uses an explicit local image tag for its test web app image.
- Container application starts now materialize project-backed images before
  preparing the runtime service, and container app stops remove the replicated
  ingress container from the service-provider path.
- Runtime-discovered Docker containers now map Docker `created` and terminal
  states to `Stopped`, `restarting` to `Starting`, and `removing` to
  `Stopping`, avoiding misleading startup status for projected containers.
- Health status pills now have a shared `HealthPill` component used by the
  resource Health views, dashboard issue list, resource inventory, and
  container app replica table.
- Resource health check declarations now deserialize reliably after persistence
  when they use either the HTTP compatibility fields or a provider-owned probe
  source.
- Documented the health vocabulary split: liveness is an observation, while
  health is an assessment that may include liveness, readiness, dependencies,
  provider-owned status, and aggregate application data.
- Recovery remains available for stopped resources that can be started, and an
  enabled recovery policy can start a stopped resource after it previously
  observed a healthy signal.
- Added a service observability and degradation proposal for service-first,
  replica-aware local-development telemetry correlation, common views, load
  and capacity context, established telemetry interfaces, extension
  abstractions, and redacted public reports.
- The service observability proposal is now framed as service telemetry and
  degradation: Telemetry is the product/API surface for emitted logs, traces,
  metrics, scopes, and events, while observability remains the broader
  capability that also includes health, liveness, recovery, monitoring, and
  correlations.
- Application Topology request-count metric panels now show a cumulative
  `http.server.requests.total` counter instead of summing the most recent 100
  per-request samples.
- SQL Server overview connection strings now omit the SA password while
  keeping the password available only through the masked password field.
- Resource Manager resource relationship cards, resource-reference pills, and
  application overview resource cards now show resource-type icons for faster
  scanning.
- Dashboard, Health, and Observability summary cards now use a shared metric
  card with prominent area icons, while generated information cards use the
  same left badge treatment as resource cards for consistency.
- SQL Server database rows, container replica tables, and the common Health
  table now use a shared resource table identity component for consistent
  icon, title, and secondary-label rendering.
- Docker container host views now use the shared resource table and identity
  components for the projected container list.
- Resource Manager and observability pages now use the shared empty-state
  component instead of locally hand-coded empty-state markup for simple empty
  and not-found states.
- Users, Extensions, Settings, Health, Resource Templates, custom shell views,
  Resource Manager settings, and tabbed resource panels now use the shared
  shell panel header component instead of repeated local header markup.
- Log, trace, and metric explorers now share a telemetry explorer header
  component for source summaries and toolbar controls.
- Resource-scoped activity, telemetry, monitoring, health, and service graph
  views now share a small resource activity header component instead of
  repeating the same inline title and action layout.
- Shell, observability, and Resource Manager pages now use a shared compact
  page heading component for standard eyebrow, title, description, and action
  layouts.
- Composition-backed shell summaries now use a shared summary tile component
  with component-scoped styling instead of repeated selected-type card markup.
- Dashboard, Health, and Observability navigation rows now use shared activity
  list and link components with row-state styling contained in component CSS.
- Dashboard, Health, and Observability panels now use a shared shell panel
  component with component-scoped card styling.
- Local user and resource-type lists now use shared shell entity row and tag
  list components instead of global provider-row and tag-row markup.
- Resource Manager, Docker provider, application provider, configuration
  provider, and shared primary-action forms now use shared `FormPanel` and
  `FormActions` components instead of repeated form wrappers and global form
  layout CSS.
- Summary metric cards now support compact rendering, and summary metric cards
  and grids own their styling through component-scoped CSS instead of
  page-level hand-coded markup.
- Dashboard, Health, and Observability summary rows now use the shared summary
  metric grid component instead of the old global `metric-grid` styling.
- Resource health, SQL Server database, and container replica tables now use a
  shared resource table component with scoped styling instead of hand-coded
  table wrappers and global table CSS.
- Documented CloudShell UI component organization guidance for view-local,
  feature-local, Resource Manager-shared, and product-shared components.
- Resource table identity and health history chart styles now live with their
  components through scoped CSS instead of the global app stylesheet.
- Added a Resource Manager project-structure proposal for separating shell
  hosting, Resource Manager shared concepts, Resource Manager UI, Resource
  Manager UI abstractions, host installation, Control Plane services, and
  provider UI/runtime integration boundaries.
- Added a CloudShell architecture document describing CloudShell UI as the
  extensible shell application, the Control Plane as the backend application,
  and extensions such as Resource Manager as integrations that can plug into
  both layers through shared concepts.
- Consolidated host topology, capability package, extension surface, and
  workload terminology into the architecture document, with system design,
  domain model, and hosting guidance now referencing that conceptual model.
- Clarified that Resource Manager UI extensions should depend on Resource
  Manager/UI abstractions and stable shared components instead of referencing
  the concrete CloudShell UI host package solely for contracts or reusable UI.
- Clarified that CloudShell UI should stay isolated from extension
  implementations, consuming contributions through extension points,
  abstractions, shell services, and adapter layers rather than exposing shell
  internals as the integration model.
- Clarified that extension-facing UI contracts should remain above the
  concrete component stack so another CloudShell UI implementation can consume
  the same public abstractions and services, then render them with its own
  presenters.
- Clarified the application-resource abstraction direction: built-in
  application providers dogfood shared process/container lifecycle,
  containment, runtime-state, and orchestrator-managed sub-resource
  infrastructure before that base is promoted as a stable extension point.

### 2026-06-21

#### Changed

- Documented the resource-centered product principle that CloudShell should
  expose established cloud-native concepts and user intent instead of copying
  provider legacy taxonomies, while keeping provider-native details available
  for resource-specific inspection and diagnostics.
- Documented that CloudShell is a hosting platform that doubles as a local
  development tool, with the same Control Plane, Resource Manager, resource
  model, and provider extension patterns running locally or in an on-premise
  environment.
- Resource overview now summarizes concrete endpoint mappings and resolvable
  HTTP/HTTPS DNS name mappings, and the common Resources list prefers a
  resolvable DNS name mapping before falling back to the primary endpoint.
- Resource Manager now restores active lifecycle transition indicators from
  recent resource activity when users leave and return to resource list or
  detail views during a start, stop, pause, or restart action.
- SQL Server resources now expose a reconcile-database-access action, and the
  local SQL Server provider can apply CloudShell read/write database grants for
  resource identities by creating provider-owned contained database users and
  read/write role memberships for declared databases before reporting
  provider-side grant effectiveness.
- Users and Extensions now live as common Settings sections, with the shell
  topbar settings cog as the primary entry point instead of a Platform
  navigation group.
- Composition section registrations now carry grouping attributes, and the
  common Settings page uses them to group General settings separately from
  Resource Management settings without adopting sidebar-style collapse
  behavior.
- Resource Management settings in the common Settings page are now split into
  separate General and Orchestration sections instead of embedding the old
  Resource Manager settings summary/navigation surface.
- Embedded Resource Management settings now defer to the common Settings
  section header so the selected section title is not shown twice.
- Documented future Control Plane scale-out around API replicas, a
  lease-backed primary controller, and independent worker processes for
  subsystems such as log ingestion, telemetry, health polling, notifications,
  and provider reconciliation.
- Documented a future IoT and edge-device direction where device provisioning
  uses enrollment evidence plus cryptographic proof, reconciles devices into
  the resource graph, binds device principals through the identity model, and
  lets Resource Manager act as the development and diagnostics cockpit.
- Mapped-resource DNS surfaces now render name mappings as endpoint anchors
  only when CloudShell can resolve the mapped target endpoint as HTTP/HTTPS,
  while name-mapping and DNS-zone overviews keep the name mapping itself
  distinct from the resolved target URL. Application overview endpoint rows
  also show DNS source metadata as muted text instead of appending it to the
  URL.
- Resource details now tolerate route-bound resource view segments that arrive
  as part of the resource id, so direct links such as `/resources/{id}/dns`
  still resolve the resource before selecting the registered view.
- Host-scoped shutdown now performs defensive cleanup for control-plane-scoped
  container workloads even when their local runner process already exited, and
  control-plane-scoped container apps remove replicas directly with force.
- Local process runner debug logs now include explicit process-handle release
  messages after tracked process exit observation and runner disposal.
- Local process runner recovery now treats persisted control-plane-scoped
  processes as startup cleanup targets only, while detached processes remain
  eligible for restart reattachment.
- Local process runner pre-start commands now terminate their process tree when
  canceled and log the released process handle.
- Container host command helpers now dispose command processes through a
  common release path and log that release even when cancellation interrupts a
  Docker/Podman command.
- Application providers now release tracked container process handles as soon
  as an exited tracked process is observed instead of retaining them until
  provider disposal.
- Docker host client create/dispose diagnostics now use Debug level, matching
  the process-oriented container host command logs.
- Docker provider shutdown now cancels active discovery refreshes before
  disposing cached host clients, waits for refresh quiescence, and logs when a
  Docker refresh does not stop within the configured request timeout.
- Docker integration smoke coverage now exercises graceful Application
  Topology host shutdown and verifies the SQL Server container is removed.
- Observability and log views now use resource-name fallbacks instead of raw
  resource IDs when a telemetry/log source references a resource that is not
  present in the current resource snapshot.
- Trace and metric selection warnings now use resource-name fallbacks for
  missing telemetry scopes instead of leading with raw resource IDs.
- Resource Manager relationship pills, restart prompts, and the observability
  overview now use resource-name fallbacks instead of raw resource IDs when
  the referenced resource cannot be resolved.
- DNS and name-mapping Resource Manager views now use resource-name fallbacks
  for unresolved target and provider resources instead of showing raw resource
  IDs in routine metadata.
- Re-aligned the local-development MVP queue around Application Topology
  repeatability, resource-name-first UI messages, exposure/name-mapping link
  clarity, host-scoped lifecycle cleanup, process/Docker diagnostics, guarded
  force-release recovery for stale runtime ownership, and app-page
  observability before broader platform work.
- Metrics views now present recent metric points in a structured table with
  stable source, metric, value, and attribute columns instead of a compact
  log-style stream.
- Metrics views can now render appsettings-configured resource metric panels
  as live indicators or recent-history line charts, with the raw metric stream
  available as a separate Metrics subview.
- The Application Topology sample now configures basic request count and
  request duration metric panels for its frontend and API resources.
- Traces and telemetry metric points now support an appsettings opt-in
  database store with per-resource retention limits, and Application Topology
  enables it for persisted debugging history.
- Application provider logs now default to session-only memory storage and can
  opt into bounded plain-file persistence with retention limits and optional
  per-day file splitting; Application Topology enables file-backed application
  logs explicitly.
- The logging proposal now identifies `ResourceLogSource` as the resource-model
  declaration and projected `LogSource` as the Control Plane abstraction for
  listing, querying, streaming, parsing, and rendering logs.
- Log source abstractions now define resource-owned source declarations,
  projected Control Plane sources, source format, storage, and capability
  metadata, with compatible projection from existing log descriptors.
- Log source declarations now track source origin and configuration metadata,
  and application resources advertise the `logs.sources` capability for future
  capability-driven log source configuration.
- Log sources now distinguish discovery/default/custom purpose, and resources
  expose a `SupportsLogSources` helper for the `logs.sources` capability.
- Resources now carry `ResourceLogSource` declarations, and application
  resources declare their default console log source for Control Plane and
  CloudShell discovery.
- The logging proposal now treats volume-backed file logs as source access
  metadata, separate from universal log persistence.
- Log source declarations now announce availability so live-only sources can be
  distinguished from persisted or provider-backed sources.
- The Control Plane API and remote client now expose log-source discovery by
  projecting resource-declared sources and provider descriptors into
  `LogSource`.
- The Logs explorer now uses projected `LogSource` metadata for source kind,
  format, and availability labels while keeping descriptor-based read and
  stream operations in place.
- Log descriptor API responses now carry source kind, format, storage,
  capability, origin, purpose, and availability metadata so descriptor-based
  consumers stay aligned with log-source discovery.
- `ILogProvider` can now expose projected `LogSource` metadata directly, with
  descriptor-backed providers bridged into source discovery by default.
- `ILogSourceSession` now represents the provider-owned runtime access context
  used when reading or streaming a projected log source.
- The Control Plane domain API, HTTP API, and remote client now expose
  source-addressed log metadata, read, and stream operations under
  `log-sources`.
- `ILogStore` now exposes source-addressed log read and stream operations,
  keeping descriptor-named methods as compatibility aliases.
- `ILogProvider` can now open log sessions from a resolved `LogSource`, so
  providers can use source metadata and configuration when materializing
  sessions.
- Log provider resolution now asks active providers whether they can open a
  resolved source before requesting a session, enabling resource-declared
  sources to be handled by provider capability.
- `ILogStore` can now explicitly materialize disposable log-source sessions for
  Control Plane read, polling, streaming, and future transport lifecycles.
- The Logs explorer and resource detail surfaces now list projected log
  sources, including source-only declarations from resource kinds, and read
  through source-addressed log operations.
- The Logs explorer now shows a compact log-source inventory with source,
  kind, format, availability, capabilities, and open actions before the
  selected source stream, routing Activity sources to the resource Activity
  view instead of a non-selectable log stream.
- The Logs explorer no longer crashes the Blazor circuit when log scrolling
  interop races with disposal during source changes or live updates.
- The Logs explorer now separates Stream and Sources views, keeps source
  inventory out of the live stream, labels merged log entries with their
  source/resource, and keeps wrapped log rows following the latest entries.
- Merged Logs explorer views now support route-backed source filters so
  users can choose which operational log sources participate in the `All logs`
  read and history view.
- The Logs explorer Sources view now lets the source inventory fill the normal
  viewer space with vertical scrolling and lets long source labels wrap instead
  of clipping the column.
- Resource-scoped log source discovery, reads, and streams now enforce both
  the common logs permission and resource read access, while provider-owned
  sources remain gated by the common logs permission.
- Resource activity logs are now projected by the built-in provider as native
  `LogSource` records, with descriptor-shaped logs retained as compatibility
  projections.
- Log source listing now has an explicit catalog/contributor abstraction:
  `ILogSourceCatalog` merges resource declarations, source contributors, and
  descriptor compatibility projections, while `ILogProvider` remains
  responsible for managing/opening sources and materializing sessions.
- Docker host/container resources and Configuration Store/Secrets Vault
  service resources now declare provider-owned default log sources.
- Resource inventory, Observability landing, Docker container, and application
  overview surfaces now use projected log sources for log availability and
  navigation.
- Logging documentation now describes `ResourceLogSource` declarations and
  projected `LogSource` records as the current listing model, with
  `LogDescriptor` retained as compatibility.
- The Logs explorer now uses projected `LogSource` records directly instead of
  a descriptor-shaped compatibility view model.

### 2026-06-20

#### Changed

- Resource action-readiness diagnostics now trim redundant current-resource
  prefixes from blocked reasons so resource pages show concise local dev
  preflight feedback.
- Observability navigation, page titles, and routes now use Dependencies and
  Service map.
- Dependency auto-start errors now describe dependency paths with resource
  display names and resource names instead of internal resource IDs.
- Resource relationship graphs now expose related resource actions for opening
  the resource, activity, logs, and traces from dependency/dependent nodes.
- Resource relationship graphs now include related resource health and
  readiness details in dependency/dependent summaries when available.
- Resource details now include a Management **Health** tab for resources with
  configured health checks, showing latest status, individual probes, and a
  manual refresh action.
- Application and generated resource overviews now link directly to the
  resource-scoped **Health** tab when the resource declares health checks.
- Application overviews now link Logs and Traces diagnostics to the
  resource-scoped tabs instead of global observability filters.
- Application overviews now describe configuration and secret reference
  metadata with resource labels and friendly resource-identity labels instead
  of leading with raw resource IDs.
- Resource detail headers now use the readable resource name in the subtitle
  while keeping the canonical Resource ID visible in detail sections.
- Trace span details now use resource labels for resource links instead of
  showing raw resource IDs as the link text.
- Health views now share a resource health history chart backed by retained
  resource health snapshots, and the resource-scoped Health tab auto-refreshes
  while open.
- Resource health history now renders separate timeline charts per configured
  health check so degradation times are visible without mixing checks into one
  combined chart.
- Resource Health tabs now appear for resource types that support health
  checks even before a resource has registered health check endpoints.
- Documented weak projected-resource references and reusable Resource Manager
  selector components as deferred model/UI design work beyond the immediate
  local-development MVP slice.
- Documented status-page-style common Health aggregation as deferred work that
  needs explicit health scopes beyond ordinary resource groups, while
  resource-scoped Health stays focused on the selected resource.
- Re-aligned the MVP roadmap around Application Topology confidence,
  app-centric Resource Manager reliability, readiness diagnostics, and pausing
  new shell-composition work unless it stabilizes current surfaces.
- Refined the shell composition direction around a CMS-like layout/content
  engine with dynamic composition, separate navigation and addressable content
  hierarchies, content-ID link resolution, Razor-owned routing, slots, section
  containers, section outlets, and Resource Manager tabs as one
  renderer-specific adapter.
- Composition menu targets now distinguish addressable artifact IDs from
  direct href targets, and CloudShell Hosting can project legacy shell
  navigation items into the composition main menu as a migration bridge.
- The CloudShell sidebar now renders through a composition-backed Fluent menu
  presenter, while the standalone Composition Sandbox demonstrates a
  Bootstrap menu presenter that interprets namespaced icon attributes with
  Bootstrap Icons.
- Resource Manager now contributes its settings surface into the common
  composition-backed CloudShell Settings page while keeping the direct
  `/resources/settings` route available.
- Resource Details now uses the shared shell tabbed layout component while
  preserving Resource Manager tab grouping, generated views, and invalid-tab
  recovery behavior.
- Custom shell views now use the same shared shell tabbed layout as Settings
  and Resource Details, and the layout exposes neutral shell CSS hooks while
  keeping Resource Manager compatibility styles.
- The CloudShell Fluent navigation presenter now consumes composition menu item
  projections and renders module ownership metadata for root and child menu
  items.
- Composition-backed sidebar menu styling now lives with the menu presenter so
  parent items with sub-items keep their custom row, toggle, and child-list
  layout under Blazor CSS isolation.
- Composition-backed section tabs now keep selected section state in sync with
  child-address routes and parent-address fragments so tab content updates on
  the first click.
- The legacy shell navigation bridge now targets the composition Settings page
  by page ID while leaving other legacy shell items on direct href targets.
- The shell-owned Overview, Users, Extensions, and Settings pages are now
  registered as composition page targets so core navigation can resolve
  through stable page IDs during the menu migration.
- The common Settings page now resolves nested section navigation and direct
  settings `SectionId` targets through `/settings/{section}`.
- Composition-backed section layouts now show a not-found empty state for
  unknown section route/query selections instead of silently opening the first
  section.
- CloudShell Fluent surfaces can now use a composition-aware anchor component
  that resolves page and artifact targets through the composition registry.
- The Home dashboard's Fluent Resources and Health actions now resolve
  registered Resource Manager page IDs through composition targets.
- The Platform section on the common Settings page now resolves Users and
  Extensions links through composition page targets instead of querying the
  legacy shell catalog directly.
- Composition menu registrations can now be contributed by multiple modules
  and merged by menu/group ID, and Resource Manager contributes its Resources
  and Health sidebar items directly into the composition main menu.
- Core shell navigation now contributes Overview, Settings, Users, and
  Extensions directly into the composition main menu, leaving the legacy shell
  navigation bridge for unmigrated extension navigation items.
- Composition artifacts now carry neutral authorization metadata for
  permissions, policies, roles, and claims; the CloudShell Fluent sidebar
  evaluates menu, group, and item permission metadata from the graph.
- Observability navigation now contributes its Overview parent item and Logs,
  Dependencies, Service map, Traces, and Metrics child items directly into the
  composition main menu with graph-encoded permission requirements.
- Custom card-like and tab-like navigation links now suppress hover underlines
  when their own border or background state already communicates hover.
- Missing configuration and secret references now fall back to resource names
  instead of full resource IDs in app environment summaries and selectors.
- Application overview, generated overview, and generated endpoint summaries
  now use resource-name fallbacks for unresolved related resources.
- The Traces resource detail panel now leads with the readable resource name
  while keeping the canonical resource ID as a secondary field.
- The Resources list details blade now uses resource-name fallbacks for
  unresolved endpoint mappings, load-balancer targets, and name providers.
- Resource Manager settings now describe the non-display-name label mode as
  resource names instead of resource IDs.
- Control Plane startup, lifecycle, identity provisioning, and Resource
  Manager diagnostics now use resource names in human-facing messages instead
  of leading with canonical resource IDs.
- Programmatic resource startup now logs a clear start/completion summary and
  keeps provider warning/error diagnostics at warning/error levels.
- Configuration, host-configuration, and Secrets Vault resource declarations
  now keep resource names separate from display names so lifecycle and startup
  diagnostics use the actual resource name.
- The Application Topology sample app settings now expose debug-level
  Resource Manager local process and Docker host diagnostic category toggles.
- Added provider coverage for control-plane-scoped container cleanup so stale
  stable container names are removed before start and stopped containers are
  removed during shutdown/stop cleanup.
- Executable-backed sample smoke tests and provider process tests are now
  categorized as integration tests, with Docker-dependent smoke tests also
  carrying `Category=DockerIntegration`.
- Local process tracking now drops exited in-memory process entries after the
  first observed exit so shutdown and stop checks do not repeat the same debug
  log line.
- Lifecycle, dependency auto-start, and host-scoped shutdown messages now use
  qualified resource labels when a display label differs from the resource name,
  keeping cascading local-dev start and shutdown flows easier to distinguish.
- Docker-backed application operations now log container-host command start,
  completion, and release messages at debug level with the full command
  arguments.
- Local executable and project-backed application operations now log
  process-level start, exit, stop, shutdown cleanup, and recovery details at
  debug level underneath the resource events, while failed or non-zero process
  exits remain warnings.
- The UI Extension Host sample now contributes its sample workspace sidebar
  item through the composition main menu instead of legacy shell navigation.
- CloudShell Hosting now exposes `builder.AddCompositionModule(...)` for
  extension-owned composition modules, keeping extension authoring off direct
  service collection registration.
- Resource Details now projects Resource Manager tabs into the shared tabbed
  layout through a focused Resource Manager adapter instead of keeping tab
  grouping rules inline in the page.
- Resource Details now has a canonical parameterized composition page target
  and uses composition link resolution when switching resource tabs.
- Resource Manager resource links now have a shared composition-backed helper
  for resolving Resource Details URLs, with Health and Resource Graph using
  the canonical page target.
- Resource Manager, dashboard, logs, and observability links to Resource
  Details now resolve through the shared composition-backed helper, leaving
  legacy route construction as the fallback path inside that helper.
- Composition section outlets now declare parent or child address mode so
  child sections can either share the parent address or own short child address
  values, with Blazor link resolution projecting those modes into the current
  route conventions.
- The common Settings composition outlet now opts into child addresses, so
  direct settings section targets resolve to `/settings/{section}` instead of
  page-local fragments while the Settings renderer decides how to present the
  sections.
- The plain Blazor composition tab outlet now uses section target resolution
  for tab links and can select sections from child-address routes or
  parent-address fragments.
- Plain Blazor composition now includes a section navigation component that
  renders normal anchors to registered sections, enabling page-local hash
  deep links for parent-addressed sections.
- The composition sandbox now demonstrates section deep links with CSS-only
  smooth scrolling and a target highlight.
- Composition tab renderers now select sections through child addresses or
  fragments instead of reserving a query-string parameter for tab state.
- Legacy custom shell views now use URL fragments for local menu item
  selection, matching the parent-addressed composition section convention.
- The shared CloudShell tabbed layout now renders route-backed tab items as
  plain links with real hrefs because the layout needs full row-level CSS
  control that the Fluent anchor wrapper does not expose cleanly.
- The plain Blazor composition menu and sandbox Bootstrap presenter now mark
  the active menu item from the resolved page or section target.
- The CloudShell Fluent composition tabbed layout now resolves non-default
  section navigation through section targets instead of duplicating route
  parameter construction in the UI adapter.
- Resource Manager static page links for graph, add resource, create group,
  templates, settings, and Resources recovery states now resolve through the
  shared composition-backed helper with legacy routes kept as fallbacks.
- Documented parent-scoped address projection for nested section navigation,
  where a page or section outlet can declare whether child sections share the
  parent address or own child address values independently from the renderer
  that presents them.
- Documented the future composition-aware Blazor router as the path toward
  route declarations sourced from the composition graph instead of duplicated
  `@page` directives.
- Composition links are now exposed as `CompositeAnchor`, support unmatched
  anchor attributes, and render a customizable unresolved placeholder instead
  of a broken `href="#"` when an artifact target cannot resolve.
- Default Blazor composition components now document their attribute-splatting
  behavior, and `CompositionMenu` supports unmatched attributes on its `<nav>`
  root plus class parameters for menu markup customization.
- The common Settings page now renders composition sections through a
  CloudShell-specific tabbed-layout adapter, keeping section projection,
  selection, link resolution, and dynamic section rendering reusable.
- CloudShell's main layout now hosts the composition context in pass-through
  mode so composition-registered pages receive cascaded context without
  blocking legacy routes that are not registered yet.
- Resource Manager now registers its static shell pages as composition pages,
  leaving parameterized resource detail routes on the existing route helpers
  until the Resource Manager details URL shape is migrated separately.
- Composition link resolution now materializes matching route-template values
  into path segments, omits missing optional route segments, and leaves
  remaining route values as query parameters.
- Shell tab links now suppress browser link underlines on hover and focus,
  because the tab item itself provides the hover and active treatment.
- Resource Manager static navigation routes now use shared
  `ResourceManagerRoutes` constants for add-resource, resource-group,
  templates, and settings links.
- Resource Details now generates `/resources/{resourceId}` and
  `/resources/{resourceId}/{view}` links by convention while preserving the
  legacy `/resources/{resourceId}/details?tab=<group>:<view>` route shape.
- The shared shell tabbed layout can now carry composition module ownership
  metadata from composition-backed section tabs to rendered tab buttons and
  panels.
- CloudShell UI Extension Host now includes an isolated shell-composition
  sandbox with sample-local typed IDs, a registry, composition context host,
  menu renderer, section container, and section outlet so the layout/content
  model can be explored without changing the core shell APIs.
- The UI composition sandbox now includes a link component and target
  abstraction that resolve page and section IDs into routes, query strings, and
  fragments.
- The UI composition sandbox now uses a Blazor layout-hosted composition root
  that resolves the current routed page to a content ID and cascades context to
  nested menus and section outlets.
- Clarified that the composition engine is a reusable Blazor composition model
  that CloudShell uses for its shell surfaces, not something limited to
  CloudShell UI.
- Documented the eventual shell-composition integration path: after the
  isolated UI Extension Host sandbox proves the model, the composition root
  should move into the core CloudShell main layout so integrating services can
  target shell-provided IDs.
- Added initial `CloudShell.UI.Composition` and
  `CloudShell.UI.Composition.Blazor` libraries plus a clean Blazor
  Composition Sandbox sample that demonstrates the engine outside CloudShell
  Hosting with plain Bootstrap styling and no CloudShell extension adapter.
- The Blazor composition library now includes a plain `TitleOutlet` component
  that renders the title for the current composition page from the cascaded
  composition context.
- The core composition registry now has focused tests for route normalization,
  target link resolution, section ordering, menu registration, duplicate ID
  validation, and extendable section outlet validation.
- The standalone Composition Sandbox now includes a second registered Blazor
  page to prove page-to-page navigation through composition page IDs.
- Added current-state UI composition documentation covering the two
  composition libraries, typed IDs, registry registration, plain Blazor
  components, link resolution, the standalone Bootstrap sample, validation,
  and explicitly deferred extension/CMS behavior.
- The Composition Sandbox now includes a dashboard route with a sample-owned
  Bootstrap grid section outlet so layout renderer patterns can be explored
  without changing the core composition libraries.
- The Composition Sandbox now includes a settings route that uses the reusable
  composition tab section outlet with a normal `section` query parameter for
  selected named-section state.
- Hardened the experimental composition docs and registry tests around named
  sections, section-link route parameters, duplicate section IDs, and deferred
  localization/module ownership.
- Documented the shell composition direction for composed ID value types,
  builder-created modules, mountable `CompositionModule` lifecycles, and a
  future descriptor/instance/projection split for serializable artifacts.
- Added the first composition module boundary: `CompositionModuleId`,
  `CompositionModuleBuilder`, module assembly APIs, registry creation from
  modules, and tests for module identity, composition, and duplicate module
  validation.
- Added an in-memory `CompositionEngineHost` that mounts and unmounts
  composition modules by rebuilding the active registry projection, with tests
  for successful mounts, failed duplicate mounts, unmounts, and missing module
  removals.
- Added first-pass composed ID factories for composition modules, menus, menu
  sections, menu items, pages, section outlets, and sections so child IDs can
  be derived from parent IDs without string concatenation.
- Added first-pass composition descriptor records and mapping helpers for
  modules, pages, menus, menu sections, menu items, and sections, including
  JSON round-trip coverage for the serializable descriptor shape.
- Added descriptor-to-module rehydration through a host-provided component
  type resolver so persisted component type names do not directly activate
  runtime components.
- Added first-pass composition projections for pages, menus, and sections that
  preserve the owning composition module ID for diagnostics and future
  renderer-specific views.
- Updated the plain Blazor composition menu, link, stacked section, and tabs
  renderers to consume module-owned projections and expose module ownership
  through `data-composition-module` attributes.
- Added explicit section outlet artifacts with `IsExtendable` validation so
  modules can only add sections to extension points that the outlet owner has
  marked as extendable. Permissions and visibility remain future dynamic
  policy layers.
- Composition Blazor outlets and title rendering can now resolve page context
  from cascade, an explicit page ID, or the current route, keeping the base
  components viable for static SSR, interactive server, WebAssembly, and mixed
  render-mode hosts.
- Added a separate `PageTitleOutlet` that wraps Blazor `PageTitle`, keeping
  visible page-header rendering separate from document-title rendering.
- Added Blazor DI helpers for registering multiple `CompositionModule`
  instances before assembling the composition registry, and updated the
  sandbox to use separate host and sample-extension modules.
- CloudShell UI now registers the composition engine services during
  `AddCloudShellUi()`, and the UI Extension Host sample registers a passive
  composition module for its sample workspace page as the first shell
  integration seam.
- The Blazor composition library now includes reusable page and tabbed-page
  layout components, and the standalone Composition Sandbox settings page uses
  the tabbed-page layout to prove the pattern before CloudShell adopts it.
- CloudShell Hosting now includes a reusable resource-details-style tabbed
  layout component and a common `/settings` page backed by composition
  sections for the initial shell and platform settings surfaces.
- Composition modules now have typed section-outlet extension points, typed host
  context module registration, and an `Extend(...)` API for cross-module
  contributions to published extendable section outlets.
- Composition pages now track whether they are extendable, and the registry
  validates that extension-owned section outlets can only target extendable
  pages. The composition docs also distinguish future `CanBeReplaced`
  artifact policy from extension.
- Composition section outlet extension now returns a limited extension builder,
  and the composition docs describe declaration builders, extension builders,
  and runtime projections as separate views over the same artifact model.
- Composition menus now use `MenuGroup` terminology for named item groups,
  support root menu items, grouped items, sub-items, permission metadata,
  namespaced attributes such as icon, and direct href targets, and the registry
  now maintains typed lookup maps for composition artifacts.
- Moved the composition proof direction away from the UI Extension Host sample:
  CloudShell extension integration should adapt to the core composition graph
  only after the standalone app structure is credible.
- Refreshed the local-development MVP target around Application Topology
  confidence, immediate resource relationship comprehension, readable
  Resource Manager labels, app-centric diagnostics, and focused readiness
  hardening.
- Dashboard failed request rows now reuse the trace severity treatment: entry
  span failures render as red failure rows, while traces with failed child spans
  render as yellow attention rows.
- Log views now keep initial and streaming auto-scroll requests pending across
  render frames so the viewer scrolls to the latest entry after the nested log
  list is attached.
- Unauthenticated request-bound resource operations now record activity with
  the generic `user` actor instead of falling through to system activity, while
  background work without a request context remains system-owned.
- ResourceHost sample smoke coverage now verifies that an in-memory user
  principal grant is visible in the Resource Manager Access control tab.
- SQL Server Add Resource and Configuration views now use SQL service language
  and hide provider runtime image/host settings behind advanced controls instead
  of presenting the resource as a container app.
- SQL Server **Databases** now merges declared database intent with a read-only
  live query against the running instance so Resource Manager can show whether
  declared databases exist and list additional databases on the server.
- SQL Server startup now creates missing declared databases for the local
  provider, and ApplicationTopology now connects its API to the declared
  `application_topology` database while keeping SQL grant-to-user
  materialization explicit future work.
- SQL Server **Databases** now keeps live databases in the normal list and
  shows a separate missing-declarations section only after runtime verification
  proves declared databases do not exist on the server.
- SQL Server overview now warns when database access grants are modeled in
  CloudShell but have not been materialized as SQL Server users or roles yet.
- SQL Server Access control now shows the same provider-side materialization
  warning before users assign database grants.
- SQL Server identity/access documentation now makes requested-versus-effective
  database grant status the next step before provider-owned SQL user and role
  materialization is treated as working access.
- Managed SQL Server proposal now documents the Azure-style identity boundary:
  identity brokers provide authentication artifacts, while SQL Server providers
  materialize SQL-side users, roles, or external mappings.
- Resource Manager now exposes provider-backed permission-grant status, and SQL
  Server Access control uses it to show database grants as requested but not
  yet effectively applied by the SQL Server provider.
- Access control grant rows now include provider status details so SQL Server
  grants explain why requested access has not been applied.
- SQL Server resource overview now leads with SQL service details instead of
  generic container image, revision, and host placement fields.
- Managed SQL Server and identity/access proposals now mark
  requested-versus-effective grant status as implemented and keep SQL-side
  login, user, role, and drift inspection as the remaining provider work.
- Resource Monitoring tabs now auto-refresh while open by default and only
  show the manual Refresh command when auto-refresh is disabled.
- Application action-readiness messages now use the resource name instead of
  leading with internal resource IDs.
- Application setting and secret grant diagnostics now use displayed resource
  labels for the referenced resource and the calling identity.
- Configuration and Secrets reference resolution errors now use the existing
  store or vault name instead of repeating the resource ID.
- Setting and secret resolution context can carry a readable identity display
  name so provider denial messages do not need to expose principal resource IDs.
- SQL Server grant warnings now render through the custom CloudShell
  `ProcedureMessage` styling instead of the default Fluent message bar.
- Account setup, sign-in, and local user feedback now use the same custom
  `ProcedureMessage` styling as Resource Manager feedback.
- Trace views now distinguish OpenTelemetry error spans from spans and trace
  summaries that only contain error spans, using red error treatment for the
  failing span itself and a softer attention treatment for recovered or
  fallback flows.
- Recent trace summaries now default to newest-first ordering and expose sort
  options for newest, longest duration, and errors first.
- The global Traces view can now show an aggregate **All sources** list while
  keeping trace detail links scoped to the trace entry resource.
- Application Topology sample now includes an `/upstream/fallback` endpoint
  that preserves one trace across a failed upstream attempt and successful
  recovery path.
- Resource health polling now waits until the Control Plane host has started,
  suppresses repeated polling failure logs until polling succeeds, and routes
  Resource Manager lifecycle, process, and health-probe logs through dedicated
  CloudShell logging categories that hosts can tune through appsettings.
- Docker provider discovery and container-host commands now honor shutdown
  cancellation by stopping refresh work, disposing cached Docker clients, and
  killing canceled Docker CLI processes instead of leaving host connections or
  commands alive across repeated CloudShell host restarts. Docker host client
  creation/disposal and Docker CLI process start/exit/kill events are now
  logged through the Resource Manager Docker host lifecycle category.
- Resource list rows now show a warning triangle for a potential bad state
  when a resource is in a non-running lifecycle state but its latest health
  checks are still reporting healthy.
- Resource Manager now has a D3-powered resource dependency graph page linked
  from Resources, showing visible resources, endpoint summaries, lifecycle
  state, and `DependsOn` relationships.
- Resource Manager severity indicators now use a shared Fluent icon mapping:
  warning triangle, information circle, success check circle, and error circle,
  with filled variants available for higher-emphasis contexts.
- Application Topology now sends frontend and API HTTP request count and
  duration metrics to CloudShell metric ingestion so they appear in
  Observability and each application resource's Metrics tab.
- Observability log, trace, and metric source selectors now use Resource
  Manager display labels when available while keeping canonical resource IDs in
  detail fields.
- Trace and metric views now share a Resource Manager resource-source selector
  component so resource option formatting stays consistent across
  observability views.
- The Logs resource filter now uses the same Resource Manager source selector
  path as trace and metric sources, including preformatted resource labels for
  sources whose resource projection is unavailable.
- Trace and metric unavailable-resource messages now use the Resource Manager
  display label when the requested resource exists but does not expose that
  telemetry signal.
- Logs unavailable-resource messages now use the Resource Manager display label
  when the requested resource exists but has no registered log sources.
- Request graph and request map resource subtitles now honor the Resource
  Manager display-name preference instead of always preferring display names.

### 2026-06-19

#### Changed

- SQL Server service resources and projected SQL database child resources now
  use distinct Resource Manager icons so the server and database levels are
  visually distinguishable.
- SQL Server resources can now declare projected databases through
  `DeclareDatabase(...)`. The application provider projects those databases as
  provider-managed `application.sql-database` child resources and adds a SQL
  Server **Databases** tab so Resource Manager can display them without
  exposing generic container-app controls.
- SQL Server can now be declared through a provider-owned `AddSqlServer(...)`
  builder that projects `application.sql-server` as a service resource while
  keeping the local runtime container-backed. ApplicationTopology and
  ContainerHost samples now use that builder, ApplicationTopology records SQL
  Server database read/write grant intent for the API identity, and SQL Server
  resource pages no longer expose generic container-app Deployment or Scale
  tabs by default.
- Application Topology smoke coverage can now exercise the SQL-inclusive
  runtime path when Docker and the local SQL Server image are available,
  proving frontend-to-API, settings, secrets, and API-to-SQL connectivity
  without forcing a container image pull in default test runs.
- Application Topology smoke coverage now starts the project-backed API and
  frontend resources and verifies that the deliberate frontend-to-API failure
  path returns correlated ProblemDetails without requiring Docker or SQL
  Server.
- Application Topology intentional failure responses now include trace,
  resource, sample-failure, and upstream-status ProblemDetails extensions so
  failed runtime requests can be correlated with Resource Manager traces and
  logs.
- Refreshed Application Topology sample documentation to separate already
  covered local-development MVP proof areas from the remaining scenario
  additions.
- Application Topology documentation and smoke coverage now reflect the current
  local-hostname workflow: use the Local DNS resource's **Reconcile name
  mappings** action instead of manually editing host mappings outside
  Resource Manager.
- Container App Deployment smoke coverage now verifies that resources declared
  with `Persist(overwrite: true)` are projected and rendered as persisted
  declarations rather than transient startup declarations.
- Application Topology smoke coverage now verifies that stopped project-backed
  apps surface Control Plane health feedback on the resource page and in the
  Health workspace.
- Clarified that local-development MVP work should be planned and verified
  through concrete use cases that combine scenario proof, Control Plane
  feedback, Resource Manager surfacing, and app-centric diagnostics.
- Re-evaluated the local-development MVP target after the recent readiness
  hardening slices: Control Plane feedback and Resource Manager surfacing
  remain core quality, while the urgent work now returns to Application
  Topology proof and the full app-centric developer experience.
- Refreshed the MVP execution order around the current local-development
  target: Application Topology confidence, app-centric Resource Manager
  workflows, readiness diagnostics before failure, settings/secrets/identity
  clarity, persisted-state handoff, and release hardening.
- Realigned the current MVP decision filter around the repeated local
  app-development loop: run, understand, diagnose, and persist a realistic
  distributed application from Resource Manager before opening new platform
  fronts.
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
- Resource overview relationship sections now use a shared immediate
  relationship graph that shows direct dependencies, the current resource, and
  resources that depend on it, with application overview using the same
  component.
- Identity grant and provisioning rows now use display-name-aware resource
  identity labels, including the resource name when needed to disambiguate.
- Application overview readiness now follows the resource state and shows the
  relevant Start or Restart preflight instead of always preferring Start.
- Container app replica rows now avoid showing internal runtime resource IDs
  when the container name, revision, materialization, and host already identify
  the projected replica.
- Storage and volume relationship rows now prefer display labels and resource
  type metadata over raw resource IDs when listing owned volumes and volume
  consumers.
- Added a shared resource display-label helper and adopted it in identity,
  access control, storage, and volume views to reduce duplicated label
  formatting logic.
- Added a shared resource state display helper so dashboard, Resource Manager
  pages, and the state indicator use one lifecycle-state CSS mapping.
- Adopted the shared resource display-label helper in application overview,
  app setting references, and volume display helpers to keep app-centric
  resource labels consistent.
- Adopted the shared resource display-label helper in DNS, name-mapping,
  load-balancer, service, storage, volume, and environment-reference forms so
  resource picker labels and ordering share one formatting path.
- Added a shared volume-mount display helper for mount summaries and
  materialization labels, replacing duplicate formatting in application,
  storage, and volume views.
- Added a shared volume display helper for access-mode labels, replacing
  duplicate formatting in volume editors and using the same readable labels in
  volume overview.
- Added a shared application resource display helper for lifetime and
  container-host labels, replacing duplicate formatting across application
  overview and application resource editors.
- Added a shared name-mapping display helper for DNS host, target, exposure,
  provider, materialization, and target-resource matching, replacing duplicate
  formatting across generated resource views, DNS zone overview, resource-list
  blades, and application overview.
- Shared display helpers for volume access modes, volume mount
  materialization, name-mapping materialization, application lifetime,
  container hosts, and application volume mount access now expose localized
  overloads, and Resource Manager views use those overloads for human-facing
  labels.
- ASP.NET Core project-backed applications now report missing project paths as
  Start/Restart unavailable reasons before dispatching `dotnet build`; relative
  paths are resolved from the resource working directory or CloudShell content
  root.
- Local process application resources now report missing configured working
  directories and missing explicit executable file paths as Start/Restart
  unavailable reasons before dispatching the process runner.
- Container app resources now report missing registry credential password
  environment variables as Start/Restart unavailable reasons before attempting
  registry login, without exposing secret values.
- Container app Start/Restart availability now reports unsupported selected
  container host capabilities before provider dispatch: image-backed apps
  require `container.image`, and project-container builds require
  `container.build`.
- Container app Start/Restart availability now also reports unavailable
  selected container host resources and unavailable host credentials before
  provider dispatch.
- Access Control principal search results now render as a vertical list with
  full-width principal rows and disambiguate resource identity display names as
  `<DisplayName> (<resource name>)` when those values differ.
- Added a reusable `ResourcePillLink` component for display-name-aware resource
  relationship pills and adopted it in application overview, generated
  resource views, Access Control, and the resource-list blade.
- Added a reusable `EndpointLink` component for endpoint address rendering and
  adopted it in application overview, generated endpoint/overview views, and
  the resource list.
- Added a reusable Resource Manager diagnostic list component so generated
  resource views, storage overview, and resource-list blades render diagnostic
  rows consistently.
- Added a reusable `GeneratedListItem` component and adopted it for generated
  endpoint, DNS, and identity rows that share the standard title, summary, and
  metadata layout.
- Adopted `GeneratedListItem` in application overview identity, service
  discovery, networking, readiness, and diagnostics summaries while leaving
  custom environment rows unchanged.
- Added a Resource Manager `ResourceListItem` component for resource-icon list
  rows and adopted it in DNS, storage volume, and volume consumer views.
- Added a reusable `ProcedureMessage` component and adopted it across Resource
  Manager procedure, read-only, and compact warning messages.
- Adopted `ProcedureMessage` across application provider warnings and log read
  errors so `procedure-message` markup is owned by one shared component.
- Moved `ResourceStateIndicator` into the shared component package and adopted
  it for application replica state pills in scaling and monitoring views.
- Added a reusable `GeneratedRowActions` component and adopted it for generated
  action rows in identity, access control, endpoints, and application actions.
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
- Generated resource overview pages now include direct incoming dependents in
  the Relationships section, so backing resources such as configuration stores
  and secret vaults show which application resources use them.
- Resource list detail blades now render dependencies as display/name-aware
  resource links and include direct incoming dependents in a `Used by` section.
- Application overview pages now use projected display names for related
  resources in dependency, service discovery, networking, environment
  reference, identity grant, and storage mount summaries.
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
- Resource health checks now run through the Control Plane, which caches the
  latest health state and can retain opt-in bounded in-memory health snapshots
  or database-backed health snapshots behind a store abstraction so UI clients
  read shared health state instead of probing resource endpoints directly. This
  follows
  [ADR-20260619-004](ADR.md#adr-20260619-004-store-resource-health-as-control-plane-snapshots).
- Resource access is now modeled as ordered effective levels: none, reference,
  read, operate, and manage. `resources.reference` supports locked resource
  references without granting inspection, and shared helpers evaluate the
  caller's effective access from reference, read, action, and manage
  permissions. Programmatic resource declarations can grant typed access
  profiles through `ResourceAccessPermissions`. This follows
  [ADR-20260619-005](ADR.md#adr-20260619-005-model-resource-access-as-ordered-effective-levels).
- Observability now has grouped and signal-specific read permissions:
  `observability.read`, `observability.logs.read`,
  `observability.traces.read`, and `observability.metrics.read`. Control Plane
  log, trace, and metric queries enforce those permissions and filter returned
  telemetry to resources the caller can read, while shell navigation and
  observability pages hide or block unavailable signal areas. This follows
  [ADR-20260619-006](ADR.md#adr-20260619-006-gate-observability-by-signal-permissions-and-resource-read-access).
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
- Built-in local identity sign-in now requires the user's email address, and
  the account sign-in, setup, and sign-out pages opt out of interactive
  routing so their Fluent UI forms can set or clear authentication cookies
  through static SSR posts. In-memory sample users such as Alice can sign in
  from the browser without accepting the programmatic principal key as a login
  identifier, and in-memory users now keep the configured key as the
  CloudShell principal ID while ASP.NET Core Identity uses the user's email for
  sign-in.
- Account sign-in, setup, and sign-out pages now use browser-local language
  and color-scheme selectors instead of the user-scoped interactive shell
  selectors, so they work before the user has signed in.
- Clarified authentication and authorization modes across the docs, including
  permissive local development, local claim-evaluation tests, built-in
  identity, and external identity. The ResourceHost sample now explicitly runs
  in built-in Identity mode by default while documenting the
  `Authentication:Enabled=false` override for permissive local development.
- Resource action, image update, and replica update activity now default the
  audit actor from the current authenticated Control Plane principal when the
  command omits `TriggeredBy`; authenticated Control Plane requests take
  precedence over client-supplied actor text, while background/system work can
  still use explicit system actors when no authenticated request is present.
- Built-in ASP.NET Core Identity sign-in now has an explicit
  `Authentication:BuiltInIdentity:AllowUserNameSignIn` policy. The default
  remains email-only; enabling the policy allows local usernames for browser
  sign-in, password-token requests, and in-memory identity users.

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
- Control Plane image and replica update preflight failures now return stable
  `resourceImageUpdateUnavailable` and `resourceReplicasUpdateUnavailable`
  ProblemDetails codes instead of collapsing provider readiness failures into a
  generic operation failure.
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
- Application Start and Restart action availability now preflights configured
  app setting and environment-variable references through registered
  configuration-entry and secret resolvers, so missing entries or secrets are
  reported before provider dispatch without exposing resolved values.
- Container app Deployment tabs now surface image-update and restart readiness
  diagnostics before enabling the Deploy command.
- Container image and replica updates with automatic restart now run restart
  readiness checks before saving deployment changes when the app is already
  running, so a known restart blocker does not leave the resource partially
  updated.
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
- ApplicationTopology smoke coverage now asserts the SQL Server resource
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
- Container apps now project requested replica/container runtime artifacts as
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
  requested replica count, and keeps the deploy-image operation grouped with
  deployment-specific state.
  Decision: [ADR-20260615-002](ADR.md#adr-20260615-002).
- Container apps now project an internal orchestrator deployment view onto the
  stable app resource. The projection includes deployment id, service id,
  status, revision/workload version, requested replicas, and projected runtime
  replicas, and the Deployment tab renders that state without exposing public
  rollout-history or restore APIs yet.
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
  storage-owned SQL data volume, and a sample-local SQL Server resource;
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
- Load-balancer action availability now revalidates route shape before apply
  and lifecycle execution, so persisted or loaded definitions with conflicting
  route matches report a stable unavailable reason before provider dispatch.

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
