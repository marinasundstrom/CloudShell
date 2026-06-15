# Container Host Abstraction Proposal

## Status

In progress.

This proposal defines how CloudShell should select and use container-capable
hosts for provider-owned runtime infrastructure. It is the bridge between the
user-visible host resource model, such as `docker.host`, and provider-owned
runtime work, such as starting a Traefik implementation container for a load
balancer resource.

## Problem

CloudShell now has three related concepts that must stay distinct:

- a stable user-facing resource, such as a container app or load balancer
- a host resource or configured host, such as a Docker host where runtime work
  can be materialized
- provider-owned runtime state, such as an implementation container, helper
  process, or scheduler deployment created on behalf of the stable resource

The current code still uses older "container host" names in several internal
contracts. That naming worked for local Docker-only orchestration, but it is too
narrow for the resource model CloudShell is moving toward:

- A host is the placement/control boundary CloudShell can target.
- A runtime is the implementation family available through that host, such as
  Docker, Podman, containerd, Kubernetes, systemd, or a vendor appliance API.
- An engine is product-specific wording and should only remain in compatibility
  APIs until they are migrated.

The missing design piece is a consistent host-resolution and provider-owned
runtime contract. Providers need to ask "where can I materialize this
implementation?" without hard-coding Docker, requiring a user-visible host in
every local-development scenario, or bypassing the resource model with ad hoc
local wiring.

## Relationship to Remote Docker Hosts

The [Remote Docker Hosts Proposal](remote-docker-hosts.md) owns the concrete
Docker host resource model:

- `docker.host` registration and projection
- provider-owned Docker connection configuration
- credentials and endpoint normalization
- Docker container discovery and Docker-specific actions
- group-scoped duplicate host validation

This proposal owns the generic placement and runtime integration model:

- how a resource selects a host
- how a provider resolves an implicit or explicit host into a usable descriptor
- how provider-owned runtime state is created, tracked, probed, logged, and
  removed without becoming the stable user resource
- how current `container host` APIs migrate to host-oriented names

The proposals should remain separate. Docker is the first concrete host
provider, not the platform abstraction.

## Goals

- Model runtime placement as host selection, not as direct engine selection.
- Keep user-managed Docker hosts and provider-owned runtime state separate.
- Let local development work with an implicit default host from `UseDocker()`
  or equivalent provider defaults.
- Let on-premise and multi-host environments select explicit host resources.
- Reuse the existing resource/orchestration descriptor path instead of
  inventing a parallel Web API concept.
- Provide a narrow provider-owned runtime contract for implementation
  containers or helper services.
- Move engine-oriented provider, descriptor, workload, settings, and builder
  names to host-oriented names before release, while keeping samples and
  built-in providers on the current API.

## Non-Goals

- Do not make every host provider implement Docker-compatible operations.
- Do not require every default host to appear as a normal resource in Resource
  Manager.
- Do not make runtime containers the stable deployment target for user-facing
  resources.
- Do not store provider-owned host credentials or runtime state in platform
  registration state.
- Do not standardize Kubernetes, systemd, or scheduler-specific details in the
  first version.

## Design Principles

### Stable Resources Own User Intent

The stable resource remains the user-facing object. A load balancer resource
owns route configuration and lifecycle actions. A container app resource owns
image update and revision semantics. Provider-owned runtime containers,
processes, or deployments are implementation state unless a provider
deliberately projects them as child resources for inspection.

### Hosts Are Placement Boundaries

A host is a resource or configured runtime/control boundary that CloudShell can
target. It may be a `docker.host` resource, an implicit local Docker host from
`UseDocker()`, a Podman host, a Kubernetes cluster, a systemd machine, or a
vendor appliance API.

The selected host is a stable reference in provider-owned configuration when a
resource needs explicit placement. Existing fields named `ContainerHostId`
should migrate toward `ContainerHostId` or `HostResourceId`.

### Runtime State Is Provider-Owned

Provider-owned runtime state should be stored by the provider that creates it.
Platform registration remains responsible for resource identity, group
assignment, dependencies, parentage, and authorization boundaries. Runtime
details such as container IDs, generated config paths, transient health, and
host credentials stay behind provider contracts.

### Descriptors Are the Cross-Provider Contract

