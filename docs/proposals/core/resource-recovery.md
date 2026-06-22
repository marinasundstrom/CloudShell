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
- whether CloudShell should restore the resource automatically
- how recovery attempts back off after repeated failures
- where users configure and inspect that behavior
- how providers participate without owning Control Plane policy

Without a shared recovery model, each provider would need to invent its own
restart loop, event messages, retry timing, and UI configuration. That would
make Resource Manager behavior inconsistent and make split-hosted Control
Plane deployments harder to reason about.

## Goals

- Define resource recovery as a Control Plane concern over resource-owned
  signals and standard lifecycle actions.
- Treat recovery as an explicit per-resource management capability that can be
  enabled only when the resource has a suitable liveness signal and lifecycle
  action support that can restore it.
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

Recovery is a capability that depends on a liveness signal, not on broad
Health status. A resource can expose health checks without supporting
automatic recovery, and recovery should not appear as configurable until
CloudShell can identify both a liveness-capable signal and a lifecycle action
that can restore the resource.
The resource model should represent liveness support and recovery support as
separate capabilities, with recovery declaring its dependency on liveness.
The first capability IDs are `liveness` and `recovery`; `recovery` describes
support for configuring recovery policy, not whether recovery is currently
enabled for the resource.

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
- recovery action, initially `Restart` for running or degraded resources and
  `Start` for an unexpected stopped runtime after recovery has already
  observed the resource healthy
- recovery cause, so lifecycle events can explain why the resource is being
  restored

The first implementation can keep this policy platform-owned and resource
scoped. Provider-specific resource configuration should not store the generic
recovery policy unless the provider owns an external scheduler that must
receive a projected policy.

Programmatic authoring should declare recovery on the resource builder beside
health and liveness probe declarations, such as `WithRecovery(...)` on an
application resource. The resource model carries recovery policy declarations;
the Control Plane materializes the currently supported policy into operational
recovery state for polling, status, and restore decisions.

As the liveness model matures, recovery policy should split trigger behavior
by observed state transition instead of treating every failed liveness signal
the same way. At minimum, resources should be able to configure:

- whether `Degraded` should trigger restart and how many attempts are allowed
  for degraded recovery
- whether `Stopped` after an unexpected liveness loss should trigger restart
  and how many attempts are allowed for stopped recovery

Those state-trigger sections can still share default backoff settings, but
the policy should allow a resource or operator to treat "responding but
unhealthy" differently from "not responding and probably stopped."

Liveness check results should carry structured outcome data so the Control
Plane does not need to parse transport or provider messages. The initial
outcomes distinguish real responses, no response, unresolved probe targets,
unsupported probe sources, and unknown synthetic observations.

Liveness checks are best-effort observations. A failed check should appear in
Health immediately, but lifecycle status changes, recovery attempts, and
resource activity entries should wait until the configured failure threshold
is reached. Once the threshold is reached, the latest liveness outcome should
be trusted for the resource status transition.

Liveness and recovery should be active only for running resources. If a
resource is stopped intentionally through a lifecycle action, CloudShell should
not keep polling its liveness signal and should not treat that stopped state
as a recovery trigger.

Provider-observed stopped state is different when recovery already saw the
resource healthy under an enabled policy. If a container-backed resource was
running and its container disappears, the provider can project the resource as
`Stopped` before the liveness check gets another response. Recovery should
treat that as an unexpected runtime loss and use the normal `Start` action
when policy allows it. For SQL Server, the TDS liveness signal still describes
the SQL service, while the container stopped/deleted state describes the
runtime backing that service.

During normal local development, recovery is usually not enabled by default:
the developer controls the host process and can recreate or restart the whole
resource graph from programmatic declarations. Recovery is still useful for
resilience testing, demonstrations, and managed or shared environments where
CloudShell is expected to keep selected resources alive without operator
intervention.

Health and liveness checks should use the shared polling cadence by default so
resources are sampled predictably and operators can compare results from the
same polling window. Individual checks can override that interval when the
target or development scenario calls for a different cadence. For example, a
SQL Server liveness check should not need to open a database connection as
often as a cheap in-process HTTP endpoint.

Intervals and per-check timestamps should also feed future visualization. The
dashboard can show freshness per health or liveness signal, render
cadence-aware timelines, and explain which liveness signal drove a lifecycle or
recovery decision.

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

