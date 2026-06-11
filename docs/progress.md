# Progress

This is the living CloudShell progress tracker. Update it when a feature,
stabilization pass, or design decision changes the current direction.

See also: [Roadmap](roadmap.md) for product direction and [TODO](../TODO.md)
for the current task queue that turns those priorities into concrete next
tasks.

## Current MVP focus

Make CloudShell functional and stable for the common-hosted scenario while
preserving the path to split hosting.

The MVP should prove:

- Combined UI and Control Plane hosting works reliably.
- Split UI and Control Plane samples build and smoke-test.
- The Control Plane exposes a stable domain-shaped client abstraction.
- The Control Plane API has a clear OpenAPI contract.
- Resource Manager behavior is predictable across states, validation failures,
  permissions, and provider capability differences.
- Samples demonstrate the intended hosting and resource declaration patterns.

## Proposal status snapshot

- Platform foundations proposal: In progress
- Resource identity and permissions proposal: Proposed; current implementation
  focus
- Identity and permissions proposal: In progress platform foundation
- Secrets management proposal: In progress
- Container host abstraction proposal: Proposed
- Remote Docker hosts proposal: Partially implemented
- Load balancer resource proposal: In progress
- Virtual network resource proposal: In progress
- Runtime-managed resource proposal: In progress design
- Deployments and revisions proposal: In progress design

## Current proposal order

1. Define resource identity and permissions first: resource identity-provider
   contracts, default provider selection, identity bindings, resource-scoped
   permissions, workload identity lifecycle, token claim mapping, action
   authorization, authorization diagnostics, and a separate replaceable
   development identity server using standard OIDC/OAuth. The same provider
   contract must work with Microsoft Entra ID (Azure AD), including
   issuer/audience validation, claim mapping, groups or app roles, and
   service-principal automation flows.
2. Jump next to host abstractions: host descriptors, compatibility adapters,
   default/explicit host resolver, and host-resolution diagnostics.
3. Align configuration and secrets access with identity: Resource Manager
   assignment UI, in-process secrets client, and secret-read authorization.
4. Persist and filter resource events, then define audit event schemas for
   actions, host/runtime operations, deployments, authorization, and secret
   access.
5. Complete remote Docker hosts as the first concrete user-managed container
   host: UI registration, provider-owned persistence, credentials,
   duplicate-host validation, and remote action coverage.
6. Add provider-owned runtime lifecycle support for implementation containers
   and helper services, starting with Traefik container mode and app-owned
   ingress cleanup.
7. Harden virtual networking, load balancing, and replicated app ingress with
   provider selection, host-readiness warnings, route conflicts, endpoint
   conflict diagnostics, configuration preview, and backend resolution.
8. Decide runtime-managed resource ownership, visibility, cleanup, diagnostics,
   and authorization before broadening replica and implementation-resource
   projection.
9. Introduce richer deployment and revision concepts only after runtime
   ownership and traceability boundaries are clear.
10. Revisit advanced app and environment concepts such as autoscaling, backend
   pools, traffic splitting, `cloudshell.service`, and container application
   environments after the lower-level foundations stabilize.

## Recent decisions

- The WebUI is the shell surface; the Control Plane is a separately deployable
  service boundary.
- Consumers should use domain managers, not generated HTTP clients directly.
- Internal Control Plane stores/providers remain internal implementation
  contracts.
- Resource actions are domain operations on resources, not UI actions.
- Resource API responses expose resource actions as keyed hypermedia
  affordances.
- Resource action capabilities are separate signals that describe current
  executability and reasons.
- Standard lifecycle resource actions map to the Azure RBAC-style
  `CloudShell.Resources/resources/lifecycle/action` operation permission.
  Custom actions can declare narrower Azure-style operation permissions and
  otherwise use `CloudShell.Resources/resources/actions/execute/action`.
  `resources.manage` remains a compatibility superset for resource actions.
