# Refactoring Tracker

This is the living task list for active CloudShell refactoring work. Keep it
focused on boundary, ownership, and implementation slices that are underway or
queued next. Durable product decisions still belong in `ADR.md`; landed changes
belong in `CHANGELOG.md`; feature shape belongs in the relevant proposal.

## Current Refactoring Goal

Finish the Resource model migration boundary: ResourceDefinition-based
desired state should become the normal Resource Manager create/update/export
path. The active priority is finishing the switch to the `CloudShell.ResourceModel`
package family after hosts, samples, tests, UI surfaces, and active solution
files moved off the old provider model.

Container app orchestration remains an important internal runtime direction,
but it is on hold until the provider migration is finished. Keep the already
landed deployment/reconciliation seams stable, but do not make orchestrator
controller consolidation the next active slice unless it directly unblocks the
Resource model provider migration.

## Boundary Decisions

- Resource Manager owns lifecycle orchestration, deployment apply,
  environment revision recording, dependency policy, authorization gates,
  resource graph state, and cross-provider diagnostics.
- Resource providers are the Resource Manager integration umbrella for a
  resource type. A provider capability does not have to be one monolithic
  class; creation/declaration, change application, lifecycle actions, action
  availability, resource projection/listing, logs, monitoring,
  attribute-to-runtime mapping, and provider-specific commands can be separate
  concerns coordinated under that umbrella.
- Resource model providers integrate with host/runtime behavior through
  focused adapter contracts. Providers own resource semantics and call
  contracts such as runtime controllers, inspectors, reconcilers, command
  runners, and deployment handlers. Hosts, samples, or default runtime
  integration packages register the concrete process, Docker, filesystem,
  networking, sidecar, or orchestrator implementations. Providers should not
  depend directly on concrete runtime services.
- Declared resources and projected resources are related but not identical.
  Declared resources are stable authored, persisted, imported, or accepted
  resource identities. `GetResources()` currently also projects provider-
  observed and runtime-managed artifacts into the unified graph, so projection
  must not be treated as proof that a resource was explicitly declared.
  Projected/listed resources can still be referenced and may support provider-
  owned operations, but they should be considered read-only unless the owning
  provider implements management behavior for them.
- Resource definitions describe declared resource identity, resource type, and
  resource-specific intent through typed payloads and provider-owned
  attributes. The definition structure should remain separate from JSON, YAML,
  builders, persistence records, or other serialized format projections.
- Shared application services are implementation support for application-like
  resources: process/container spawning, runtime state tracking, logs,
  environment-variable resolution, reusable projection helpers, and app-owned
  stores. They should not own Resource Manager deployment, revision, lifecycle,
  or replica-management semantics.
- Application provider infrastructure should be the common toolkit for
  application-like resource providers. Implementors such as Container app,
  Executable app, and ASP.NET Core Web project own their unique configuration,
  lifecycle, validation, and projection policy as that behavior is separated
  from shared support.
- The shared Application Resource Provider infrastructure must not become a
  catalog for application resources or logs. It may provide reusable projection
  and log helpers, but the resource-type integration should decide what is
  projected and which concern receives or lists projected resources.
- Treat the Application Resource Provider infrastructure as if it could move
  to a shared library, while provider implementors that use, extend, or
  dogfood that infrastructure can live in separate assemblies. Shared
  infrastructure should therefore not construct provider-specific policy by
  default.
- Container app configuration revisions and Resource Manager environment
  revisions are separate concepts. Container app revisions track app
  configuration snapshots; environment revisions track materialized hosting
  environment outcomes.
- Replica groups and replica slots are orchestration concepts. Container apps
  define requested runtime state; Resource Manager deployment/orchestration
  reconciles replica groups and records outcomes.
- Resource templates are ResourceDefinition envelopes. They should not wrap
  normal user-authored resource state in deployment-shaped DTOs, and they
  should not require provider-specific serializers once a resource type can
  round-trip through the graph definition format.
- Orchestrator services, replica groups, replicas, routing bindings,
  deployment attempts, and environment revisions are Runtime model artifacts.
  They are internal orchestration boundaries unless a later
  workload-builder scenario deliberately exposes them. The Environment Map can
  visualize them as a read model, but it must not become a second source of
  truth.
- Container app DNS and virtual-network endpoint intent belongs to the stable
  container app service boundary by default. Replica-specific DNS names are a
  future diagnostic concern. Sticky/session-affinity routing is app-level
  resource intent projected into service-routing binding metadata, while
  provider-specific runtime enforcement remains a routing-provider task.

## Active Slice

- [x] Revise the Resource model migration plan and keep `docs/roadmap.md`,
  `docs/proposals/README.md`, resource template docs, deployment docs, and
  container app docs aligned around the same architecture story.
- [x] Add an internal Resource Manager deployment coordinator boundary for
  graph-backed apply paths so accepted ResourceDefinition changes can produce
  orchestrator deployments without bypassing deployment records, locking,
  previous replica-group lookup, routing reconciliation, or cleanup policy.
- [x] Replace the old provider-specific resource template engine with the
  ResourceDefinition-native `ResourceTemplate` public manager contract. Remove
  `ResourceGroupTemplate`, `ResourceTemplateDefinition`,
  `IResourceTemplateProvider`, and application/configuration/secrets
  provider-owned template serializers.
