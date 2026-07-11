# Artifact implementation guidelines

This document defines implementation and verification expectations for
CloudShell artifacts. It is a practical checklist for agent and manual changes
that add, change, project, or verify resource-model artifacts.

Read this with [Domain model](domain-model.md),
[System design guidelines](system-design-guidelines.md), and the
[extension authoring overview](extensions.md). The domain model defines the
concepts. This document defines how to implement them consistently across the
public abstraction, Control Plane, API, remote client, Control Plane
providers, Resource Manager UI integrations, and shell UI extensions.

## Artifact contract

Every CloudShell artifact should have a clear owner, projection shape, and
verification path.

- Public artifacts live in `CloudShell.Abstractions` when shell integrations,
  extensions, samples, or remote clients need to consume them.
- Internal capabilities can start behind built-in services while the model is
  being proven. When the same capability benefits integrators or authored
  services, graduate it only if the platform owns the integration contract and
  is ready to maintain it as a public API. Otherwise keep it internal and avoid
  exposing incidental implementation details.
- Public APIs that are not yet stable must be labeled as preview,
  experimental, or unstable in code-facing documentation and product docs.
  Document the owner, expected change surface, and what must happen before the
  API is considered stable.
- Control Plane services own resource inventory, registration, grouping,
  lifecycle procedures, logs, templates, validation, authorization, and
  provider coordination.
- Providers own provider-specific configuration, runtime state, external
  system calls, and provider template payloads.
- Providers must access concrete host/runtime behavior through focused adapter
  interfaces registered by the host, sample, or default runtime package. A
  provider may own the contract for the operation it needs, but it should not
  depend directly on a concrete process launcher, Docker bridge, filesystem
  materializer, network reconciler, sidecar host, or orchestrator controller.
- API DTOs are versioned transport projections of established domain artifacts.
  They may add affordances such as `href` and `method`, but should not rename
  domain concepts into parallel Web API concepts.
- The remote client maps API DTOs back to the public domain shape with shallow,
  obvious mappings.
- The shell UI renders projected artifacts and capabilities. It should not own
  domain validation, lifecycle policy, or provider behavior.
- Resource Manager UI integrations are resource-specific shell UI extensions.
  They build on the base UI extension architecture but remain separate from
  non-UI Control Plane resource providers.
- Any Control Plane extension or resource provider should consider the
  matching CloudShell UI integration. The UI integration is not technically
  required for deployments that do not use CloudShell UI, but omitting it for a
  user-facing feature should be an explicit documented decision.

When changing an artifact, first answer:

1. What is the owning layer?
2. Is this projected state, desired intent, provider-owned configuration,
   runtime observation, or presentation?
3. Which manager or provider contract exposes it?
4. Does the HTTP API need a versioned projection?
5. Which tests prove the valid path and the important invalid or edge path?

## General rules

Use established domain nouns. If an API or UI change needs a concept that does
not exist in the public model, decide whether the domain model is missing an
artifact before adding transport-only shape.

Keep resources uniform. Do not add projected subclasses for containers,
executables, services, projects, infrastructure, networks, or provider-specific
resources. Use `ResourceClass`, `TypeId`, attributes, capabilities, endpoints,
actions, health, observability, and provider-owned descriptors.

Separate projected facts from intent. `ResourceEndpoint` describes what exists
now. `ResourceEndpointRequest` describes what should be assigned or reserved.
`ResourceAction` describes an operation that exists. `ResourceActionCapability`
describes whether it can execute now.

Separate platform-owned state from provider-owned state. Registrations,
groups, group assignments, and platform-declared dependencies are platform
state. Provider configuration, runtime state, credentials, log sources, and
provider template payloads remain behind provider contracts.

Prefer diagnostics and result objects for expected validation outcomes.
Exceptions are appropriate for programmer errors and boundary adapters that
translate invalid input into stable API errors.

Use stable IDs and invariant formatting. Identifiers, attribute names, action
IDs, capability IDs, endpoint names, template versions, and event types must be
stable and non-localized.

Never project secrets through resource artifacts. Secret values belong in
provider-owned configuration, environment-variable references, secret stores,
or protected runtime channels.