- Resource operation permissions must be documented per resource type or class
  as they are added. Network endpoint reconciliation now uses
  `CloudShell.Network/networks/reconcileEndpointMappings/action`, and
  load-balancer configuration apply now uses
  `CloudShell.Network/loadBalancers/applyConfiguration/action`.
  Common operation constants live in `CommonResourceOperationPermissions`;
  resource-type-specific operation constants live in dedicated classes such as
  `NetworkResourceOperationPermissions` and
  `LoadBalancerResourceOperationPermissions`.
- Resources can project an optional resource identity binding with kind, stable
  name, provider ID when resolved, subject, scopes, and non-secret claim
  metadata. The Control Plane API and remote client expose this as
  `ResourceResponse.identity`.
- Resource identity provider selection now has a catalog abstraction. Concrete
  provider bindings resolve by provider ID; required-but-unresolved bindings
  resolve to the configured default provider, with a single registered provider
  used as the implicit default. Control Plane hosts can register providers and
  the default through `ResourceIdentity` configuration. Unresolved identity
  providers are reported through resource model diagnostics. Resource-group or
  parent-resource inheritance, token issuance, and provider-backed workload
  behavior remain future resource identity work.
- `docs/resource-identity-and-permissions.md` is the current-state feature
  documentation for resource identity and permissions. The matching proposal
  remains the tracker for open design, decisions, and remaining implementation
  work.
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
  overview displays resource identity bindings when present. A provider-neutral
  `IResourceIdentityProvisioner` contract and Control Plane provisioning
  planner can group declared identities and matching grants by resolved
  identity provider. Mock-principal tests, token-claim projection,
  provider-backed identity proof, concrete authority registration, identity
  management UI, multiple identities, and provider-backed managed identity
  lifecycle remain future resource identity work.
- The domain model should be documented across product concepts, public
  abstractions, internal Control Plane services, provider contracts, API
  projection, and UI projection.
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
  Resource-type logs remain available for operational detail such as container
  console output.
- Container app image deployments create and project a new app-owned revision;
  runtime container instances/replicas implement a revision but do not define
  the stable revision identity.
- Build-server container app deployments should push an immutable image tag to
  a registry, then call the authenticated Container Apps revision API with that
  tag. The Control Plane authorizes the caller, updates the image, creates the
  revision, and records resource events for traceability.
- Container app resources and Docker resources can specify a non-secret
  container registry value, projected as `container.registry`; both default to
  Docker Hub (`docker.io`). Registry credentials are provider-owned
  configuration and use a username plus password environment variable
  reference instead of projecting secrets through resource attributes.
- Container app and Docker host configuration UI exposes registry settings,
  and container app details show the latest projected revision.
- Networking is modeled through resources and capabilities. Resources can
  advertise endpoint-source and networking-provider capabilities; network
  resources can reserve or auto-assign endpoint requests and record endpoint
  mappings while richer networking behavior remains provider-owned.
- Network resources now distinguish host, logical, and virtual network kinds.
  When no network is created, the platform projects a default host network.
  Virtual networks reuse endpoint requests and mappings while advertising
  virtual-network and ingress capabilities.
- Host-provided virtual networking starts with macOS. The built-in macOS host
  networking provider is an activated resource that can materialize virtual
  endpoint mappings as local TCP proxies for HTTP, HTTPS, and TCP endpoints.
- Network resources project endpoint mappings as first-class resource data.
  Resource Manager shows mappings on the network resource and read-only network
  exposure on mapped target resources, instead of treating exposure as a
  dependency or encoded attribute.
- Platform-owned network, service, and load-balancer endpoint assignments are
  validated for concrete protocol/host/port conflicts before registration.
  Endpoint mapping reconciliation also validates that mapping sources belong to
  the reconciled network and are not reused across multiple mappings.
- Load balancing should be modeled as a resource abstraction over providers.
  Traefik is the proposed first provider target, with routes mapped to stable
  resource endpoints and raw ports treated as authoring convenience.
