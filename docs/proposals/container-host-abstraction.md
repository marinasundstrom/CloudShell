# Container Host Abstraction Proposal

## Status

Proposed.

This proposal defines the internal runtime abstraction CloudShell should use
when a resource depends on container-backed or host-backed infrastructure, even
when that host is not explicitly added to the user-visible resource graph.

## Problem

This proposal is intentionally about the internal runtime contract that allows
resources and providers to depend on container-backed infrastructure, even when
that infrastructure is not a user-managed resource in the shell.

It overlaps with [Remote Docker Hosts Proposal](remote-docker-hosts.md), but
it addresses a different concern:

- the remote Docker hosts proposal defines the public, user-managed Docker host
  resource model
- this proposal defines the internal default host abstraction that providers
  rely on for runtime operations, probing, and dependent service integration

The two proposals should be read together, but they should stay separate to keep
UI ownership and provider runtime contracts clear.

Today the resource model distinguishes between:

- a user-managed Docker or container host resource, and
- a provider-owned runtime implementation that needs to create, inspect, or
  manage containers or independent services on behalf of a stable resource.

That split is useful for UI and ownership, but it leaves a missing piece: the
Control Plane still needs a stable internal container-host abstraction for
provider-owned runtime work.

This matters for scenarios such as:

- a load balancer provider creating its own Traefik runtime container
- a resource that depends on a Docker-backed helper service, even when the
  host itself is not modeled as a user-added resource
- a provider that launches an independent service and wants the Control Plane
  to interact with it through the normal remote client/API path instead of
  direct local-only wiring

## Goals

- Introduce a stable internal container-host abstraction for provider-owned
  runtime infrastructure.
- Make the default container host the primary runtime target, while keeping
  the abstraction capable of working across different host runtimes.
- Allow resources to access the default container or Docker host without
  requiring the host to be added as a user-managed resource.
- Support create, start, stop, remove, probe, inspect, and log operations for
  runtime containers or dependent services.
- Keep the abstraction provider-owned internally, while allowing resources to
  consume it through the existing domain/resource model.
- Use the remote Control Plane client/API path for independent services and
  resource-backed runtime operations whenever that is the preferred integration
  model.

## Non-Goals

- Do not replace the existing user-managed Docker host resource model.
- Do not require every provider to implement all runtime modes immediately.
- Do not standardize every container runtime capability in the first version.
- Do not make the internal host abstraction a public UI concept.

## Proposed Design

### 1. Internal host runtime contract

CloudShell should introduce an internal contract for container-backed runtime
infrastructure, for example:

```csharp
public interface IContainerHostRuntime
{
    Task<ContainerHostCapabilities> GetCapabilitiesAsync(CancellationToken ct);
    Task<ContainerHandle> CreateContainerAsync(ContainerSpec spec, CancellationToken ct);
    Task StartAsync(ContainerHandle id, CancellationToken ct);
    Task StopAsync(ContainerHandle id, CancellationToken ct);
    Task RemoveAsync(ContainerHandle id, CancellationToken ct);
    Task<ContainerStatus> ProbeAsync(ContainerHandle id, CancellationToken ct);
    Task<IReadOnlyList<ContainerSnapshot>> ListAsync(CancellationToken ct);
}
```

This interface is internal to the Control Plane/provider boundary. It is not the
same thing as the user-managed `docker.host` resource projected to Resource
Manager.

### 2. Default host resolution

The Control Plane should be able to resolve a default host in this order:

1. An explicitly selected host resource when one is available.
2. A configured preferred container host from provider/runtime settings.
3. A provider-default runtime host for the current environment.

This allows a resource to depend on a container host even when the host was not
added as a resource for user management.

### 3. Resource-backed runtime operations

A resource should be able to ask the Control Plane or provider to:

- create or start an implementation container for the resource
- inspect runtime state and health
- attach logs, diagnostics, or probe results
- stop or remove provider-owned runtime state when the resource is deleted or
  stopped