- [x] Investigate `ApplicationResourceService` responsibilities and current
  deployment/orchestration paths.
- [x] Route deployment-capable `Start` actions through Resource Manager
  deployment apply so first materialization creates deployment history and an
  environment revision baseline.
- [x] Remove provider-owned live replica scaling from the shared application
  service. Scaling updates container app intent; Resource Manager deployment
  apply reconciles replica groups.
- [x] Document the provider/application-service boundary in the container app
  and deployment proposals.
- [x] Commit the deployment-backed start and scaling boundary slice.
- [x] Split resource procedure follow-up signals so runtime reconciliation is
  distinct from restart-required UI prompts.
- [x] Extract container app revision numbering/history behavior into a
  dedicated revision unit with direct unit tests, while keeping
  `ApplicationResourceService` call sites stable for this slice.
- [x] Extract application workload configuration mapping into a dedicated
  factory with direct tests for workload kind selection, common runtime
  attributes, and replica-mode behavior.
- [x] Extract deterministic container app orchestrator deployment shape into a
  dedicated factory with direct tests for service identity, deployment inputs,
  revision scoping, and status mapping.
- [x] Extract application runtime state projection/transient lifecycle tracking
  into a dedicated tracker with direct tests for fresh/expired transient state,
  running fallback, and clear-starting/clear-stopping behavior.
- [x] Document local Docker daemon crash handling for Docker-backed
  verification runs.
- [x] Extract application resource projection attributes/capabilities into a
  dedicated projection factory with direct tests.
- [x] Move ASP.NET Core Web project runtime environment and process argument
  policy into ASP.NET Core Web project provider-owned units.
- [x] Move ASP.NET Core Web project endpoint defaulting and launch-settings
  endpoint discovery into ASP.NET Core Web project provider-owned units.
- [x] Move ASP.NET Core Web project definition normalization into an ASP.NET
  Core Web project provider-owned rule.
- [x] Split shared container-backed normalization from container app revision
  and replica normalization, and keep provider-specific normalization composed
  by the application provider extension instead of the shared normalizer
  fallback.
- [x] Extract container app image-deployment planning into a container-app
  unit that owns definition mutation plus deployment/revision history records,
  while leaving runtime restart and persistence coordination in the current
  facade for this slice.
- [x] Extract container app replica-scaling planning into a container-app unit
  that owns requested replica intent changes while runtime reconciliation
  remains coordinated by the current facade.
- [x] Extract container app deployment failure planning into a container-app
  unit that owns failed-deployment base revision lookup and rollback state.
- [x] Extract container app deployment tear-down planning into a container-app
  unit that owns superseded revision and legacy stable replica group cleanup
  decisions.
- [x] Extract container app runtime revision scoping policy into a
  container-app unit that owns when replica runtime names should include the
  active app revision.
- [x] Extract container app deployment-applied planning into a container-app
  unit that owns recording the materialized environment revision on the app
  definition.
- [x] Move container app deployment store, revision service, and orchestrator
  deployment factory files under the `ContainerApp` provider directory so the
  source layout reflects provider-owned implementation.
- [x] Extract top-level application resource projection into a reusable
  toolkit helper, without making shared application infrastructure the
  resource inventory owner.
- [x] Add resource graph membership metadata so Resource Manager projections
  can distinguish stable declared/registered resources from provider-projected
  artifacts while keeping them in one graph.
- [x] Extract application resource naming helpers for stable identifiers and
  runtime container resource IDs so projection, logs, observability, and
  runtime child-resource concerns can share the same naming boundary.
- [x] Align log provider contracts and docs with the `LogSource` direction.
- [x] Move application resource log discovery to source-first `LogSource`
  projection.
- [x] Keep resource activity log discovery source-first.
- [x] Move Configuration Store and Secrets Vault service log discovery to
  source-first `LogSource` projection.
- [x] Move Docker host diagnostics and container log discovery to source-first
  `LogSource` projection.
- [x] Simplify the Control Plane log-source catalog so source discovery merges
  resource declarations with contributed `LogSource` records.
- [x] Remove legacy `LogDescriptor` discovery support from provider, store,
  manager, API, and remote-client contracts.
- [x] Rename provider runtime log access methods to source-addressed
  `ReadLogSourceAsync` and `StreamLogSourceAsync`.
- [x] Rename log selection routes and component parameters from `logId` to
  `logSourceId` so the UI contract matches the source-first log model.
- [x] Extract application container-host resolution into
  `ApplicationContainerHostResolver` so runtime, logging, and future provider
  units can share host selection without depending on the full application
  resource service.
- [x] Extract application log-source discovery and reads into
  `ApplicationLogProvider` so application logging is no longer owned by the
  shared application resource service.
- [x] Extract SQL Server declared database child-resource projection into
  `SqlServerDatabaseResourceProjector`.
- [x] Extract application infrastructure projection profile selection into
  `ApplicationResourceProjectionProfiles`.
- [x] Extract application local port allocation into
  `ApplicationResourcePortResolver`.
- [x] Remove the unused SQL database normalization partial from
  `ApplicationResourceService`.