- Provider-owned resources can create and manage implementation containers as
  runtime state or child resources without becoming container app resources.
  The stable resource, such as a load balancer, owns the user-facing lifecycle.
- Provider-owned runtime infrastructure should select a host resource, where
  host means an instance of a runtime or control boundary CloudShell can
  target. Docker, Podman, containerd, schedulers, process managers, and
  appliance APIs are host runtime capabilities or provider-owned facts, not
  separate placement primitives.
- Docker now projects configured local and remote Docker runtime connections as
  `docker.host` container host resources. UI language uses container host,
  while `container.host` remains the future generic resource-type direction for
  non-Docker providers.
- The first load-balancer implementation slice adds a platform load-balancer
  resource model, fluent route declarations, API/client projection, generated
  Resource Manager route display, an apply-configuration resource action, and a
  Traefik file-provider implementation that writes dynamic HTTP/TCP
  configuration from stable resource routes.
- The load-balancer sample declares a selected container host, mock web/API/TCP
  container-app targets, and a Traefik-backed public load balancer. Its smoke
  test invokes the advertised apply action and verifies the generated dynamic
  configuration file.
- `IResourceManager` publishes coarse `ResourcesChanged` notifications after
  resource-manager mutations. Resource Manager listens for those notifications
  and also polls the inventory so provider-discovered changes, such as runtime
  containers appearing or status changing outside CloudShell, update visible
  resource rows without manual refresh.
- Added Docker host definitions for local and remote endpoints, safe host
  endpoint projection, per-host Docker clients, remote host builder APIs, and
  group-scoped duplicate Docker host validation.
- Defined artifact implementation guidelines for resource-model artifacts,
  including ownership, projection, API/client mapping, provider boundaries, UI
  responsibilities, end-to-end resource type implementation, and verification
  expectations.
- Settings and secrets are being split into explicit reference-backed resource
  configuration. Application resources now have app-setting metadata,
  configuration-entry references for non-secret settings, and secret-reference
  placeholders for vault-backed values while secret storage remains provider
  owned.
- Host applications can explicitly expose selected `IConfiguration` entries
  through host-configuration source resources for development scenarios. These
  sources resolve through the same configuration-entry reference path as
  configuration stores and do not expose the entire host configuration surface.
- Added the first built-in Secrets Vault slice: `AddSecretsVault(...)`
  programmatic resources, `vault.Secret(...)` reference helpers, a
  secrets-provider resolver implementation, multiple vault support, and
  template export that preserves secret names without exporting secret values.
- Added Resource Manager UI for creating, inspecting, updating, and deleting
  built-in Secrets Vault resources. Existing secret values are masked in the
  UI and preserved unless replaced.
- Added a Settings and Secrets sample for the resource-assignment path: a
  programmatically declared Web API resource receives environment variables
  from configuration-entry and Secrets Vault references.
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
- The container host abstraction should be host-first and descriptor-driven:
  providers resolve explicit or default container hosts through a shared
  resolver, keep provider-owned runtime state behind provider contracts, and
  migrate existing container-engine APIs through compatibility adapters.
- Container app replicas can now be updated as an explicit desired count
  through the domain manager and `PUT /api/container-apps/v1/{containerAppId}/replicas`.
  This is not autoscaling: richer replica health, placement, traffic splitting,
  and backend-pool behavior remain future design work. Provider-owned runtime
  containers should be named by convention from the parent container app when
  replicas are materialized.
- Orchestrator-specific services, backends, deployments, and runtime
  containers are implementation details below the stable container app
  resource. The app exposes image/revision and replica desired state; providers
  map that state to Docker Compose, Kubernetes, the default local runner, or
  another runtime without exposing those implementation objects as Resource
  Manager targets.
- Added `ResourceOrchestratorService` as the orchestration-layer service
  descriptor for a stable workload. Docker Compose now renders Compose services
  from that descriptor, including replica count, ports, dependencies, and
  networks, instead of treating workload configuration as the service directly.
  The existing `cloudshell.service` resource remains a separate platform
  exposure resource for stable endpoints over one or more targets.