The first recovery implementation reuses this vocabulary and the shared probe
source/evaluator model. Existing HTTP health checks can become recovery inputs
when their type is `Liveness`. Provider-native signals can be evaluated
through `IResourceProbeEvaluator` implementations registered by providers.

The model should still move toward separate resource capabilities for health
checks, liveness signals, and recovery policy. `ResourceProbeType.Liveness`
is the first bridge from existing health-check declarations to recovery, not a
decision that liveness must remain only a subtype of health forever.

Recommended first signal behavior:

- `Liveness`: eligible to trigger recovery when policy is enabled.
- `Readiness`: not a restart trigger by default; useful for diagnostics and
  future routing/load-balancer decisions.
- `Startup`: suppresses premature recovery while a resource is starting.
- `Health`: remains a general status signal unless the user or provider
  explicitly maps it as the liveness signal. Broad or aggregate Health checks
  can represent many health scopes, such as services, dependencies, related
  resource sets, instances, replicas, or routes, through a single HTTP JSON
  response, provider-native payload, or future Control Plane-provided health
  endpoint. Those aggregate scopes should not imply recovery by themselves.

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
- Invoke the selected lifecycle action through `IResourceManager` or the
  internal equivalent lifecycle orchestration boundary.
- Skip and record recovery attempts when action capability checks make the
  selected lifecycle action unavailable.
- Record resource events for recovery decisions, attempts, skipped attempts,
  exhausted attempts, and reset-after-healthy transitions.
- Expose policy and runtime recovery status through domain managers and the
  Control Plane API for split hosting.

For the local-development MVP, the recovery controller can expose an explicit
refresh/evaluation operation that the combined host can call. The periodic
polling loop should remain separable from request-serving Control Plane APIs:
future shared or on-premise deployments should let a primary controller or
separate worker process periodically request resource health state and recovery
evaluation so multiple API replicas do not run competing restart loops.

## Recovery States And Transitions

The first recovery controller uses `ResourceRecoveryState` to describe the
policy runtime state, and the resource lifecycle state to describe the
resource itself. The important scenarios are:

| Scenario | Resource state | Health/liveness behavior | Recovery state | Lifecycle action |
| --- | --- | --- | --- | --- |
| Policy disabled | Any | No recovery evaluation | `Disabled` | None |
| Initial or intentionally stopped resource | `Stopped` | Liveness is inactive | `WaitingForSignal` | None |
| Running and healthy | `Running` | Selected liveness signal is healthy | `Healthy` | None |
| Running with failed liveness below threshold | `Running` | Failure count is recorded | `Failing` | None |
| Running with failed liveness at threshold | `Running` or `Degraded` | Failure count reaches policy threshold | `Restarting` or `Scheduled` | `Restart` |
| Provider observes runtime loss after healthy recovery signal | `Stopped` | Liveness is inactive because the resource is already stopped | `Restarting` or `Scheduled` | `Start` |
| Recovery attempts exhausted | Any recoverable failure state | Failure threshold is met but attempts are exhausted | `Exhausted` | None |
| Matching liveness signal cannot be found or evaluated | Any active state | Signal is unknown, unsupported, or unresolved | `Unavailable` | None |

This keeps manual stop and unexpected runtime loss separate. A CloudShell Stop
action intentionally moves the resource out of liveness and recovery, and
clears the recovery runtime observation while leaving the declared policy in
place. A provider-observed `Stopped` state after recovery previously saw the
resource healthy is treated as unexpected runtime loss, such as a deleted
container, and recovery can use `Start` to recreate the backing runtime.

## Resource Manager UX

Resource Manager should show Recovery configuration only when the resource has
a liveness-capable signal and supports a lifecycle action that can restore it,
or when a provider projects a provider-native recovery policy.

The generated Resource Manager view should place Recovery under the resource
Management area. The Health tab remains the health-check and health-state
surface, and should link to the Recovery tab when recovery is available so
users can move from a failed liveness signal to the automatic restart policy
that consumes it.

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
- restore-action capability warnings
- recent recovery-related activity

For resources where a provider owns richer behavior, the provider can replace
or extend the generated Recovery view. The provider-owned UI should still use
the same domain policy and status APIs when CloudShell owns recovery.

Dashboard and Health summary surfaces should also expose liveness and
degradation state when those signals exist. The detailed configuration belongs
in Recovery, but operators should not need to open a resource detail page to
notice that liveness is failing or that a resource has degraded.