- [x] Move provider UI pages off direct `ApplicationResourceService`
  injection by introducing focused application management, container history,
  and SQL database inspection operation contracts.
- [x] Move container app deployment and revision history reads into
  `ApplicationContainerHistoryService`.
- [x] Move SQL Server database inspection reads into
  `SqlServerDatabaseInspectionService` and keep shared SQL connection behavior
  in a focused helper.
- [x] Move the SQL Server credential API route off direct
  `ApplicationResourceService` dependency by introducing a focused credential
  resolution operation contract.
- [x] Extract SQL Server managed credential naming into a shared helper used by
  credential resolution, grant status, and reconciliation.
- [x] Move SQL Server credential resolution into
  `SqlServerCredentialResolutionService`.
- [x] Move SQL Server permission grant status inspection into
  `SqlServerGrantStatusService`.
- [x] Move SQL Server database and access reconciliation into
  `SqlServerDatabaseReconciliationService`.
- [x] Move application resource definition lookup for resource-type providers
  into `ApplicationResourceDefinitionSource`.
- [x] Add `IApplicationResourceRunningStateOperations` so SQL services can
  depend on running-state checks without depending on management operations.
- [x] Add `IApplicationResourceConfigurationOperations` so application
  configuration UI depends on definition lookup, updates, and running-state
  checks without depending on the full management facade.
- [x] Add `IApplicationResourceRegistrationOperations` so application
  registration UI depends on definition lookup and setup without depending on
  the full management facade.
- [x] Move remaining application provider UI pages off broad application
  management operations by reusing focused definition, running-state, and
  configuration contracts.
- [x] Remove the unused broad `IApplicationResourceManagementOperations`
  contract once consumers moved to focused operation contracts.
- [x] Move application registration operations to a focused adapter over
  definition lookup and definition registration instead of the shared
  application resource service facade.
- [x] Move application configuration operations to a focused adapter over
  definition lookup, running-state checks, and definition registration instead
  of the shared application resource service facade.
- [x] Remove application definition-source contract implementation from the
  shared application resource service; keep `ApplicationResourceDefinitionSource`
  as the provider-facing boundary.
- [x] Move application declaration operations to a focused adapter over
  declared provider options, definition lookup, and registration operations
  instead of the shared application resource service facade.
- [x] Move application template import/export operations to a focused adapter
  over definition lookup and registration operations instead of the shared
  application resource service facade.
- [x] Move host-scoped application process cleanup to a focused cleanup
  provider instead of the shared application resource service facade.
- [x] Move SQL Server reconcile-access action ID constants out of the shared
  application resource service.
- [x] Move application app-setting and environment-variable configuration
  provider behavior to a focused settings provider instead of the shared
  application resource service facade.
- [x] Move application process and container monitoring behavior to a focused
  monitoring provider instead of the shared application resource service
  facade.
- [x] Move application orchestration descriptor behavior to a focused
  descriptor provider and extract workload intent mapping into
  `ApplicationWorkloadConfigurationProvider`.
- [x] Move container runtime process tracking into
  `ApplicationContainerProcessTracker` and route
  `IApplicationResourceRunningStateOperations` through a focused running-state
  operation instead of the shared application resource service facade.
- [x] Move application resource graph projection and runtime container child
  projection into `ApplicationResourceProjectionSource` instead of the shared
  application resource service facade.
- [x] Rename the remaining runtime/procedure coordinator from
  `ApplicationResourceService` to `ApplicationResourceRuntimeOperations` so the
  old catch-all service is no longer a required provider-facing concept.
- [x] Move application configuration-setting and secret setting resolution into
  `ApplicationResourceSettingResolver` so preflight checks and runtime
  execution share a focused resolver instead of runtime coordinator internals.
- [x] Move application action availability and start/restart preflight checks
  into `ApplicationResourceActionAvailabilityOperations` so resource-type
  providers can query action capability without depending on runtime procedure
  execution.
- [x] Move application workload and runtime environment-variable composition
  into `ApplicationResourceEnvironmentVariableResolver` so workload
  descriptors and process/container startup share one provider-neutral
  resolver.
- [x] Move application volume mount materialization and validation helpers into
  `ApplicationResourceVolumeMounts` so action availability and runtime startup
  do not depend on runtime coordinator static helpers.
- [x] Move container app image and replica update intent operations into
  `ContainerApplicationUpdateOperations`, leaving lifecycle execution and
  orchestration hooks in the runtime/procedure coordinator for this slice.
- [x] Move container app deployment description into
  `ContainerApplicationDeploymentDescriptionOperations`, separating Resource
  Manager deployment shape projection from runtime service execution.
- [x] Move container app orchestrator service description into
  `ContainerApplicationOrchestratorServiceDescriptionOperations`, separating
  Resource Manager service capability and service shape creation from runtime
  service instance execution.
- [x] Move container-backed application service preparation into
  `ApplicationContainerOrchestratorServicePreparationOperations`, separating
  registry login, network preparation, and replicated-app ingress stop
  preparation from runtime service instance execution.
- [x] Move container image materialization into
  `ApplicationContainerImageMaterializer`, separating project container
  publish, Dockerfile build, and shared replica build caching from runtime
  procedure coordination.
- [x] Move container app deployment outcome handling into
  `ContainerApplicationDeploymentOutcomeOperations`, separating post-apply,
  failed-apply, and tear-down planning from runtime service execution.