CloudShell already has `ResourceOrchestrationDescriptor` for provider-owned
descriptions. The host abstraction should build on that path:

- host providers describe a host through a host descriptor
- orchestrators and resource providers resolve explicit or default hosts from
  descriptors
- resource APIs continue to expose the uniform `Resource` projection, not
  provider-specific host internals

## Proposed Model

### Host Resource Projection

Concrete host providers project resources when the host should be visible or
manageable:

- Docker projects `docker.host` resources.
- Future Podman, Kubernetes, or systemd providers can project their own host
  resource types.
- The projected resource uses `ResourceClass.Infrastructure`.
- Non-secret attributes describe durable facts such as runtime family,
  registry, endpoint kind, or safe endpoint display.
- Credentials and transport details remain provider-owned.

Recommended future capability identifiers:

- `runtime.containerHost`: resource can host container-backed runtime work.
- `runtime.containerImage`: host can run image-based workloads.
- `runtime.containerBuild`: host can build images from local context.
- `runtime.hostProcess`: host can run process-backed helper services.
- `runtime.logs`: host can provide provider-owned runtime logs.

These should be added to `ResourceCapabilityIds` only when implementation work
needs them. Until then, providers can resolve host descriptors through
orchestration descriptors.

### Host Descriptor

Add a host-oriented descriptor shape alongside the current engine descriptor:

```csharp
public static class ContainerHostResourceTypes
{
    public const string ContainerHost = "cloudshell.container-host";
}

public enum ContainerHostKind
{
    Docker,
    Podman,
    DockerCompatible,
    Kubernetes,
    Process,
    Custom
}

public sealed record ContainerHostDescriptor(
    string Id,
    string Name,
    ContainerHostKind Kind,
    string Endpoint,
    bool IsDefault = false,
    string Registry = ContainerRegistryDefaults.Default,
    ContainerRegistryCredentials? RegistryCredentials = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    IReadOnlyList<string>? Capabilities = null);
```

This descriptor is not a new platform resource type by itself. It is the
provider-owned configuration payload returned by a resource descriptor.
Capabilities are stable, non-secret runtime role IDs used by the resolver when
a placement request names `RequiredCapability`. The current built-in IDs are
`container.image` and `container.build`.

Pre-release migration rule: code that selects placement should use host names
and emit `ContainerHostDescriptor`.

### Host Provider

The provider registration path uses a host-oriented provider:

```csharp
public interface IContainerHostProvider
{
    ContainerHostDescriptor GetDefaultHost();
}
```

This supplies a configured default host without requiring a user-managed host
resource. `UseDocker()` and `UseContainerHost(...)` register providers through
this contract.

### Host Resolution

Introduce one resolver service for runtime placement:

```csharp
public interface IContainerHostResolver
{
    Task<ContainerHostResolutionResult> ResolveAsync(
        ContainerHostResolutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ContainerHostResolutionRequest(
    string TargetResourceId,
    string? ResourceGroupId,
    string? ExplicitHostResourceId = null,
    string? PreferredHostId = null,
    string? RequiredCapability = null);

public sealed record ContainerHostResolutionResult(
    ContainerHostDescriptor? Host,
    string? ErrorMessage = null)
{
    public bool IsResolved => Host is not null && ErrorMessage is null;
}
```

Resolution order:

1. Explicit host resource ID from provider-owned resource configuration.
2. Preferred host ID from orchestration context or host/provider options.
3. Group-scoped default host selection when that exists.
4. Default host from `IContainerHostProvider`.
5. Default host resource descriptor discovered from registered resources.

The resolver returns diagnostics instead of throwing for expected missing-host
or unsupported-capability states. Orchestrators can convert those diagnostics
into resource action capability reasons, logs, or failed procedure results.

### Provider-Owned Runtime Contract

Providers that need to materialize implementation containers should use a
narrow owner-scoped runtime contract. This contract is about provider-owned
runtime state, not arbitrary container management:

```csharp
public interface IContainerHostRuntime
{
    bool CanUse(ContainerHostDescriptor host);

    Task<ProviderRuntimeHandle> EnsureAsync(
        ContainerHostDescriptor host,
        ProviderRuntimeSpec spec,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> StartAsync(
        ProviderRuntimeHandle handle,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> StopAsync(
        ProviderRuntimeHandle handle,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> RemoveAsync(
        ProviderRuntimeHandle handle,
        CancellationToken cancellationToken = default);

    Task<ProviderRuntimeStatus> ProbeAsync(
        ProviderRuntimeHandle handle,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderRuntimeSpec(
    string OwnerResourceId,
    string Name,
    string Image,
    IReadOnlyList<EnvironmentVariableAssignment>? EnvironmentVariables = null,
    IReadOnlyList<ServicePort>? Ports = null,
    IReadOnlyDictionary<string, string>? Labels = null);

public sealed record ProviderRuntimeHandle(
    string OwnerResourceId,
    string HostId,
    string RuntimeId,
    string ProviderId);

public sealed record ProviderRuntimeStatus(
    ResourceState State,
    string? Message = null,
    DateTimeOffset? CheckedAt = null);
```

Important constraints:

- Every runtime operation is owner-scoped.
- Providers must label or otherwise track runtime objects with the owning
  resource ID.
- Runtime handles are provider-owned state and may be projected as resource
  attributes only when safe and non-secret.
- The contract should start with `Ensure`, `Start`, `Stop`, `Remove`, and
  `Probe`. Log streaming can be added once a provider needs it.

This avoids a general-purpose Docker API in CloudShell while giving providers a
shared shape for provider-owned infrastructure.

### Child Resource Projection

Provider-owned runtime objects can be projected as child resources when useful,
but that is optional:

- A Traefik implementation container can be hidden provider-owned state.
- A Docker provider may project discovered containers as `docker.container`
  children for inspection, but those raw discoveries should be hidden
  runtime-managed artifacts by default.
- If projected, the child resource should name its owner or parent and should
  not become the stable deployment target for user actions such as app image
  updates.

Default UI behavior should keep the stable resource primary and show runtime
children as diagnostics/implementation detail.

Dependency relationships should not automatically become child-resource UI.
For example, a container app depending on a storage volume, DNS zone, or
container host does not make those dependencies sub-resources of the app.
Providers choose when a child relationship is part of their resource model, and
the generic Resource Manager child list still honors resource visibility.

For Docker specifically, the `docker.host` resource is the user-facing host
boundary. Raw Docker containers discovered from the host are useful operational
observations, but they should not appear in the global resource inventory by
default. The Docker host `Containers` tab intentionally shows those containers
to users who can view the host, while normal global inventory stays focused on
stable user-authored resources and explicitly declared Docker container
resources. The Docker host overview stays separate and summarizes host state,
connection, and projected container count.

### Remote Client/API Boundary

Host resolution and runtime operations happen in the Control Plane. UI and
remote clients should continue to use domain operations such as resource
actions, resource updates, logs, and templates.

Independent services created by providers should integrate with CloudShell
through the normal domain-shaped Control Plane API when they need to report
status, emit diagnostics, or participate in resource operations. They should
not introduce a second out-of-band local management API for the shell.

## Concrete Migration Plan

### Phase 1: Host Names

- Add host-oriented names:
  - `ContainerHostDescriptor`
  - `ContainerHostResourceTypes.ContainerHost`
  - `IContainerHostProvider`
  - `IContainerHostResolver`
- Remove or rename existing engine names as call sites move to the host model.
- Update docs and UI copy to say "container host" except where Docker Engine is
  the product being discussed.

### Phase 2: Resolver

- Implement `IContainerHostResolver` over:
  - explicit resource descriptor lookup
  - `IContainerHostProvider`
  - registered default host resources
- Docker Compose orchestration calls the resolver and does not keep a separate
  provider-local host lookup path.
- Use `ContainerHostId` on workload configuration for explicit placement.

### Phase 3: Provider-Owned Runtime

- Add `IContainerHostRuntime` for owner-scoped runtime objects.
- Implement Docker runtime support first.
- Use it for one provider-owned scenario, such as Traefik container mode for
  load balancers.
- Store runtime handles in provider-owned state.
- Project runtime logs/status through the stable parent resource first; add
  optional child resources only when the provider needs detailed inspection.

### Phase 4: Multi-Host Policy

- Add group-scoped default host selection if local/global defaults are not
  sufficient.