Prefer proven building blocks for common concerns. If a maintained NuGet
package, standard API, protocol, or container image already solves a provider
or service implementation detail, use it before designing CloudShell-specific
machinery. Keep that reuse behind the owning provider, service, or abstraction
boundary so CloudShell still projects stable product concepts rather than
incidental library or container details.

## Implementation flow

Use this flow for every artifact change.

1. Update the public abstraction only when consumers need the artifact.
2. Implement or adapt the Control Plane behavior at the service boundary that
   owns validation, authorization, persistence, or provider coordination.
3. Update provider contracts only when provider implementations must produce,
   consume, or act on the artifact.
4. Project the artifact through API DTOs when split hosting or remote clients
   need it.
5. Map the API shape back into the public model in the remote client.
6. Render or invoke it through Resource Manager UI only after the domain and
   capability shape is available. Use generic shell UI extensions only for
   shell-level views, navigation, and workspaces.
7. Add targeted tests at the owning layer, plus API/client contract tests when
   transport shape changes.
8. Update docs, ADR, changelog, and roadmap when the artifact changes product
   concepts, API shape, hosting guidance, MVP priorities, or durable
   decisions.

## New resource type checklist

Adding a resource type is an end-to-end feature, not just a provider or UI
change. The default expectation is that the whole chain is implemented and
verified through the shell UI unless the change explicitly documents a smaller
scope.

Use this checklist when adding a resource type such as a new application,
configuration, network, infrastructure, service, host, or provider-specific
resource.

1. Define the domain concept.
   - Confirm whether the type is a new resource type, a capability on an
     existing resource, provider-owned runtime state, or a child resource.
   - Choose the stable `TypeId`, expected `ResourceClass`, provider ID, and
     user-facing name.
   - Document ownership boundaries, especially which configuration is
     platform-owned and which configuration remains provider-owned.
2. Add the Control Plane provider path.
   - Implement the provider projection through `IResourceProvider` and creation
     through `IResourceCreationProvider` when the type can be created by users
     or declarations.
   - Add procedure, image update, endpoint mapping, template, log, or
     orchestration providers only when the type owns that behavior.
   - Define focused runtime adapter interfaces for host/runtime actions the
     provider needs, and require the host/sample/default runtime package to
     register concrete implementations. Test providers with fake adapters so
     resource semantics stay separate from runtime plumbing.
3. Define the projected resource shape.
   - Project stable identity, class, type, state, parent, dependencies,
     endpoints, attributes, capabilities, actions, health checks,
     observability, endpoint mappings, routes, and detail route as applicable.
   - Use reserved `ResourceAttributeNames` only for CloudShell-defined facts.
   - Keep secrets, provider credentials, and runtime-only implementation detail
     out of `Resource`.
4. Implement Control Plane behavior.
   - Validate create/register/dependency/group commands before provider
     dispatch.
   - Enforce resource class consistency across type contribution, creation
     metadata, programmatic declaration metadata, and provider projection.
   - Implement action capability calculation, lifecycle policy, dependent
     warnings, dependency startup, delete behavior, and resource events when
     the type supports management operations.
   - Persist only platform-owned registration/group/dependency state in
     platform stores.
5. Implement authoring surfaces.
   - Add programmatic declaration builders or extension methods when the type
     should be declared in code.
   - For Resource Model provider ports, add a provider-owned
     `ResourceGraphBuilder` builder unless the provider README
     explicitly defers it. The builder should cover the resource's common
     attributes, relationships, configuration payloads, capability payloads,
     and operation declarations without leaking runtime implementation
     details.
   - Keep start-after-create UI behavior explicit and separate from
     programmatic startup autostart.
6. Project through the API and remote client.
   - Ensure `ResourceResponse` carries the full projected shape without
     type-specific omissions.
   - Add specialized API groups only for domain operations that need
     type-specific request/response contracts.
   - Map all API DTO additions back to the public abstraction in the remote
     client.
   - Update OpenAPI expectations and contract tests for changed transport
     shape, hypermedia actions, errors, or authorization behavior.
