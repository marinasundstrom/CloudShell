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
- [x] Move application configuration-entry and secret setting resolution into
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
  Control Plane hosts use the Control Plane methods, and the plain
  `AddCloudShell()`, `UseCloudShellAsync()`, and `MapCloudShell(...)` methods
  live in the combined host surface that composes both sides.
- [x] Remove legacy provider project references from remaining samples where
  built-in Resource model providers already cover the scenario.
- [ ] Move any remaining sample-local gaps behind Resource model
  provider-owned runtime seams instead of keeping the old provider projects
  installed for general host behavior.
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
  provider project. Verify whether the sample-local Docker bridge already
  covers the runtime behavior, remove the stale dependency, and run
  ContainerHost graph smoke coverage.
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

- [ ] Define resource attribute schemas across provider-owned resource
  type/kind/class boundaries, including scalar and complex values, so
  Resource Manager can understand resource intent without hard-coding
  provider-specific attributes.
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