- [x] Move replicated container-app ingress configuration, start, update, and
  stop handling into `ContainerApplicationIngressOperations` so routing
  reconciliation has a container-app-owned boundary instead of living in the
  shared runtime/procedure coordinator or service-preparation code.
- [x] Move container app replica Docker run command construction into
  `ContainerApplicationContainerRunCommandFactory`, separating runtime command
  translation from process tracking, readiness, and lifecycle coordination.

## Next Slices

- [x] Rework the resource template engine around `ResourceTemplate` containing
  `ResourceDefinition` entries. Remove `ResourceDeploymentDefinition` and
  other deployment-shaped user-authoring wrappers unless an internal
  orchestration API explicitly owns them.
- [x] Move the combined development host off the legacy Applications,
  Configuration, and Docker provider extensions and install built-in Resource
  model providers plus graph Resource Manager integration by default.
- [x] Add package-owned `UseBuiltInResourceModelProviders(...)` and
  `AddBuiltInResourceModelProviderTypes(...)` registration seams so the
  Control Plane side of the combined development host installs the default
  built-in provider catalog and graph bridge without hand-maintaining a
  scattered provider list.
- [x] Split hosting registration names so UI-only hosts use
  `AddCloudShellUi()`, `UseCloudShellUiAsync()`, and `MapCloudShellUi(...)`,
  Control Plane hosts use the Control Plane methods, and combined local hosts
  compose both sides explicitly instead of using ambiguous plain
  `AddCloudShell()`, `UseCloudShellAsync()`, or `MapCloudShell(...)` methods.
- [x] Clarify the preferred local-development registration story: install the
  Control Plane application first, add CloudShell UI explicitly, and register
  backend provider extensions separately from Resource Manager UI extensions.
- [x] Remove legacy provider project references from remaining samples where
  built-in Resource model providers already cover the scenario.
- [x] Move reusable sample-local gaps behind Resource model
  provider-owned runtime seams instead of keeping the old provider projects
  installed for general host behavior. The current pass moved container app
  local Docker runtime projection, Traefik load-balancer configuration,
  CoreDNS zone-file publishing, fixed local executable orchestration
  descriptors, and SQL Server/database runtime helpers into provider-owned
  adapters while leaving scenario-specific sample providers in place.
- [x] Delete the old provider implementation folders once no active host,
  sample, or test requires `CloudShell.Providers.Applications`,
  `CloudShell.Providers.Configuration`, or `CloudShell.Providers.Docker`.
- [x] Rename `CloudShell.ResourceModel*` packages after the migration is
  complete so the new provider stack is presented as the default Resource
  model implementation rather than as a prototype.
- [x] Define the Resource Manager apply path for incremental
  `ResourceDefinition` updates: target resolution, provider validation,
  accepted graph-state commit, runtime-planning trigger, diagnostics, and
  rollback behavior for failed validation or failed runtime materialization.

## Old Provider Dependency Inventory

Use this inventory before deleting old provider folders. The removal sequence is
dependency-first: remove one host/project dependency, build that host, then run
the graph-backed tests that cover the same resource path.

- [x] Combined development host no longer references
  `CloudShell.Providers.Applications`, `CloudShell.Providers.Configuration`,
  or `CloudShell.Providers.Docker`. It installs Resource model reference
  providers and graph Resource Manager integration by default.
- [x] `CloudShell.ConfigurationStoreService` and
  `CloudShell.SecretsVaultService` referenced
  `CloudShell.Providers.Configuration` for old DTOs. Move the service file
  contracts to service-local DTOs or shared Resource model runtime
  contracts, then remove the old project reference.
- [x] `samples/ApplicationTopology/Host` referenced the old
  Configuration and Docker provider projects. Verify whether these are stale
  project references/usings after the Resource model sample migration,
  remove them, and run ApplicationTopology graph smoke coverage.
- [x] `samples/CloudShell.ContainerHost` referenced the old Docker
  provider project. The provider-owned local SQL Server Docker runtime now
  covers the runtime behavior, the stale dependency has been removed, and
  ContainerHost graph smoke coverage verifies the path.
- [x] `CloudShell.Abstractions.Tests` contained broad tests for the old
  provider model. Move behavior that must survive to
  `CloudShell.ResourceModel.Tests` or sample tests, then delete tests
  that only preserve old provider registration/template behavior. Removing the
  old provider references from `CloudShell.Host` exposed this coupling because
  those tests were relying on the combined host's transitive provider
  references rather than declaring their own boundary.
- [x] Remove `CloudShell.Providers.Applications`,
  `CloudShell.Providers.Configuration`, and `CloudShell.Providers.Docker` from
  `CloudShell.slnx` once no active host, sample, or
  service project references them.
- [x] Audit old provider folders and excluded old-provider tests as the
  migration backlog for the new provider packages. Move forward only reusable
  runtime/toolkit pieces that the Resource model providers still need:
  local process execution, container command helpers, log parsing, runtime
  monitoring, endpoint/probe projection, environment-variable resolution,
  volume mount materialization, container host resolution, and any
  provider-owned runtime handlers that have not yet been rebuilt. Bring those
  pieces forward only behind provider adapter contracts or a default runtime
  integration package. Do not bring forward old provider-owned definition
  stores, template serializers, registration pages, direct host/runtime
  dependencies, or `ApplicationResourceDefinition` as the public declaration
  shape.