7. Implement Resource Manager UI integration.
   - Register a `ResourceTypeContribution` with the expected class,
     registration component, optional update component, tabs, and probe
     options when users should manage the resource through Resource Manager.
   - Verify the resource appears in Resource Manager lists, filters, grouped
     views, and generated details.
   - Render endpoints, attributes, related resources, health checks, logs,
     observability, actions, capability reasons, endpoint mappings, routes, and
     provider detail routes as applicable.
   - Prefer predefined view sections when provider-owned details extend a
     standard Resource Manager concern such as Overview, Endpoints, DNS,
     Identity, Access control, Activity, or Monitoring. Use a custom tab or
     predefined-tab replacement only when the provider owns the whole workflow.
   - Register custom Resource Manager UI actions only for presentation
     workflows. Use advertised resource actions and Control Plane capabilities
     for domain operations instead of local state guesses.
   - Use the base UI extension architecture only for generic shell views,
     navigation, and shell-hosted workspaces.
8. Add tests and samples.
   - Add abstraction tests for builders, helpers, and public contribution
     contracts.
   - Add Control Plane tests for projection, validation, state, capabilities,
     action execution, delete, dependency, group, events, and diagnostics.
   - Add provider tests for provider-owned configuration, projection, and
     runtime behavior.
   - Add API/client tests for DTO shape, errors, hypermedia, and remote mapping.
   - Add sample smoke tests when the resource type is part of supported
     product guidance.
9. Update documentation.
   - Update the relevant resource docs, API docs, programmatic resource docs,
     ADR when a durable decision changed, changelog when implementation lands,
     and roadmap execution plan when scope or task order changes.
   - Document intentionally deferred chain links, such as "Control Plane
     provider only, no Resource Manager UI integration yet", in
     `ADR.md`, `CHANGELOG.md`, and `docs/roadmap.md`.

## Artifact guidelines

### Resource

`Resource` is the central projected artifact. It describes what exists now:
identity, type, class, provider, region, state, endpoints, parentage,
dependencies, actions, health, observability, capabilities, endpoint mappings,
load-balancer routes, attributes, and detail routing.

Implementation:

- Keep `Resource` passive. It may expose lookup helpers, but operations go
  through `IResourceManager` or `IControlPlane`.
- Use `EffectiveTypeId` when projecting or comparing resource type identity.
- Normalize null collections to empty collections in domain helpers, API DTO
  mapping, client mapping, and UI code.
- Keep `ResourceClass` consistent with known resource type contributions,
  creation metadata, programmatic declarations, and provider projections.
- Expose provider-owned detail routes or tabs only for specialized operational
  experiences; generated details should work from the base resource shape.

Verification:

- Add abstraction tests for helper semantics or public contract behavior.
- Add Control Plane tests for projection, registration overlays, validation,
  state transitions, and provider override normalization.
- Add API/client tests when `ResourceResponse` changes.
- Add UI tests or focused component checks when generated details, overview
  rows, or detail routes change.

### Resource type contribution

`ResourceTypeContribution` describes a stable user-facing type and optional
registration/update UI. It is separate from provider discovery.

Implementation:

- Use stable `Id` values such as `docker.host` or `configuration.store`.
- Declare the expected `ResourceClass`; treat it as a model constraint, not a
  visual hint.
- Keep registration and update component types in the shell contribution
  layer. Provider runtime configuration remains provider-owned.
- Use `ResourceTypeProbeOptions` for health checks that apply to the resource
  type by default.
- Use `ResourceEndpointDescriptor` for resource endpoints that apply to the
  resource type by default. The descriptor announces endpoint name, protocol,
  target port, and whether the provider supports remapping that endpoint to a
  different concrete port; providers and network resources still create
  concrete endpoint assignments, endpoint network mappings, and configured
  endpoint mappings for individual resource instances.

Verification:

- Add extension/abstraction tests for contribution registration and metadata.
- Add Resource Manager validation tests proving creation and projection reject
  or normalize class mismatches.
- Add UI tests when registration or update component selection changes.

### Resource class

`ResourceClass` is a broad classification of the projected domain shape. It is
not a provider runtime type.

Implementation:

- Use it for broad filtering, generated details, and class-level validation.
- Do not use it to select provider-specific orchestration behavior when
  `TypeId`, provider contracts, or capabilities are more precise.