## Events And Diagnostics

Recovery events should be separate from lifecycle action events. A recovery
attempt can cause a normal `Restart action`, `Restarting`, and `Restarted` or
`Restart failed` sequence, with the restart cause set to the liveness failure
that triggered recovery. The activity stream should also explain why recovery
acted and whether the recovery attempt succeeded or failed.

Potential event types:

- `event.lifecycle.degraded`
- `event.lifecycle.stopped.unexpectedly`
- `event.recovery.signal.failed`
- `event.recovery.restart.attempted`
- `event.recovery.restart.scheduled`
- `event.recovery.restart.succeeded`
- `event.recovery.restart.failed`
- `event.recovery.restart.skipped`
- `event.recovery.restart.exhausted`
- `event.recovery.reset`

These events are operational activity. Future user-facing notifications can
subscribe to the same taxonomy, but notification delivery is part of the
broader notifications story.

Future operational notifications should be published for liveness failures,
degradation transitions, recovery attempts, skipped attempts, exhausted
attempts, and reset-after-healthy transitions. That belongs to the broader
notifications story rather than the first recovery controller slices, but the
event taxonomy should preserve enough structure for notifications to consume
later.

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

The third landed slice adds the controller core as an explicit refresh path:

- `RefreshResourceRecoveryAsync` evaluates the configured liveness signal
  through the shared health probe evaluator path.
- Failed liveness results update consecutive failure state, backoff timing,
  and attempt counts.
- When the failure threshold and backoff policy allow it, recovery invokes the
  normal `Restart` lifecycle action with `TriggeredBy` set to `recovery`.
- `Unknown` liveness results do not trigger restart because CloudShell cannot
  prove the resource is dead.
- The controller core is callable through the in-process manager and
  Control Plane API/client. A hosted background polling loop remains deferred.
- The periodic scheduler remains deliberately separate so future deployments
  can move polling to a primary controller or worker process.

The fourth landed slice adds opt-in local-development polling:

- `ResourceManager:Recovery:EnableLocalPolling` allows a combined host to run
  the recovery refresh loop locally.
- `ResourceManager:Recovery:PollIntervalSeconds` can override the recovery
  polling cadence; otherwise recovery uses the configured health-check
  interval.
- The polling host enumerates enabled resource recovery policies and calls the
  same `RefreshResourceRecoveryAsync` path exposed through the Control Plane.
- Local polling is disabled by default so request-serving Control Plane hosts
  do not implicitly become the long-term owner of singleton recovery work.

The fifth landed slice adds shared capability projection:

- Resources with a liveness probe project the `liveness` capability.
- Resources with both the `liveness` capability and a Restart action project
  the `recovery` capability.
- The projected `recovery` capability carries metadata declaring its
  dependency on the `liveness` capability.
- Generic health checks do not imply liveness or recovery support.

The sixth landed slice connects liveness to lifecycle status:

- Latest unhealthy liveness results project otherwise active resources as
  `Degraded`.
- Generic health failures do not project lifecycle degradation.
- Stopped resources remain stopped when liveness fails, because not responding
  is expected for a stopped resource.
- This makes recovery policy able to build on the normal lifecycle status
  model instead of treating failed liveness as a private recovery-only fact.

The seventh landed slice adds structured liveness outcomes:

- `ResourceHealthCheckResult` now carries a `ResourceHealthCheckOutcome` so
  Control Plane logic can distinguish a real unhealthy response from no
  response, unsupported sources, unresolved probe targets, and synthetic
  unknown observations.
- HTTP probe failures from non-2xx/3xx responses remain
  responding-but-unhealthy results, while timeouts and request failures are
  no-response results.
- Consecutive unhealthy liveness results that meet the configured failure
  threshold can project otherwise active resources as `Stopped` when the latest
  result has a no-response outcome; responding-but-unhealthy liveness projects
  active resources as `Degraded`.

The eighth landed slice connects liveness and recovery to resource activity:

- Liveness-driven lifecycle transitions record `event.lifecycle.degraded` or
  `event.lifecycle.stopped.unexpectedly` activity when the configured failure
  threshold is reached.
- Recovery-triggered restart actions carry a cause built from the failed
  liveness signal, so normal restart lifecycle events include why the restart
  happened.