- [x] Delete the old `CloudShell.Providers.Applications`,
  `CloudShell.Providers.Configuration`, and `CloudShell.Providers.Docker`
  implementation folders after active hosts, samples, services, solution files,
  and tests moved off them.
- [x] Add merge-readiness hygiene coverage that prevents active solution,
  project, and source files from reintroducing deleted legacy provider
  projects or old resource-template wrapper contracts.
- [x] Keep `CloudShell.Providers.DockerCompose` out of this deletion pass
  unless it starts exposing the old resource provider model. It is currently an
  orchestrator/provider integration, not one of the old resource-provider
  packages being removed.

## Deferred Orchestration Cleanup

The following work stays parked until the provider migration/removal track is
stable enough that container app runtime behavior can be revisited without
preserving old provider seams:

- [ ] Finish moving container app first start, scale-in, scale-out, routing
  rebinding, and cleanup onto the internal deployment-controller path derived
  from accepted graph resource state. Image and replica-slot updates now enter
  that path through ResourceDefinition apply; lifecycle and cleanup seams still
  need consolidation.
- [x] Define the routing/load-balancer reaction boundary for replica-group
  changes by carrying service-routing binding definitions into the
  orchestrator routing reconciliation context.
- [x] Forward service-routing binding definitions through the Resource Model
  graph procedure bridge and container-app orchestrator runtime handler
  contract so runtime adapters can react to explicit binding ids.
- [x] Project container app session-affinity resource intent into
  service-routing binding definitions, and expose the setting on the
  app-centric Scale and replicas UI.
- [ ] Make the default orchestrator controller and load-balancer providers
  react to service-routing binding definitions instead of inferring replica
  membership from container-app-specific runtime names.
- [x] Project the initial network topology overlay into graph views from
  resource-owned endpoint mappings, network resources, load-balancer routes,
  name mappings, and explicit or observed internet reachability facts. The
  Environment Map shows the overlay by default with a toggle; the Resource
  graph has an optional overlay, and both views render internet reachability as
  a resource or network badge. When the Environment Map shows an Internet
  anchor, it links to the carrier boundary instead of every reachable resource.
- [ ] Continue splitting `ApplicationResourceRuntimeOperations` by separating
  remaining resource-type concerns: lifecycle procedure execution, container
  app orchestration hooks, and endpoint/probe materialization. The shared
  application infrastructure should provide reusable toolkit pieces, not be
  the runtime coordinator.
- [ ] Define the Resource Manager distinction between declared resource
  inventory and provider/runtime projections before changing `GetResources()`
  semantics. The unified graph can still include both, but code should know
  whether it is handling a declared resource or a projected artifact.
- [ ] Define reference semantics for declared resources and projected/listed
  resources, including when a provider may resolve and project a referenced
  artifact on demand and what capabilities make a projected resource
  manageable instead of read-only.
- [ ] Define the resource definition structure used for declarations and
  deployment inputs: identity, type, optional definition version, typed payload,
  provider-owned attributes, and validation by the owning resource provider.
- [x] Add a diagram to the provider/application-resource docs showing the
  layering from raw Resource Provider infrastructure to Application Resource
  Provider infrastructure and the dogfooded implementors: Container app,
  Executable app, and ASP.NET Core Web project.
- [x] Move provider UI pages off direct `ApplicationResourceService` injection
  where they can depend on concrete provider operations or Resource Manager
  managers instead.
- [ ] Move remaining container-app-specific Resource Manager semantics into
  container-app-owned operation services instead of delegating all behavior to
  the shared application service facade.
- [x] Split container app image and replica update operations behind
  `IContainerApplicationUpdateOperations`.
- [x] Split container app deployment description behind
  `IContainerApplicationDeploymentDescriptionOperations`.
- [x] Split container app orchestrator service description behind
  `IContainerApplicationOrchestratorServiceDescriptionOperations`.
- [x] Split container-backed application service preparation behind
  `IApplicationContainerOrchestratorServicePreparationOperations`.
- [x] Extract container image materialization and shared replica build caching
  from the runtime/procedure coordinator.
- [x] Split container app deployment outcome handling behind
  `IContainerApplicationDeploymentOutcomeOperations`.
- [x] Extract container app ingress/routing reconciliation support from the
  shared runtime/procedure coordinator.
- [x] Extract container app replica container run command construction from
  the shared runtime/procedure coordinator.
- [x] Extract the container app orchestrator deployment factory from the shared
  service so deployment description is independently testable.
- [x] Revisit post-apply teardown ownership. Prefer Resource Manager
  deployment/orchestration outcome data over provider-specific predecessor
  inference.
- [x] Define a provider-facing change-application contract for applying
  attribute/configuration changes to materialized resources without requiring
  every provider to invent one-off update methods.
- [ ] Define service-discovery semantics for virtual networks, DNS zones, name
  mappings, and endpoint mappings so graph and runtime views can distinguish
  declared topology, DNS/discovery names, and internet-reachable carrier
  networks without provider-specific inference.
  - [x] Add a manual Resource model proof for virtual-network-private service
    IPs and DNS name mappings through the HostVirtualNetwork sample.
  - [ ] Define automatic virtual IP allocation and default service-discovery
    naming policy for services inside a virtual network.