- Keep class values durable. Adding a new class affects public abstractions,
  API serialization, client mapping, UI filters, tests, and docs.

Verification:

- Add tests for known type consistency across resource type contributions,
  creation requests, declarations, and provider projections.
- Add API/client tests for serialization and mapping when enum values change.

### Resource attributes

Attributes are stable, non-secret string facts useful for generated details,
filtering, diagnostics, and simple orchestration hints.

Implementation:

- Use lower-camel names. Prefer flat names for resource-type-local attributes
  such as `executablePath`; use dotted names only when the attribute belongs to
  an intentional shared namespace or should render as a nested
  ResourceDefinition document group.
- Reserve `ResourceAttributeNames` for CloudShell-defined meanings.
- Use invariant formatting for numbers, lower-case strings for booleans, and
  stable non-localized tokens for enum-like values.
- Put structured, lifecycle-sensitive, validated, or secret data in
  provider-owned configuration instead of attributes.
- Do not add a provider or resource-type prefix solely for uniqueness; the
  resource type already scopes local attributes. Prefixes change the exported
  template shape and should be reserved for deliberate cross-provider or
  capability-owned namespaces.

Verification:

- Add abstraction tests for new reserved names when useful.
- Add provider or Control Plane tests proving attributes are projected,
  normalized, and non-secret.
- Add API/client tests when attributes are added to a transport payload.

### Resource registration

`ResourceRegistration` is platform-owned state saying that a resource should be
visible and managed through CloudShell.

Implementation:

- Keep registrations in registration stores and Control Plane services, not in
  provider configuration.
- Track provider ID, optional group ID, registration time, and
  platform-declared dependencies.
- Register visible root resources explicitly. Dynamic children can appear under
  registered parents.
- Publish `ResourcesChanged` after registration mutations.

Verification:

- Add Control Plane tests for registration, duplicate handling, group
  assignment, dependency updates, delete behavior, and notifications.
- Add persistence tests when registration storage changes.
- Add API/client tests when registration endpoints or DTOs change.

### Resource group

`ResourceGroup` is a platform-owned project boundary and authorization scope.
Providers do not own group semantics.

Implementation:

- Keep group creation, assignment, filtering, and authorization in the Control
  Plane.
- Let sub-resources inherit grouping through parent or registration paths.
- Do not encode provider topology or runtime ownership as group membership.

Verification:

- Add Control Plane tests for group creation, assignment, inherited grouping,
  filtering, and authorization.
- Add API/client tests for group DTOs and assignment commands.
- Add UI tests when group selectors, filters, or grouped resource views change.

### Parent and dependency relationships

Parents express containment or ownership. Dependencies express that one
resource relies on another for topology, ordering, or startup behavior.

Implementation:

- Keep `ParentResourceId` separate from `DependsOn`.
- Normalize dependency lists and avoid duplicate or unstable ordering.
- Validate cycles and missing resources at the Control Plane boundary.
- Keep startup autostart separate from dependency autostart.
- Surface dependent warnings and dependency startup failures as domain errors
  or diagnostics with concrete reasons.

Verification:

- Add Control Plane tests for parent projection, group inheritance, dependency
  normalization, cycle detection, missing dependencies, dependent warnings, and
  dependency startup failures.
- Add client/API contract tests when command or error shapes change.

### Endpoint

`ResourceEndpoint` is the current projected resource endpoint shape. It
preserves the stable endpoint name, protocol, exposure, and target port. The
resource-owned endpoint contract starts with `ResourceEndpointDescriptor`;
topology-specific reachable addresses must be represented as
`ResourceEndpointNetworkMapping` when the provider can project them.

Implementation:

- Prefer endpoint-contract factories such as `Contract`, `Http`, `Https`,
  `Tcp`, or `Udp`. Treat address-bearing compatibility factories as migration
  helpers only.
- Always set an explicit `ResourceExposureScope`.
- Keep names stable within a resource. Consumers use names for references and
  mappings.
- Do not use endpoints to carry desired networking intent or provider
  configuration. Use `ResourceEndpointRequest` for assignment intent and
  provider-owned configuration for runtime-specific settings.

Verification:

- Add provider tests for endpoint projection and exposure.
- Add API/client tests for endpoint mapping, `IsExternal`, and exposure.
- Add UI tests when endpoint copy/open behavior or detail display changes.

### Endpoint request

`ResourceEndpointRequest` is networking intent. It describes an endpoint that
should be assigned, reserved, or resolved by a network provider.

Implementation:

- Use `Manual` when the caller provides concrete host/IP and port details.
- Use `ProviderDefault` when the provider should use the descriptor's
  conventional port and fail if that port is unavailable.
- Use `Auto` when the provider may allocate a different address or port for
  resources that support endpoint remapping.
- Preserve target port, requested exposure, network resource ID, and provider
  endpoint ID through declarations and provider requests.
- Keep assigned endpoint facts projected as `ResourceEndpoint` after resolution.

Verification:

- Add Control Plane or provider tests for manual validation, auto assignment,
  provider-default behavior, and stable assigned ports.
- Add API/client tests if endpoint requests cross the transport boundary.
- Add UI tests for registration controls that collect endpoint intent.

### Endpoint network mapping

`ResourceEndpointNetworkMapping` connects a resource endpoint to a topology and
provides the currently resolved address for that topology. It is projected on
the target resource through `Resource.ResourceEndpointNetworkMappings`.

Implementation:

- Store the target as a `ResourceEndpointReference`.
- Use the mapping address for copy/open UI and runtime startup values when the
  provider needs the concrete reachable address.
- Keep endpoint network mappings distinct from configured source-to-target
  mappings owned by network resources.
- Preserve source endpoint, network resource, and provider resource metadata
  when the mapping was produced by a network or exposure provider.

Verification:

- Add provider tests for endpoint network mapping projection and fallback
  behavior.
- Add API/client tests for `EndpointNetworkMappings`.
- Add UI tests when mapped-address display or copy/open behavior changes.

### Configured endpoint mapping

`ResourceEndpointMappingDefinition` connects a source endpoint to a target
endpoint. It is a configured source-to-target resource relationship owned by a
network resource, not a provider-specific attribute and not the same thing as a
topology-resolved endpoint network mapping.

Implementation:

- Store source and target as `ResourceEndpointReference` values.
- Record the logical network boundary separately from the provider resource
  that materializes the mapping.
- Require provider resources that materialize mappings to advertise
  `networking.endpointMapper`.
- Validate source endpoint existence, target endpoint existence, provider
  capability, and selected network before dispatching provisioning.
- Project configured mappings through resource responses so in-process and
  remote clients see the same shape.

Verification:

- Add Control Plane tests for mapping normalization and validation failures.
- Add provider tests for provisioning behavior and unsupported mapping cases.
- Add API/client tests for `EndpointMappings`.
- Add UI tests for network mapping display and target resource exposure.

### Resource capability

`ResourceCapability` describes the role a resource can play, such as endpoint
source, endpoint provider, endpoint mapper, gateway, load balancer, service
discovery, host network, or environment-variable configuration. Common
capabilities are documented in [Resource capabilities](capabilities.md).

Implementation:

- Use canonical IDs from `ResourceCapabilityIds` for CloudShell-defined
  capabilities.
- Treat capabilities as projected facts and orchestration inputs. They do not
  execute operations.
- Keep capability metadata string-keyed, stable, non-secret, and small.
- Prefer capabilities over hard-coded provider or type checks when selecting
  resource roles.

Verification:

- Add provider tests proving expected capabilities are projected.
- Add Control Plane tests for capability-based validation.
- Add API/client tests for capability projection and metadata mapping.

### Resource action

`ResourceAction` is a domain operation on a resource. It is not a UI action.
UI actions are Resource Manager presentation artifacts that may invoke resource
actions but are registered by Resource Manager UI extensions, not projected by
Control Plane providers as resource-model operations.

Implementation:

- Use canonical IDs from `ResourceActionIds` for lifecycle actions.
- Use `ResourceActionKind` for standard lifecycle actions and stable custom
  IDs for provider actions.
- Keep action presentation as metadata only. Execution still goes through
  `IResourceManager.ExecuteResourceActionAsync`.
- Project actions through API responses as a dictionary keyed by action ID with
  `method` and `href`.