- The default orchestrator now owns replica instance fan-out for container app
  services, and load-balancer route resolution can expand a port-based route to
  a replicated container app into convention-named backend targets for Traefik
  file-provider output.
- The default Docker-backed container app runner now places app instances on a
  shared user-defined Docker network so convention-named replica containers can
  be resolved by provider-owned runtime infrastructure such as Traefik. The
  Traefik provider can optionally start a provider-owned runtime container on
  the selected Docker host when applying load-balancer configuration.
- Replicated container apps now own app-specific ingress for the default path.
  The default Docker runner starts a provider-owned Traefik ingress container
  automatically during app run/restart for replicated HTTP/TCP endpoints, and
  the Docker Compose generator renders a Traefik sidecar plus labels for
  replicated services with published HTTP/TCP ports. Explicit
  `cloudshell.loadBalancer` resources remain the higher-control gateway
  scenario rather than the normal app endpoint path.

## Completed recently

- Added a remote `IControlPlane` implementation for split hosting.
- Added split-hosting and sample smoke tests.
- Added remote Control Plane authentication coverage.
- Added API boundary validation and invalid-payload contract tests.
- Added internal Control Plane resource-state tests.
- Added resource action capability modeling.
- Added hypermedia resource actions to API resource responses.
- Removed legacy `actions` API compatibility from resource responses.
- Added direct `IResourceManager` validation for resource creation,
  registration, group assignment, and dependency updates.
- Added Resource Manager projection coverage for registered roots, dynamic
  children, declaration-assigned parents, group inheritance, and parent graph
  cycle safety.
- Added contract-level Control Plane errors with API `ProblemDetails` code
  projection and remote client mapping.
- Added delete/action contract-error coverage for missing resources, missing
  actions, unsupported providers, permission denial, dependent warnings, and
  delete capability alignment.
- Clarified that `CloudShell.Abstractions` is the cloud-plane client API and
  that projected resources expose action discovery while managers execute
  commands.
- Added client API helpers for canonical resource action IDs, resource action
  lookup, capability lookup, and manager-driven lifecycle action execution.
- Added a user-scoped CloudShell environment settings provider with selectable
  local or Control Plane-backed storage and theme/navigation preference
  integration.
- Renamed the projected domain entity from `CloudResource` to `Resource` and
  added `ResourceClass` projection through in-process resources, the Control
  Plane API, and the remote client.
- Added uniform resource attributes for class-defining, non-secret provider
  details such as workload kind, image, endpoint count, service port count, and
  configuration entry count.
- Added `ResourceClass` filtering to resource queries, the Control Plane API,
  and the remote client.
- Moved executable and project workload builder contracts into
  `CloudShell.Abstractions` alongside the existing container builder contract.
- Added generic declaration metadata for `ResourceClass` and non-secret
  attributes, and projected that metadata through Resource Manager overlays.
- Renamed the common programmatic resource builder contracts to
  `IResourceBuilder` and `IResourceDeclarationBuilder`.
- Added `ResourceClass` and non-secret attribute metadata to resource creation
  commands, HTTP requests, the remote client, and provider creation requests.
- Added generated Resource Manager detail views for resources without
  provider-owned detail routes, tabs, or update components.
- Added resource model class consistency validation for creation requests,
  provider projections, and declaration metadata, with result/diagnostic-based
  model validation.
- Separated ASP.NET Core project declaration and projection from executable
  command details, while preserving project app arguments, environment
  variables, endpoints, service discovery, and process-backed runtime behavior.
- Improved generated Resource Manager detail views with related-resource links,
  endpoint copy/open affordances, health metadata, logs, observability links,
  and action capability reasons.
- Defined resource attribute conventions: dotted lower-camel names,
  string-only non-secret values for MVP, invariant formatting, generated
  display behavior, and provider-specific prefix guidance.
