# Resource Recovery Proposal

## Status

Proposed.

CloudShell already has resource health checks, liveness/readiness/startup probe
types, probe sources, lifecycle actions, action capability checks, and
lifecycle events. This proposal defines how those pieces should combine into
resource recovery: polling or evaluating a liveness signal, applying a restart
policy with backoff, and invoking the provider-owned restart action through
the normal Control Plane lifecycle path.

## Problem

Some resources can fail after they have started. A local process can exit, a
container can become unhealthy, a provider-owned runtime service can stop
responding, or a service endpoint can fail its liveness check while the
resource still appears to exist.

Today CloudShell can project lifecycle state and poll resource health checks,
but it does not have a shared policy that answers:

- which signal means the resource is no longer alive
- how many failed observations are needed before recovery starts
- whether CloudShell should restart the resource automatically
- how restart attempts back off after repeated failures
- where users configure and inspect that behavior
- how providers participate without owning Control Plane policy

Without a shared recovery model, each provider would need to invent its own
restart loop, event messages, retry timing, and UI configuration. That would
make Resource Manager behavior inconsistent and make split-hosted Control
Plane deployments harder to reason about.

## Goals

- Define resource recovery as a Control Plane concern over resource-owned
  signals and standard lifecycle actions.
- Reuse existing `ResourceHealthCheck`, `ResourceProbeType`, and
  `ResourceProbeSource` concepts where they fit, especially `Liveness`,
  `Readiness`, `Startup`, and HTTP as the first source.
- Keep provider-specific restart implementation behind existing resource
  actions and action capability checks.
- Let providers contribute or evaluate liveness signals without owning the
  platform recovery policy.
- Support exponential backoff, maximum attempts, and reset-after-healthy
  behavior.
- Make recovery configurable from Resource Manager for resources that support
  the required signal and restart capability.
- Record recovery attempts and skipped attempts through resource events so
  activity, diagnostics, and future automation can explain what happened.
- Keep recovery policy separate from host restart cleanup and provider runtime
  reattachment.

## Non-Goals

- Do not build a general workflow engine.
- Do not make health polling itself execute lifecycle actions.
- Do not restart resources from UI components or browser sessions.
- Do not treat readiness failures as restart triggers by default.
- Do not require every resource with health checks to support automatic
  recovery.
- Do not replace provider-native orchestration when a provider already owns a
  durable restart policy, such as an external scheduler. In that case
  CloudShell should project the provider policy and diagnostics instead of
  running a competing loop.
- Do not make workload crash recovery part of host restart recovery. Host
  restart recovery should reconcile resources bound to the host lifetime;
  resource recovery handles failed resources during normal operation.

## Terminology

Use these terms consistently:

| Term | Meaning |
| --- | --- |
| Probe | A resource-owned signal CloudShell can evaluate. It may be HTTP-based, provider-native, process-based, or another provider-contributed signal. |
| Liveness probe | A probe that answers whether the resource should be considered alive. Liveness failure can trigger recovery. |
| Readiness probe | A probe that answers whether the resource is ready to serve traffic or dependencies. Readiness failure should affect status, diagnostics, and future routing decisions, not automatic restart by default. |
| Startup probe | A probe or grace signal used during startup so slow-starting resources are not recovered prematurely. |
| Recovery policy | Platform-owned per-resource intent that decides if and when CloudShell should attempt recovery after liveness failure. |
| Recovery controller | Control Plane background component that evaluates recovery policy and invokes lifecycle actions when recovery is due. |
| Recovery attempt | One Control Plane-initiated lifecycle action, normally `Restart`, caused by recovery policy. |

Resource Manager should present the feature as **Recovery** or **Automatic
restart**. "Probe" is appropriate in developer-facing docs and diagnostics, but
it should not be the primary user-facing label for the workflow.

The existing Health UI should keep calling these declarations health checks.
`ResourceProbeSource` and `IResourceProbeEvaluator` are implementation and
extension concepts that let Resource Manager poll signals consistently without
making HTTP the only source type.