- Add host-readiness diagnostics and action capability reasons for:
  - no host available
  - host unavailable
  - required runtime capability missing
  - credentials unavailable
  - unsupported image/build mode
  Current resolver failures carry structured reason codes for the implemented
  host missing, default missing, unavailable host, and required-capability
  cases. Credential readiness remains tied to the provider-owned host
  credential model.
- Add host system setup affordances for missing runtime prerequisites. The UI
  should support both a global host setup view and per-resource prompts when a
  container app or runtime-managed resource needs a disabled OS/runtime feature.
  Windows should get explicit consideration because Hyper-V, WSL, Containers,
  firewall, networking, and Docker-compatible runtime prerequisites may need to
  be enabled before a host can run or expose workloads.
- Include default container-host selection in the broader environment setup
  experience. Operators should be able to choose the default host for container
  apps and provider-owned runtime infrastructure, see whether that host is
  ready, and understand which resources will inherit it. Resource-level host
  selectors remain useful for explicit placement, but the environment setup
  view should make the default hosting posture visible before resources are
  created.

## Example Flows

### Container App with Default Host

1. The app has no explicit host selection.
2. The orchestrator asks `IContainerHostResolver` for a container-image host.
3. The resolver returns the default Docker host from `UseDocker()`.
4. The Docker Compose orchestrator materializes the app using that host.
5. The app remains the stable resource for image updates and lifecycle actions.

### Load Balancer with Explicit Host

1. The load balancer definition stores `HostResourceId = "docker:build-01"`.
2. The load-balancer provider resolves that resource to a host descriptor.
3. The provider uses `IContainerHostRuntime` to ensure a Traefik runtime
   container owned by the load-balancer resource.
4. The load balancer exposes apply/restart/logs through its own resource
   actions and logs.
5. The Traefik container can remain hidden or be projected as a child
   diagnostic resource.

### Local Development without a Host Resource

1. `UseDocker()` registers a default host provider from the local Docker
   endpoint.
2. No `docker.host` resource needs to be registered in the user's group.
3. Container-backed resources still resolve a default host descriptor.
4. If the Docker provider is enabled, it may also project a visible
   `docker.host` resource; that projection is not required for runtime
   placement.

## Decisions

- The abstraction should be host-first, not engine-first.
- Default host resolution should be a reusable service, not duplicated inside
  each orchestrator.
- Pre-release implementation should converge on host-oriented contracts instead
  of keeping engine-named compatibility layers.
- Provider-owned runtime containers are implementation state tied to a stable
  resource.
- A hidden default host resource is optional; the required identity is the host
  descriptor, not a platform registration.
- Expected host-resolution failures should return diagnostics/action
  capability reasons, not unhandled exceptions.

## Remaining Tasks

- Migrate consuming code from the new host-oriented descriptor and provider
  contracts into the shared resolver. `ContainerHostDescriptor`,
  `ContainerHostResourceTypes.ContainerHost`, `IContainerHostProvider`,
  `UseContainerHost(...)`, host selection settings, and container resource
  builder host selection are in place.
- Docker Compose host materialization uses `IContainerHostResolver` and no
  longer keeps a separate provider-local engine lookup fallback.
- Missing explicit/default host resolution, unavailable host resources, and
  missing required host capabilities now feed resolver diagnostics. Missing
  explicit/default hosts also feed Start/Restart action capability reasons and
  execution uses the same domain unavailable-action error before provider
  dispatch. Container-image and container-build workloads now request matching
  host capabilities. Host descriptors now carry non-secret credential readiness,
  and unavailable host credentials feed the same resolver diagnostics path.
- Add a provider-owned Docker runtime implementation for owner-scoped
  implementation containers.
- Continue removing remaining engine naming from provider internals where it is
  not explicitly Docker Engine product terminology.
- Host descriptor capabilities are available for resolver-required capability
  validation. Docker hosts advertise the built-in container-image and
  container-build capabilities.
- Resolver tests cover explicit host selection, preferred host selection,
  configured default host selection, registered default host descriptors, and
  missing host, unavailable host, missing required-capability, and unavailable
  credential diagnostics. Add tests for provider-owned runtime cleanup as that
  slice lands.
- Update load-balancer container mode to use the runtime contract instead of
  modeling implementation containers as user-authored container apps.