- Aligned resource template import with the uniform resource validation model:
  invalid template envelopes now return diagnostics without creating resource
  groups or throwing from the domain API.
- Added first-class dependency auto-start failure details with a stable
  `dependencyAutoStartFailed` Control Plane error code, dependency path, blocked
  dependency, and concrete failure reason.
- Split declaration startup autostart from dependency autostart:
  programmatic declarations now use startup autostart semantics with provider
  defaults, while dependency startup uses `WithDependencyAutoStart(...)` and the
  same provider/default precedence.
- Added explicit start-after-create support for resource creation commands and
  runnable application registration UI, with provider policy carrying the
  default checkbox intent.
- Aligned OpenAPI output with the domain-shaped resource projection for
  resources, action affordance dictionaries, attributes, and creation options.
- Expanded the ResourceHost sample to exercise provider-backed resource
  actions through advertised hypermedia hrefs.
- Grouped sample projects in the solution by sample scenario so logical
  solution folders match the physical `samples/` layout.
- Added a domain image update command for top-level container app resources,
  exposed through a Container Apps revision API rather than a resource-specific
  core Resource Manager route, with actor-attributed resource events for
  traceability, application-provider console logs for underlying container
  output, split-host client mapping, and documented registry-push deployment
  procedure.
- Split application resource documentation into a `docs/resources` area with
  separate pages for executable applications, ASP.NET Core project resources,
  and container apps.
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
- Reused the shared endpoint assignment UI for executable application
  registration so the built-in registration flows expose consistent endpoint
  assignment controls.
- Added endpoint mapping provider selection for network declarations, a
  platform reconcile action that validates source, target, and mapper
  capabilities, and remote Control Plane contract coverage for invoking it.
- Added platform endpoint assignment conflict validation for network, service,
  and load-balancer resources, plus endpoint mapping source ownership and
  duplicate-source validation during reconciliation.
- Added a Container App Deployment sample with a local registry resource,
  stopped mock container app, and `sh` deployment script that simulates a build
  by posting a new image tag to the Container Apps revision API.
- Added host/logical/virtual network primitives, an `AddVirtualNetwork(...)`
  declaration helper, and a replaceable host-local network environment for
  default endpoint assignment across Windows, macOS, and Linux.
- Added host-readiness projection for default virtual networks and Resource
  Manager settings warnings when a virtual network is running in logical-only
  host-local mode.
- Added a macOS host networking provider resource, endpoint-mapping
  provisioner contract, Resource Manager UI readiness/provider display, and a
  Host Virtual Network sample.
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
- Added a Settings and Secrets sample that demonstrates assigning
  configuration-entry and Secrets Vault references to a Web API resource's
  environment variables.

## Active stabilization areas

- Resource model consistency across provider overrides.
- Resource Manager state behavior and capability signaling.
- API contract stability for projected resources, provider-backed actions,
  OpenAPI output, and errors.
- Sample coverage for combined and split hosting.
- OpenAPI/client generation readiness.

## Next priorities

1. Continue tightening internal Resource Manager behavior as invalid-state gaps
   are found.
2. Document any remaining MVP gaps as concrete tests or issues.

## Verification baseline

For changes that touch the resource model, Control Plane, API, remote client, or
samples, run:

```bash
dotnet build CloudShell.sln --no-restore
dotnet test CloudShell.ControlPlane.Tests/CloudShell.ControlPlane.Tests.csproj --no-restore
dotnet test CloudShell.ControlPlane.Client.Tests/CloudShell.ControlPlane.Client.Tests.csproj --no-restore
dotnet test CloudShell.Abstractions.Tests/CloudShell.Abstractions.Tests.csproj --no-restore
dotnet test CloudShell.Sample.Tests/CloudShell.Sample.Tests.csproj --no-restore
```

Use narrower test runs first while developing, then run the baseline before
committing a cross-boundary change.