## Model

The recovery flow should be:

```text
Control Plane evaluates resource liveness signal
        ↓
Recovery policy decides whether a restart is due
        ↓
Control Plane evaluates Restart action capability
        ↓
Control Plane invokes the resource Restart action
        ↓
Provider performs provider-specific restart behavior
```

The recovery controller must use the same lifecycle action path as a user or
API caller. That means recovery attempts still pass through resource
resolution, lifecycle state policy, provider support, action capability
checks, dependency policy, provider dispatch, and resource event recording.

The first policy shape should be per resource:

- enabled or disabled
- selected liveness signal, by probe name or probe type
- failure threshold before recovery is attempted
- startup grace period
- initial backoff
- maximum backoff
- backoff multiplier
- maximum restart attempts
- healthy duration required to reset attempts
- recovery action, initially constrained to `restart`

The first implementation can keep this policy platform-owned and resource
scoped. Provider-specific resource configuration should not store the generic
recovery policy unless the provider owns an external scheduler that must
receive a projected policy.

## Signals

CloudShell already has:

```csharp
public enum ResourceProbeType
{
    Health,
    Liveness,
    Readiness,
    Startup
}
```

The first recovery implementation should reuse this vocabulary and the shared
probe source/evaluator model. Existing HTTP health checks can become recovery
inputs when their type is `Liveness`. Provider-native signals can be evaluated
through `IResourceProbeEvaluator` implementations registered by providers.

Recommended first signal behavior:

- `Liveness`: eligible to trigger recovery when policy is enabled.
- `Readiness`: not a restart trigger by default; useful for diagnostics and
  future routing/load-balancer decisions.
- `Startup`: suppresses premature recovery while a resource is starting.
- `Health`: remains a general status signal unless the user or provider
  explicitly maps it as the liveness signal.

When a resource has no liveness-capable signal, Resource Manager should not
offer automatic restart configuration unless a provider contributes an
equivalent provider-native liveness signal.

## Provider Responsibilities

Providers participate in recovery without owning the generic restart loop:

- Project resource lifecycle state when they can observe it.
- Project HTTP probe definitions or contribute provider-native liveness
  signals through `ResourceProbeSource` where appropriate.
- Register `IResourceProbeEvaluator` implementations when they can evaluate a
  non-HTTP source for Resource Manager polling.
- Advertise `Restart` only when the resource can be restarted.
- Return action capability reasons when restart is currently unavailable.
- Execute restart through the normal provider lifecycle action.
- Optionally project provider-native recovery policy and diagnostics when an
  external orchestrator owns recovery.

Providers should not start private background restart loops for CloudShell
resources unless the provider-native runtime is the recovery owner. If a
provider-native orchestrator owns restart behavior, CloudShell should surface
that as provider-owned policy or diagnostics rather than configuring the
Control Plane recovery controller for the same resource.

## Control Plane Responsibilities

The Control Plane owns the reusable recovery behavior:

- Store and validate resource recovery policy.
- Evaluate liveness signals on a background cadence.
- Track consecutive failures, backoff state, next attempt time, last attempt
  result, and reset-after-healthy state.
- Invoke `Restart` through `IResourceManager` or the internal equivalent
  lifecycle orchestration boundary.
- Skip and record recovery attempts when action capability checks make restart
  unavailable.
- Record resource events for recovery decisions, attempts, skipped attempts,
  exhausted attempts, and reset-after-healthy transitions.
- Expose policy and runtime recovery status through domain managers and the
  Control Plane API for split hosting.

For the local-development MVP, the recovery controller can be an in-process
background service. For shared or on-premise deployments, it should follow the
primary-controller and worker direction already documented for Control Plane
scale-out so multiple API replicas do not run competing restart loops.

## Resource Manager UX

Resource Manager should show Recovery configuration only when the resource has
a liveness-capable signal and supports restart, or when a provider projects a
provider-native recovery policy.

The generated Recovery surface should support:

- automatic restart enabled/disabled
- selected liveness signal
- failed checks before restart
- startup grace period
- initial and maximum backoff
- maximum attempts
- reset-after-healthy duration
- current recovery status
- last failed signal and detail
- next scheduled attempt
- restart capability warnings
- recent recovery-related activity

For resources where a provider owns richer behavior, the provider can replace
or extend the generated Recovery view. The provider-owned UI should still use
the same domain policy and status APIs when CloudShell owns recovery.

## Events And Diagnostics

Recovery events should be separate from lifecycle action events. A recovery
attempt can cause a normal `Restart action`, `Restarting`, and `Restarted` or
`Restart failed` sequence, but the activity stream should also explain why the
restart was attempted.

Potential event types:

- `event.recovery.signal.failed`
- `event.recovery.restart.scheduled`
- `event.recovery.restart.skipped`
- `event.recovery.restart.exhausted`
- `event.recovery.reset`

These event names are illustrative. The implementation should align them with
the resource event taxonomy before landing.

Skipped recovery should preserve the reason:

- restart action not advertised
- restart action unavailable by lifecycle state
- authorization or recovery identity lacks permission
- dependency startup blocked
- provider preflight failure
- provider-native recovery owner is active
- maximum restart attempts exhausted

## Relationship To Existing Proposals

This proposal builds on [Lifecycle orchestration](lifecycle-orchestration.md).
Recovery attempts must execute through the lifecycle orchestration path instead
of dispatching directly to providers.

It also builds on [Resource monitoring](resource-monitoring.md), but it is not
the same feature. Resource monitoring provides provider-observed metrics and
status. Resource recovery consumes liveness signals and lifecycle action
capabilities to decide whether CloudShell should attempt restart.

The host/runtime distinction remains the same as
[ADR-20260614-004](../../../ADR.md): workload crash recovery is separate from
host restart recovery. Host restart recovery reconciles host-bound resources
after a host process restarts. Resource recovery handles normal-operation
liveness failure and restart policy.

## MVP Direction

The first landed slice keeps the scope to the shared signal foundation:

- `ResourceProbeSource` identifies the health signal source.
- HTTP is the built-in source through `ResourceHttpProbeSource`.
- `IResourceProbeEvaluator` lets the Control Plane poll HTTP and future
  provider-native sources through the same abstraction.
- Existing `WithHttpHealthCheck(...)`, `WithHttpProbe(...)`, and Health UI
  wording remain in place.

The second landed slice adds the first policy/status surface:

- `ResourceRecoveryPolicy` records resource-scoped automatic restart intent.
- `ResourceRecoveryStatus` gives Resource Manager a stable status shape before
  the restart loop exists.
- `IResourceRecoveryManager` exposes policy and status through the in-process
  Control Plane and remote client/API.
- Stored policy is currently in-memory Control Plane state. Durable persistence
  should land with or before the recovery controller.

The next recovery slice should stay narrow:

1. Evaluate `ResourceProbeType.Liveness` results from the shared probe
   evaluator path as the first restart trigger.
2. Invoke `Restart` through existing lifecycle orchestration.
3. Show generated Recovery configuration and status in Resource Manager.
4. Add sample coverage for one application resource with a liveness probe and
   automatic restart policy.

Provider-native signal contracts, external orchestrator policy projection,
durable controller leases, and advanced event schemas can follow after the
local-development loop proves the model.

## Open Questions

- Should the recovery controller run under a dedicated platform identity, the
  resource owner, or a system actor with explicit audit metadata?
- Should failed readiness ever be allowed to trigger restart by configuration,
  or should restart remain liveness-only?
- Should recovery policy be configured on resource types as defaults, then
  copied to resource instances like health-check defaults?
- Should maximum attempts disable the policy until a user resets it, or pause
  until the resource becomes healthy again?
- Should recovery state be stored as operational cache, durable Control Plane
  state, or both?
- How should CloudShell represent provider-native recovery ownership when an
  external scheduler or orchestrator already owns restart policy?