## Future Resource Provider Refactoring

- [ ] Establish the provider execution boundary strategy before adding remote
  agents. The current resource type provider and handler architecture is the
  baseline: providers own resource semantics, operation providers expose the
  domain action, and handlers/reconcilers/run commands perform host-local
  runtime work behind focused contracts. The near-term refactor should improve
  those contracts so execution remains in-process now but can later be
  extracted into agents without changing the resource model, operation
  providers, or Resource Manager semantics.
  - [ ] Build an extractable in-process execution infrastructure first. The
    first implementation should run inside the Control Plane host through
    dependency injection, but Control Plane services should call a narrow
    provider execution port instead of directly depending on concrete runtime
    handlers or host command runners.
  - [ ] Make execution agent-targetable before introducing agents. Resource
    Manager should be able to produce typed execution instructions that
    identify the operation, target resource, and required capabilities, while
    the current dispatcher
    still resolves an in-process handler through local service registration.
    This is the MVP transition point between direct Control Plane execution
    and a future remote agent transport.
  - [ ] Keep remote execution concerns deferred while still designing for
    them. Do not introduce agent processes, transport protocols, host
    registration, cluster scheduling, or distributed leases until at least two
    existing handlers use the new shape locally, but avoid contracts that
    assume same-process execution, ambient service access, local filesystem
    paths, or direct process handles.
  - [ ] Treat the execution boundary as a provider-side assignment shape, not
    as a new top-level resource concept. The assignment-shaped instruction
    should name the operation, target resource, desired generation or
    revision, idempotency key, required capabilities, and provider-owned
    payload.
    Resource definitions should not name an agent or execution participant for
    the normal local and single-host case; the Control Plane derives the
    execution target from the host profile, provider capability, and later
    placement policy only when more than one participant exists.
  - [ ] Defer explicit region and data-center topology until after the agent
    transition. The first execution boundary should assume one implicit
    default execution target; later placement metadata can add regions,
    failure domains, and capacity pools without changing resource definitions
    for local development.
  - [ ] Treat handler results as observations. Results should carry observed
    status, observed generation, provider-owned observations, diagnostics, and
    enough correlation data for Resource Manager to relate them to the
    requested desired state.
  - [ ] Preserve the existing Control Plane authority model. Resource Manager
    validates and records desired state, evaluates authorization and action
    capability, coordinates provider operations, and derives resource status.
    Handlers make a local assignment true; they do not decide global placement
    or own the resource graph.
  - [ ] Keep resource type providers and handlers as the extension model. The
    refactor should add a clearer execution port between provider planning and
    host runtime work, not replace provider packages with an agent-specific
    plugin model.
  - [ ] Start with handlers that already look like reconciliation boundaries,
    such as DNS/name mapping or virtual-network endpoint mapping, before
    adapting Docker-backed lifecycle handlers. This keeps the first contract
    focused on desired-versus-observed state rather than container-runtime
    complexity.
- [ ] Define resource attribute schemas across provider-owned resource
  type/kind/class boundaries, including scalar and complex values, so
  Resource Manager can understand resource intent without hard-coding
  provider-specific attributes.
  - [x] Split canonical attribute identity from authored/projection path at
    the attribute-definition contract level.
    `ResourceAttributeId` should remain the provider/runtime schema key, while
    `ResourceAttributeDefinition` can declare optional document paths, aliases,
    display names, and descriptions. Existing dotted IDs remain compatibility
    input; new schema work should not rely on the ID string alone to encode
    hierarchy or grouping. Import/export behavior still needs to consume the
    new metadata.
  - [x] Add a schema-local resource attribute path resolver that maps
    canonical IDs, authored paths, and aliases to canonical
    `ResourceAttributeId` values while reporting ambiguous paths instead of
    guessing.
  - [x] Add opt-in resource definition canonicalization that rewrites
    schema-local authored attribute paths to canonical IDs and reports
    ambiguous paths or duplicate inputs as diagnostics.
  - [x] Allow attribute path resolution to compose resource type/class
    definitions with selected capability definitions, so capabilities can make
    reusable authored attributes valid for a resource without making the
    resource type own those attribute IDs.
  - [x] Add a capability attribute-provider contract and wire it into
    ResourceDefinition resolution so selected class/type/resource capabilities
    can contribute inherited attribute definitions, path canonicalization, and
    value validation.
  - [x] Preserve canonical attribute IDs as the disambiguation form when
    authored paths or aliases collide, while keeping authored-path reuse local
    to the composed resource schema.
- [ ] Define provider validation contracts for attributes and capabilities.
  Providers should be able to validate whether a declared or deployed resource
  state conforms to the provider-supported schema and capability set.
- [ ] Define provider apply contracts for attribute changes. A provider should
  own how validated resource intent maps to its runtime target, whether that
  target is an executable, container, orchestrator service, database, or other
  managed resource.