- Validate authorization, state, dependency policy, and provider support in the
  Control Plane before provider dispatch.

Verification:

- Add Control Plane tests for valid execution, unsupported actions,
  state-specific rejection, authorization denial, dependent warnings, and
  event emission.
- Add API/client tests for hypermedia affordances and remote action execution.
- Add UI tests that buttons invoke advertised resource actions and honor
  confirmation metadata.

### Resource action capability

`ResourceActionCapability` describes whether an action can currently execute
and why. `ResourceOperationCapabilities` groups action capabilities with
management and delete capability.

Implementation:

- Keep capability calculation in the Control Plane, where authorization, state,
  dependencies, and provider support are available.
- Return unavailable reasons suitable for display and diagnostics.
- Do not force consumers to infer disabled states from missing actions alone.
- Keep `ExecutableActionIds` as a compact projection, but prefer
  `ResourceActionCapabilities` for rich UI and diagnostics.

Verification:

- Add Control Plane tests for each lifecycle state and important provider or
  permission constraint.
- Add API/client tests for capability response mapping.
- Add UI tests for disabled action reasons when action controls change.

### Resource health check

`ResourceHealthCheck` describes a provider or type-contributed probe.

Implementation:

- Use probe type, source, name, and source-specific settings explicitly.
- Use `ResourceProbeSource` to describe where the signal comes from. HTTP is
  the built-in source; provider-native sources should use stable source kinds
  and non-secret metadata.
- Use `IResourceProbeEvaluator` when a provider can evaluate a non-HTTP probe
  source for Resource Manager health polling.
- Keep health check declarations separate from observed health state.
- Type-level probe options can provide defaults, but provider projection should
  still reflect what the resource supports.

Verification:

- Add abstraction tests for contribution defaults when needed.
- Add provider or Control Plane tests for projected probe metadata.
- Add UI tests when health display, filtering, or generated details change.

### Observability

`ResourceObservability` describes whether a resource exposes logs, traces,
metrics, OTLP configuration, telemetry sources, and telemetry scopes.

Implementation:

- Treat observability as projected capability or startup configuration, not as
  embedded telemetry data.
- Use telemetry sources for stable producer or collection metadata:
  provider-owned streams, OpenTelemetry exporters, or
  Prometheus/OpenMetrics-style endpoints.
- Use telemetry scopes for provider-defined selectable units under a stable
  resource, such as replicas, partitions, workers, shards, or runtime
  containers.
- Providers may inject environment variables or side effects when starting a
  resource, but the projected resource should only expose stable observability
  metadata.
- Keep explicit resource environment variables able to override provider
  defaults where the provider supports it.

Verification:

- Add provider tests for projected observability and startup environment
  generation.
- Add UI tests when observability links or generated details change.

### Log descriptor and log entry

Logs are first-class operational streams exposed by log providers. They are not
embedded fields on `Resource`.

Implementation:

- Use `ILogManager` for consumers, `ILogProvider` for provider sources, and
  `ILogStore` for internal storage.
- Use `ResourceLogSource` for resource-owned log declarations and `LogSource`
  for the Control Plane projection of readable/queryable/streamable sources.
- Use `ResourceId`, `ArtifactId`, and `SourceKind` to scope descriptors.
- Keep provider console output separate from platform resource events.
- Do not assume every operational signal is a text log. Structured log fields,
  metrics, traces, diagnostics, audit records, and non-text payload references
  are tracked separately in the logging infrastructure proposal.
- Avoid assuming one log per resource.

Verification:

- Add provider tests for descriptor discovery and log reads.
- Add API/client tests for log descriptor and entry projection.
- Add UI tests for log routing and resource-scoped log shortcuts.

### Resource event

`ResourceEvent` is the platform-owned resource history stream for operations
performed on a resource. Consumers query it through `IResourceEventManager`;
the generated Activity log is a compatibility view adapter, not the owning
model.

Implementation:

- Record actor or trigger information when available.
- Use stable event types and `ResourceSignalSeverity`. Standard lifecycle
  action event types use the `action.lifecycle.*` namespace, such as
  `action.lifecycle.start` and `action.lifecycle.stop`. Custom action event
  types are derived from the requested action ID under `action.*`; authors may
  namespace their own action IDs, such as `database.backup`.
  Standard lifecycle event types describe lifecycle phases and outcomes, such
  as `event.lifecycle.starting`, `event.lifecycle.started`,
  `event.lifecycle.stopping`, and `event.lifecycle.stopped`. Event types are
  namespaced too; authors may define custom event namespaces under `event.*`,
  such as `event.database.backup.completed`. Custom actions and custom event
  types are allowed, but only standard lifecycle action kinds should be treated
  as lifecycle events by Resource Manager.
- Emit resource events for operations such as actions, image updates,
  configuration changes, and important Control Plane decisions.
- Provider procedures may emit provider-scoped resource events under
  `event.provider.<provider-id>.*` for concise milestones or observations that
  explain what the provider did while fulfilling the current resource
  procedure. These events remain resource-scoped and should carry actor/cause
  information from the procedure context when available.
- Keep provider-scoped resource events concise and non-sensitive. Do not write
  secrets, raw credentials, secret values, or raw configuration values into
  resource events. Provider-specific logs can add operational detail when that
  detail belongs in a log stream rather than Activity.
- Keep structured event properties additive until event schemas are defined.

Verification:

- Add Control Plane tests proving events are emitted for mutating operations.
- Add storage/API/client tests when events become persisted or queryable.
- Add UI tests when event history display or filtering changes.

### Template

`ResourceTemplate` is the portable desired-state envelope owned by
CloudShell. It contains one or more `ResourceDefinition` entries. The
resource definition is the serialization boundary for user-authored resource
intent; runtime deployment artifacts are produced later by Resource Manager
and orchestrator planning.

Implementation:

- Keep the template envelope focused on apply metadata and resource
  definitions. Do not wrap normal authoring in deployment-shaped DTOs such as
  `ResourceDeploymentDefinition`.
- Validate the envelope in Resource Manager and validate each resource
  definition through the owning resource type provider or graph apply
  contract.
- Commit accepted graph state before runtime materialization. When accepted
  state affects runtime, provider-owned planners should describe internal
  deployment work for Resource Manager to coordinate.
- Export accepted graph state back to resource definitions without dumping
  provider runtime caches, logs, live container IDs, secret values, or
  internal deployment records.
- Retire provider-specific template serializers for graph-backed resource
  types once those resource types can round-trip through definitions.

Verification:

- Add template service tests for export/apply diagnostics, invalid envelopes,
  dependency/reference handling, graph-state commit behavior, and
  no-partial-state guarantees for envelope-level failures.
- Add provider or graph-apply tests for resource-definition validation,
  normalization, incremental updates, and export round trips.
- Add API/client tests if template endpoints or DTOs change.

### Programmatic declaration

`ResourceDeclaration` and resource builders are declaration-time artifacts.
They create uniform resources plus provider-owned configuration.

Implementation:

- Keep builder names resource-oriented and provider extension methods
  provider-owned.
- Use typed builders for resource relationships when both resources are
  declared in the same callback.
- Keep `DependsOn`, `WithParent`, `WithReference`, `WithAutoStart`, and
  `WithDependencyAutoStart` semantically distinct.
- Carry `ResourceClass` and non-secret attributes through declaration metadata
  into the same validation path as creation and provider projection.

Verification:

- Add abstraction tests for builder DSL behavior.
- Add Control Plane tests for declaration application, persistence, overwrite,
  startup policy, dependency startup policy, and class validation.
- Add sample smoke tests when declarations are intended as user guidance.

### Create and management commands

Commands such as `CreateResourceCommand`, `RegisterResourceCommand`,
`AssignResourceGroupCommand`, `SetResourceDependenciesCommand`,
`ExecuteResourceActionCommand`, `UpdateResourceImageCommand`, and
`UpdateResourceReplicasCommand` are intent-shaped public artifacts.

Implementation:

- Keep commands domain-oriented. Do not include route templates, UI labels, or
  generated-client types.
- Include explicit options for behavior that has side effects, such as
  `StartAfterCreate`, `StartDependencies`, `IgnoreDependentWarning`, or
  `RestartIfRunning`.
- Validate commands before provider dispatch and translate invalid API payloads
  into stable ProblemDetails responses.