The stable resource remains the user-facing abstraction. The container or
service is implementation detail unless the user explicitly wants to manage it
as a workload.

This matches the existing load-balancer direction: the load balancer resource
owns the stable lifecycle and routing contract, while the provider owns the
runtime container or process behind it.

### 4. Remote client/API integration for independent services

When a resource launches an independent service, the preferred integration path
should be through the existing remote Control Plane client/API model.

That means:

- providers use the client API boundary rather than internal-only local calls
- control-plane operations for lifecycle, diagnostics, and resource updates
  remain remote and domain-shaped
- provider-owned services can still be launched locally or in a container, but
  the Control Plane-facing interaction should flow through the established Web
  API/client path

This keeps provider logic aligned with the same operational model used for
other resources and avoids a second, parallel way of talking to the runtime.

### 5. Relationship to resource resources

The user-managed Docker host resource remains the UI-owned way to:

- select which host a user wants to operate
- manage host credentials and provider configuration
- display discovered child containers and host details

The internal abstraction is the runtime integration point for provider-owned
infrastructure and dependencies. The two are related, but not the same thing.

One important design option is to treat the default container host as an
internal resource record anyway, even if it is hidden from the main resource
list by default. In that model, the host still has a stable resource identity
for provider-owned operations, reuse of the same action surface, and lifecycle
coordination, but it is not presented as a normal user-managed host in the
primary inventory UI. This remains the primary concrete runtime identity for
host-backed infrastructure, while dynamic child containers can continue to be
listed separately when the provider chooses to project them.

This would let providers use the same resource action model for host-backed
runtime infrastructure while keeping the default host out of the main user
management workflow. The proposal should keep that option open as an
implementation detail, not require it up front.

## Proposed Flow

1. A resource declares a dependency or provider-owned runtime need.
2. The Control Plane resolves the default or preferred host through the internal
   runtime abstraction.
3. The provider uses that abstraction to create, inspect, or manage the
   dependent container or service.
4. The resource and provider interact through the remote client/API path when
   the service is independent or needs Control Plane coordination.
5. The stable resource continues to expose lifecycle, logs, diagnostics, and
   actions through the normal resource model.

## Examples

### Load balancer-style runtime container

A load balancer provider can use the internal host runtime to create a Traefik
container on the selected host without requiring that host to be a user-managed
resource in the shell.

### Independent service launched by a resource

A provider launches a service that should persist or report back to the Control
Plane. The provider should use the normal client/API boundary for resource
status updates, action invocation, and diagnostics, rather than bypassing the
resource model with ad-hoc local calls.

## Remaining tasks

- Define the internal host runtime interface and its provider implementations.
- Decide how the default host is resolved when no user-managed host resource
  exists.
- Ensure provider-owned runtime containers remain distinct from user-managed
  Docker host resources.
- Wire the remote client/API path into provider runtime operations for
  independent services.
- Add tests for host resolution, runtime probing, and provider-owned resource
  lifecycle behavior.

## Relationship to the remote Docker hosts proposal

The default host abstraction should reuse the same host and runtime concepts
introduced in the Docker host proposal, but it should not depend on the host
being surfaced as a normal user resource. The primary target is the default
container host, with other host runtimes supported as future implementations.

This means:

- the Docker host proposal remains the place for registration, projection,
  credential handling, and uniqueness rules
- this proposal remains the place for default-host resolution, provider-owned
  runtime operations, and internal lifecycle integration

If the default host is eventually modeled as an internal resource record, that
should be treated as an implementation detail of this proposal, not as a reason
for merging the two designs.

## Open Questions

- Should the internal abstraction support only Docker-compatible hosts initially,
  or should it be generic enough for Podman, containerd, and scheduler-backed
  runtimes?
- Should the default host resolution be global, per resource group, or
  provider-specific?
- How should runtime failure and host-readiness diagnostics be surfaced to the
  UI and API?
- Should the abstraction expose a reusable “probe” contract for health checks,
  readiness, and dependency startup validation?