- Recovery now records restart attempted, succeeded, and failed activity in
  addition to scheduled, skipped, exhausted, and reset activity.
- Resource builders can declare recovery with `WithRecovery(...)`, and the
  Application Topology sample enables recovery for the API project so liveness
  and recovery can be tested against a real web application resource.
- Recovery waits for resources to be running before checking liveness, so an
  intentional manual stop does not trigger automatic restart.

The ninth landed slice adds the first Resource Manager recovery surface:

- Generated resource details show a Management > Recovery tab when the
  resource declares recovery policy or projects the recovery capability.
- The generated Recovery tab reads the Control Plane recovery policy and
  runtime status, including the selected liveness signal, threshold, backoff,
  attempts, last/next timestamps, and latest detail.
- Resource-scoped Health links to Recovery when recovery is available, keeping
  Health focused on health checks while making the consuming restart policy
  discoverable.
- The Recovery UI reads status only; invoking recovery refresh remains owned by
  the recovery controller or polling loop because refresh can act on policy.

The tenth landed slice adds per-check polling cadence and built-in SQL Server
liveness support:

- `ResourceHealthCheck` can carry an optional interval override so different
  resources and checks can be sampled at different cadences while the global
  health interval remains the fallback.
- Health check results now keep per-check `CheckedAt` timestamps, allowing
  later Health and Recovery visualizations to show mixed-cadence samples
  without implying every check was evaluated at the summary timestamp.
- Background health refresh respects per-check intervals and reuses previous
  results for checks that are not due; explicit refresh requests still force a
  fresh evaluation.
- SQL Server resources declare a provider-native liveness check backed by a
  short TDS connection through the projected `tds` endpoint.

The eleventh landed slice stabilizes stopped-state recovery:

- Recovery remains visible for resources that have a liveness signal and a
  `Start` action, so stopped-but-startable resources do not lose the Recovery
  tab.
- Recovery starts a stopped resource only after an enabled recovery policy has
  previously observed a healthy signal for that resource. Initial or
  intentionally stopped resources still wait for the resource to become
  running instead of being probed or recovered.

The next recovery slice should stay narrow:

1. Add integration validation that kills the process behind an ASP.NET Core
   project resource or the container behind a SQL Server resource, then
   observes liveness-driven lifecycle transition and recovery behavior.
2. Decide whether a separate liveness state or resource condition is still
   needed alongside the primary lifecycle status for resources where providers
   own more nuanced alive/not-alive semantics.
3. Track resource state transitions together with liveness observations so
   recovery policy can decide whether to wait, restart, or leave provider
   state authoritative based on the observed transition path.
4. Split recovery policy trigger configuration so degraded recovery and
   stopped recovery can each opt into restart behavior and define their own
   maximum attempts.

Provider-native signal contracts, external orchestrator policy projection,
durable controller leases, and advanced event schemas can follow after the
local-development loop proves the model.

Application providers may later need aggregate liveness for resources backed
by multiple executables, containers, or replicas. In that model, one failed
unit may represent degraded capacity rather than a stopped resource, while
all units failing may represent stopped or failed resource state. The provider
should own that aggregation and expose the resulting liveness/degradation
signal through the shared resource model instead of forcing the generic
recovery controller to infer per-unit semantics.

## Open Questions

- Should the recovery controller run under a dedicated platform identity, the
  resource owner, or a system actor with explicit audit metadata?
- Should failed readiness ever be allowed to trigger restart by configuration,
  or should restart remain liveness-only?
- Should recovery policy be configured on resource types as defaults, then
  copied to resource instances like health-check defaults?
- Should liveness become a first-class resource-model concept separate from
  health checks, or remain a typed probe with separate capability metadata?
- Do resources also need a separate liveness state or condition for cases
  where liveness should not directly degrade the primary lifecycle status?
- Which state transitions should recovery policy react to directly, and which
  transitions should be left to provider-owned lifecycle reconciliation?
- Should degraded recovery and stopped recovery share one backoff schedule, or
  should each state trigger maintain its own backoff and attempt counters?
- Should maximum attempts disable the policy until a user resets it, or pause
  until the resource becomes healthy again?
- Should recovery state be stored as operational cache, durable Control Plane
  state, or both?
- How should CloudShell represent provider-native recovery ownership when an
  external scheduler or orchestrator already owns restart policy?