- [ ] Audit built-in-provider runtime implementations and move reusable
  or host-shaped implementations behind default runtime integration
  registrations, leaving provider packages dependent only on adapter contracts
  and fakeable abstractions.
  - [x] Collapse repeated local Docker/SQL runtime plus local-executable
    descriptor registration into provider-owned runtime registration overloads
    so samples do not wire descriptor providers separately for the same
    resource id.
- [ ] Define a provider execution boundary for host-local runtime work. The
  boundary should describe typed provider execution instructions, desired generation,
  observed state, diagnostics, idempotency, and lease-friendly execution
  metadata without introducing remote agents yet. The goal is to let current
  in-process runtimes and future agents execute the same provider-side
  assignment shape.
  - [x] Add the first provider execution contract types in the provider
    runtime layer: dispatcher, handler, assignment-shaped instruction request,
    observed result, status, capability IDs, and instruction type IDs. The
    contract is transport-neutral and does not require resource definitions to
    specify an agent, host, or region.
  - [x] Add the in-process provider execution dispatcher and register it with
    built-in runtime adapters so Control Plane services can target the
    provider execution port while handlers still run inside the same process.
    Missing operation handlers and missing required capabilities return
    unavailable observed results with diagnostics instead of requiring callers
    to know concrete runtime services.
  - [x] Add execution target metadata to provider execution requests while
    keeping the default target implicit. The in-process dispatcher executes the
    implicit/default and local in-process targets, and returns unavailable for
    future agent targets until an agent transport is introduced.
  - [x] Route the Network endpoint-mapping reconcile operation through the
    provider execution dispatcher. The operation now creates an
    assignment-shaped instruction with the current resource graph snapshot,
    and the in-process network execution handler adapts that instruction to
    the existing endpoint-mapping reconciler while preserving diagnostics.
  - [x] Route the DNS zone name-mapping reconcile operation through the
    provider execution dispatcher with the same snapshot-backed instruction
    pattern and a DNS-specific in-process execution handler.
  - [x] Route Local Volume provisioning through the provider execution
    dispatcher as the first filesystem/materialization instruction. The
    operation now targets the same assignment-shaped request model and the
    in-process handler adapts that request to the existing volume provisioner.
  - [x] Route SQL Server access reconciliation through the provider execution
    dispatcher so database access materialization also goes through the
    instruction/handler boundary while keeping orchestration decisions in the
    Control Plane.
  - [x] Route RabbitMQ access reconciliation through the provider execution
    dispatcher with grant data carried as instruction payload, exercising the
    payload path needed for future agent-backed execution.
  - [x] Route Virtual Network endpoint-mapping reconciliation through the
    provider execution dispatcher with a distinct virtual-network instruction
    so multiple endpoint handlers can coexist without dispatcher ambiguity.
  - [x] Route Docker container lifecycle execution through the provider
    execution dispatcher with in-process handlers for start, stop, pause,
    restart, and unpause. This keeps local Docker command execution behind the
    same instruction/result boundary used by reconciliation-style handlers.
  - [x] Route SQL Server lifecycle execution through the provider execution
    dispatcher with in-process handlers for start, stop, and restart. The
    operation still evaluates local runtime status for current capability
    behavior, but runtime materialization now crosses the dispatcher boundary.
  - [x] Route RabbitMQ lifecycle execution through the provider execution
    dispatcher with in-process handlers for start, stop, and restart, matching
    the managed service lifecycle shape now used by SQL Server.
  - [x] Route local and macOS host-network endpoint-mapping reconciliation
    through the provider execution dispatcher with platform-specific
    in-process handlers. This keeps host networking behind the same
    instruction/result boundary without introducing remote agents yet.
  - [x] Route Event Broker lifecycle execution through the provider execution
    dispatcher with in-process process handlers for start, stop, and restart.
    This is the first process-backed managed service to cross the
    instruction/result boundary.
  - [x] Route Configuration Store lifecycle execution through the provider
    execution dispatcher with in-process process handlers for start, stop, and
    restart, matching the Event Broker process-backed boundary shape.
  - [x] Route Secrets Vault lifecycle execution through the provider execution
    dispatcher with in-process process handlers for start, stop, and restart.
  - [x] Add in-memory provider execution observations for dispatched
    instructions. The dispatcher records assignment id, target resource,
    instruction type, desired generation, target, observed status, diagnostics,
    observations, and timestamps for successful and unavailable in-process
    execution results.
  - [x] Route Container Application lifecycle execution through the provider
    execution dispatcher with in-process handlers for start, stop, and restart.
    Image and replica updates remain separate follow-up slices because they
    use distinct runtime handler methods.
  - [x] Route Container Application image and replica materialization through
    the provider execution dispatcher with in-process handlers for
    `ApplyImageAsync` and `ApplyReplicasAsync`, including deployment
    reconciliation paths that previously called the runtime handler directly.
  - [x] Route Container Application orchestrator routing reconciliation
    through the provider execution dispatcher with a typed routing payload,
    establishing the payload shape for the remaining Container Application
    orchestration hooks.
  - [x] Route Container Application orchestrator service prepare and routing
    teardown through the provider execution dispatcher with the same typed
    orchestrator-service payload. Replica-instance execution remains the next
    Container Application orchestration hook still calling the runtime handler
    directly.
  - [x] Route Load Balancer configuration apply through the provider
    execution dispatcher with an in-process handler for the existing
    configuration applier, keeping Traefik/file-provider materialization behind
    the same instruction/result boundary.
  - [x] Route CloudShell Volume provisioning through the provider execution
    dispatcher with an in-process storage provision handler, while keeping
    volume intent validation in the operation provider.
  - [x] Route Executable Application start through the provider execution
    dispatcher with an in-process process handler, keeping process startup
    behind the same boundary without introducing remote agents.
  - [x] Inventory current execution boundaries. Public domain contracts live
    in `CloudShell.Abstractions` and `CloudShell.ResourceModel`; the Control
    Plane owns stores, managers, API projection, orchestration, and platform
    reconciliation; built-in provider packages own resource semantics plus
    local runtime handlers; host projects install concrete runtime
    integrations. This is directionally correct.
  - [x] Identify weak seams. Runtime operation interfaces such as
    `IContainerApplicationRuntimeHandler`, `IDockerContainerRuntimeHandler`,
    `ISqlServerRuntimeHandler`, `IVirtualNetworkEndpointMappingReconciler`,
    and `IDnsZoneNameMappingReconciler` already separate operation providers
    from runtime execution, but most still accept broad `Resource` input and
    return only diagnostics. They do not yet expose typed execution requests,
    desired generation, observed state, idempotency keys, or lease metadata.
  - [x] Inventory direct runtime command paths that should stay behind this
    boundary: local Docker container app runtime bridge and command runner,
    Docker container runtime handler, SQL Server local Docker runtime,
    RabbitMQ local Docker runtime, Traefik/load-balancer runtime, local
    hostname publishing/resolver refresh, virtual-network endpoint mapping,
    and process-backed ASP.NET Core, executable, JavaScript, Java, Go, Python,
    configuration-store, secrets-vault, event-broker, and device-registry
    runtime controllers.
  - [x] Introduce a small execution contract in the provider/runtime layer,
    not as an agent API yet. A first version should model instruction type,
    target resource id, desired generation or revision, capability
    requirements, idempotency key, and provider-owned payload, with a result
    that carries observed status, observed generation, diagnostics, and
    provider-owned observations.
  - [x] Clarify the naming boundary between resource-domain operations and
    provider execution instructions. `ResourceOperationId` remains for
    behavior exposed on a resource. Dispatcher-only reconciliation work uses
    execution keys and instruction types instead of introducing internal
    resource operations.
  - [x] Pick one low-risk operation and introduce the first typed execution
    request/result contract around the existing handler without changing user
    behavior. Good candidates are DNS/name-mapping reconcile or virtual-network
    endpoint-mapping reconcile because they already have explicit reconcile
    operations, observation attributes, and provider boundaries.
  - [x] Make the first contract report observed generation/status and stable
    diagnostics so Resource Manager can reason about desired-versus-observed
    state before any remote agent exists.
  - [x] After the first networking-style operation proves the shape, adapt one
    container-backed lifecycle operation, such as SQL Server local Docker
    Start/Stop or Docker container lifecycle, so Docker command execution
    moves behind the same typed request/result pattern.
  - [x] Decide not to persist assignments in Control Plane operational state
    yet. Keep observations in memory while the local boundary is still being
    proven; do not introduce remote agents, host placement, or distributed
    leases before Container Application execution also crosses this boundary.
  - [x] Document future execution handler strategy by resource category,
    including which behaviors should become generic execution capabilities and
    which should remain provider-specific adapters, plus the first
    storage/volume placement rules for host-bound versus shared volumes.