- Keep resource-type-specific operations on specialized API groups only when
  the domain operation needs a specialized contract surface.

Verification:

- Add Control Plane tests for command validation and behavior.
- Add API contract tests for request DTOs, ProblemDetails codes, and remote
  client mapping.
- Add event tests for mutating commands.

### Provider-owned configuration and runtime state

Provider-owned configuration is not a CloudShell resource artifact until the
provider projects stable facts from it.

Implementation:

- Store provider configuration behind provider contracts and stores.
- Project only stable, non-secret facts into `Resource`, logs, capabilities,
  endpoints, templates, and events.
- Use environment-variable references or protected stores for credentials.
- Treat implementation containers, processes, replicas, and external runtime
  objects as provider state or child resources only when the user needs to
  inspect or operate them directly.

Verification:

- Add provider tests for configuration validation, secret handling, runtime
  projection, and provider-owned lifecycle behavior.
- Add Control Plane tests when provider state affects platform validation or
  resource-manager decisions.

### Load-balancer route

`LoadBalancerRoute` is a projected resource detail for load-balancer
resources. It is provider-neutral routing configuration over resource
endpoints.

Implementation:

- Keep the stable resource as the load balancer. Provider implementation
  containers are runtime state or children, not authored container apps.
- Use stable entrypoint and route IDs.
- Resolve route targets through resource IDs and endpoint names before provider
  application.
- Apply provider configuration through resource actions and provider
  contracts.

Verification:

- Add Control Plane tests for route projection and target resolution.
- Add provider tests for generated provider configuration and validation
  diagnostics.
- Add sample smoke tests for apply actions and generated configuration.
- Add API/client tests for route projection.

### Shell contribution artifacts

Shell views, navigation items, resource tabs, detail routes, registration
components, and update components are presentation artifacts.

Implementation:

- Keep shell contribution contracts in the extension/UI layer.
- Use base UI extension artifacts for shell-level views, navigation,
  shell-hosted workspaces, and start page behavior.
- Use Resource Manager UI extension artifacts for resource type registration
  UI, update components, resource tabs, detail routes, generated detail
  overrides, and resource UI actions.
- Keep non-UI resource projection and operation behavior in Control Plane
  resource-provider extensions.
- Do not encode domain validation or lifecycle policy in UI contributions.
- Use domain managers and capabilities to read, render, and invoke resource
  operations.
- Provider-owned detail routes can replace generated details when specialized
  operations need a richer UI.

Verification:

- Add UI/component tests for routing, navigation, tab rendering, registration,
  update flows, and capability-driven disabled states.
- Add Control Plane tests for behavior that the UI invokes but does not own.

## Verification matrix

Use this matrix to choose the minimum useful test coverage.

| Change | Required verification |
| --- | --- |
| Public abstraction helper, record, enum, or DSL | Abstraction tests and docs |
| Resource Manager validation or state behavior | Control Plane service tests |
| Provider projection or provider operation | Provider tests and relevant Control Plane tests |
| API DTO, route, hypermedia, error, or OpenAPI shape | Control Plane API/client contract tests |
| Remote adapter mapping | `CloudShell.ControlPlane.Client.Tests` |
| Generated Resource Manager UI or extension UI | Focused UI/component test or sample smoke test |
| Programmatic resources or hosting guidance | Abstraction tests plus sample smoke tests |
| Template import/export | Template service tests plus provider template tests |
| Sample behavior | `CloudShell.Sample.Tests` smoke coverage |

For resource model, Control Plane, API, remote client, or sample changes, run
the verification baseline from [AGENTS.md](../AGENTS.md) after targeted tests
when practical:

```bash
dotnet build CloudShell.slnx --no-restore
dotnet test CloudShell.ControlPlane.Tests/CloudShell.ControlPlane.Tests.csproj --no-restore
dotnet test CloudShell.ControlPlane.Client.Tests/CloudShell.ControlPlane.Client.Tests.csproj --no-restore
dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore
dotnet test CloudShell.Sample.Tests/CloudShell.Sample.Tests.csproj --no-restore
```

Docs-only changes do not require the test baseline, but should pass:

```bash
git diff --check
```