- [ ] Feed the schema/validation/apply model into orchestrator deployment
  planning so accepted ResourceDefinition state can be translated consistently
  across resource types while leaving type-specific reconciliation to the
  owning provider. Deployment definitions may reference accepted resource
  state or carry normalized runtime definitions for services, replica groups,
  replicas, and routing bindings, but user-authored resource intent remains in
  `ResourceDefinition` entries and `ResourceTemplate` envelopes.

## Environment and UI Follow-Ups

- [ ] Keep the Environment page as a diagnostic projection over Resource
  Manager deployment/orchestration state. Do not let it become a second source
  of truth.
- [ ] Move reusable Environment read-model/projection logic out of `.razor`
  pages when it grows beyond simple UI composition.
- [ ] Keep container app UI app-centric. Link to Environment diagnostics when
  orchestration detail is useful, but do not require users to understand
  deployment/environment-revision internals for the default workflow.

## Verification Expectations

- Run focused tests for the owning layer first.
- For cross-boundary Resource Manager/provider changes, run:
  `dotnet test CloudShell.ControlPlane.Tests/CloudShell.ControlPlane.Tests.csproj --no-restore`
  `dotnet test CloudShell.ControlPlane.Client.Tests/CloudShell.ControlPlane.Client.Tests.csproj --no-restore`
  `dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore`
  `dotnet test CloudShell.Sample.Tests/CloudShell.Sample.Tests.csproj --no-restore`
  `dotnet build CloudShell.slnx --no-restore`
- If Docker-backed sample tests fail before reaching CloudShell behavior
  because the Docker daemon is unavailable, follow `CONTRIBUTIONS.md`: verify
  with `docker info`, restart or unblock Docker, and record the blocked
  Docker-dependent verification instead of treating it as a product regression.
