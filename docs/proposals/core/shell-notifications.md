# Shell notifications and toasts

CoreShell should provide product-neutral notification building blocks that
CloudShell and other shell hosts can use for user-visible operational signals.
The first goal is not a full activity-log or workflow engine. The first goal is
a common notification center and toast presentation model that can show users
when background work starts, progresses, succeeds, fails, or needs attention.

## Status

- Status: Partially implemented
- Strategy fit: Medium-high; supports Azure-like operation feedback and
  strengthens CoreShell as the common shell layer without forcing CloudShell
  resource semantics into CoreShell.
- Canonical feature docs:
  [Shell customization](../../shell-customization.md) and
  [UI composition](../../ui-composition.md) describe the landed CoreShell
  notification UI contract and sample reference path.
- Remaining action: expand the first Control Plane notification projection
  beyond in-memory lifecycle event coalescing to durable storage, rule
  configuration, audience resolution, producer update APIs, SignalR delivery,
  and additional operation producers.
- Out of scope: full workflow orchestration, durable CloudShell activity
  history, email/push/device delivery, notification preferences UI, and
  cross-Control Plane federation.

## Goals

- Give CoreShell a product-neutral notification contract that any shell host
  can implement or consume.
- Separate notification records from change delivery so persistence, remote
  fetching, SignalR, polling, or other delivery channels can vary by host.
- Support audience targeting for a specific user, group, role, tenant, or all
  users, with fan-out into per-user notification instances where each user can
  read, acknowledge, dismiss, or later act on their own copy.
- Provide a common UI placement for asynchronous operation feedback, including
  transient toasts and a longer-lived notification center.
- Let CloudShell publish resource-operation notifications from Control Plane
  facts created by Resource Manager commands, Control Plane operations,
  providers, or extension-owned services while keeping operation history and
  diagnostics in their owning systems.
- Keep the initial CoreShell sample simple enough to be a reference app for
  future CoreShell development.

## Non-goals

- Do not make notifications the source of truth for resource lifecycle,
  deployments, logs, traces, metrics, or audit history.
- Do not encode CloudShell resource, Control Plane, provider, or authorization
  concepts directly in CoreShell notification records.
- Do not require a database, SignalR, or any one transport in CoreShell.
- Do not turn every toast into a durable notification. The host should be able
  to decide whether a notification is durable, transient, or both.
- Do not add user notification preferences in the first slice. Preference
  scope and storage should wait until the record, audience, and delivery
  contracts are proven.

## Concept model

Notifications have four major concerns:

1. **Record source and storage**: where notification records come from and how
   they are queried. Examples include an in-memory store, a database-backed
   store, a remote Control Plane feed, or a fetched external service.
2. **Change signal**: how a connected shell learns that notification state may
   have changed. Examples include in-process events, SignalR, server-sent
   events, polling, or a broker subscription.
3. **Audience and visibility**: who the notification is intended for and who
   is allowed to see it. Examples include one user, a group, all users in an
   environment, users with a role, or an extension-defined audience that the
   host resolves.
4. **Recipient instance state**: each user's copy of a delivered notification,
   including read, acknowledged, dismissed, archived, action availability, and
   any future per-user action outcome.

The CoreShell UI contract should stay flexible about that first concern. A
simple CoreShell host can keep notifications in memory, store them in the UI
process, fetch them from another service, or adapt any host-specific source.
CoreShell presenters should only care that they can query current-user items,
handle actions, acknowledge or dismiss records, and react to change signals.
CloudShell makes a narrower product choice: because resource operations,
authorization, events, and diagnostics are Control Plane-owned, CloudShell
notification records should also use the Control Plane as their source of
truth.

The normal CloudShell shape should be per-user notification instances. A
producer may publish one logical notification event to a group or to all users,
but delivery should materialize recipient-specific instances so each user can
acknowledge or dismiss their notification independently. This avoids shared
state bugs where one user's dismissal hides the same notification for another
user.

The proposal therefore distinguishes:

- **Notification event**: the logical thing that happened, such as "resource
  creation started" or "artifact upload failed".
- **Notification instance**: the per-recipient shell item shown to one user,
  derived from an event or created directly for that user.

The change signal should not be treated as the source of truth. A signal can be
lost, duplicated, delayed, or delivered out of order. It should normally tell
the client to re-query notifications from the record source, optionally with a
cursor or checkpoint. That keeps a SignalR-backed implementation and a polling
implementation compatible with the same UI contract.

Toasts are presentation, not storage. A toast is a short-lived view over a
notification instance, event update, background task update, or transient UI
signal. A notification center is a longer-lived view over queryable
per-recipient instances. The same user-visible event may appear in both
places, only one place, or neither place if the host filters it out.

Notifications and toasts should therefore be related but not identical:

- A notification can request toast presentation when the item is timely,
  actionable, or needs user attention.
- A notification can suppress toast presentation when the item belongs only in
  history, is low urgency, or was already represented by another signal.
- A toast-only signal can exist for ephemeral feedback that should not create a
  notification-center item. The landed CoreShell reference path exposes this
  through `ICoreShellToastService`.
- Publishing a notification or toast should return the created item. The
  returned `Id` is the controller reference that background work can use to
  update, dismiss, or replace progress feedback when the operation changes
  state.
- Visibility, lifetime, and auto-dismiss behavior are notification/toast data.
  A service may support immediate visibility, scheduled visibility through
  `VisibleAt` or `VisibleIn`, default time-to-live dismissal, or
  never-auto-dismiss operation feedback. Unsupported scheduling should be an
  implementation decision, not a missing CoreShell field.
- A notification or toast can request a specific presentation template when
  the default title/message/actions layout is not enough. Template selection
  should be data-driven, for example an optional template key plus structured
  attributes, so CoreShell remains product-neutral while CloudShell or another
  host can register richer renderers.
- A toast should render notification actions when the backing item provides
  them. If the user ignores the toast, the same action remains available in
  the notification center.
- If no actions are present, a toast or notification can fall back to a
  whole-body target link, or to dismiss-on-click when no target exists.
- A toast or notification can show a loading/progress indicator when it
  represents in-progress background work.

## Proposed CoreShell boundary

CoreShell should own stable shell-level contracts such as:

- notification event and per-recipient notification instance shapes
- a notification query contract
- publish/update contracts for notification producers
- producer publish methods that return the created notification or toast item,
  including the ID needed for later update or dismissal
- acknowledge/dismiss contracts for per-recipient instances
- optional notification action descriptors and action-handling hooks
- notification toast behavior hints, including suppression
- a separate transient toast contract for toast-only signals
- a change subscription contract
- audience descriptors that stay product-neutral
- presentation data such as severity, status, route/href, source label, toast
  eligibility, scheduled visibility, time-to-live, auto-dismiss behavior, and
  renderer/template selection

CoreShell should not own:

- CloudShell resource operation execution
- Control Plane persistence schema
- authorization decisions for CloudShell users, groups, or roles
- provider-owned diagnostics or operation logs
- remote transport details such as SignalR hub route names
- CloudShell notification production policy

The initial CoreShell implementation provides the UI-facing
`ICoreShellNotificationService` contract and an empty default implementation
for hosts that have not wired a notification source yet. A later sample slice
can add an in-memory source for simple hosts. That implementation would be
useful for proving the UI and extension model, but it should be documented as
sample/default behavior, not as the only supported implementation.

`ICoreShellNotificationProducer` is an adapter contract, not a statement that
the producer must run in the Blazor UI host. A combined development host can
implement it in-process. A split deployment can implement it as a client for a
remote notification capability. In CloudShell, that remote capability should
be the Control Plane notification API. A provider, worker, or
Control Plane-adjacent process can publish through that API without referencing
the UI app. The same request/update shapes should still apply so the Control
Plane can return a notification reference that the producer can later update or
dismiss.

Notification and toast templates should follow the same boundary. CoreShell can
define the neutral template-selection fields and the renderer registration
contract, but CloudShell should own CloudShell-specific templates such as
resource operation progress, approval requests, provider diagnostics, or
deployment summaries. Templates should not replace the standard notification
fields. They interpret additional item data in a specific way while the shell
still understands the common title, message, severity, status, target, actions,
acknowledgement, dismissal, visibility, and lifetime fields. A host can decide
whether unknown template keys fall back to the default renderer, are hidden, or
are shown with a reduced generic layout.

## Proposed CloudShell boundary

CloudShell should integrate with the CoreShell contracts by adding a Control
Plane-owned notification subsystem:

- a Control Plane notification projection service that subscribes to
  resource-change notifications, resource events, operation/procedure records,
  provider diagnostics, and other Control Plane facts
- a Control Plane-owned notification store for notification events and
  per-recipient instances, in-memory for local development and database-backed
  when persistence or multiple UI hosts require it
- Control Plane notification APIs for current-user queries, acknowledgement,
  dismissal, action handling, producer publish/update, and change cursors or
  signals
- configurable notification rules that match Control Plane facts and decide
  whether to create, update, suppress, or route notification events
- fan-out from event-level producer messages to per-user notification
  instances
- user/group/all-users audience resolution using CloudShell identity and
  authorization services
- SignalR-backed change signals for split UI clients, with polling or
  cursor-based recovery for reconnects and missed signals
- Resource Manager links that take a notification to the relevant resource,
  operation, revision, log query, or diagnostics page

In CloudShell, notification records are part of the Control Plane domain. The
UI owns presentation. The Control Plane should store notification-safe fields,
recipient state, actions, links, lifetime/visibility data, and optional
renderer/template hints. The UI adapts those records into Fluent UI toast and
notification-center presentations, including host-specific templates, icons,
layout, action placement, and fallback rendering.

CloudShell notifications should complement, not replace, existing operational
records. A resource create operation might produce:

- a notification saying that creation started
- progress updates or a completion/failure notification
- Control Plane operation/procedure records that own the durable workflow
  state
- resource events, logs, traces, or diagnostics that explain what happened

The notification should point to the durable operational record when one
exists.

## CloudShell integration scenario

The Control Plane should not need to know about notifications as UI objects.
It already owns resource state, accepted commands, operation/procedure records,
resource events, logs, traces, diagnostics, and provider dispatch. Notifications
should live beside those concerns as a Control Plane-owned projection: a
Control Plane notification service subscribes to domain facts and materializes
notification events plus per-user instances. That keeps notification state
close to the operation truth without making providers or resource managers
depend on CoreShell UI components.

A typical resource-create flow should look like this:

1. The user commits a create-resource flow in Resource Manager.
2. Resource Manager sends the create command to the Control Plane with the
   acting principal and requested operation data.
3. The Control Plane validates the command, accepts or rejects it, records the
   durable operation/procedure state when the workflow is asynchronous, and
   emits domain facts such as `operation accepted`, `provider dispatch
   started`, `operation succeeded`, `operation failed`, or `diagnostics
   available`.
4. The Control Plane notification projection consumes those domain facts
   through a deliberate subscription point. That integration point could start
   as an in-process resource event/change observer, then grow into a durable
   Control Plane event feed, operation-status projection, or broker-backed
   adapter if deployment shape requires it.
5. Configured notification rules match the Control Plane event, decide whether
   a user-facing notification should exist, map the event to notification-safe
   title/message/severity/status/link data, and choose the target audience.
6. The Control Plane notification service resolves the audience, creates or updates
   per-user notification instances, and stores only notification-safe text and
   links.
7. The notification change signal tells connected shell clients to refresh
   their current user's notification instances.
8. The shell renders a toast and notification-center item that links back to
   the durable resource, operation, activity, log, or diagnostics view.

This keeps dependencies disciplined: Control Plane notification services can
depend on Control Plane domain facts and expose notification records through a
CoreShell-compatible shape. The UI adapts those records into a presentation,
but resource execution, providers, and durable operation records do not depend
on CoreShell UI services or toast concepts.

The producer of a notification may be outside the UI process. Resource Manager
running in the UI host can publish through a registered
`ICoreShellNotificationProducer`, but the CloudShell implementation should
route that call to the Control Plane notification API. A background worker or
separate app can publish to the same Control Plane notification API over HTTP,
a broker, or another host-owned transport. In those cases, the in-process
CoreShell interface represents the local adapter surface for the Control
Plane-owned capability.

The integration must support both CloudShell hosting shapes:

- **Combined UI and Control Plane host**: the notification integration can
  subscribe to in-process Control Plane facts such as resource-change
  notifications, resource events, operation/procedure records, and provider
  diagnostics. The Control Plane notification store, projection service,
  producer adapter, and UI `ICoreShellNotificationService` can all be
  registered in the same DI container for a simple local-development path. The
  UI adapter can react to in-process notification change events and re-query
  the local Control Plane notification service.
- **Split UI and Control Plane hosts**: the UI process cannot rely on
  in-process Control Plane events or local notification storage. The Control
  Plane must expose notification query, mutation, producer, and change-signal
  APIs. SignalR should be the preferred change-signal transport for CloudShell:
  the Control Plane owns the notification hub and sends lightweight
  invalidation messages that tell UI clients to re-query. The UI host should
  use remote adapters for notification queries, producer calls, and SignalR
  change signals while keeping the same CoreShell UI-facing interfaces.
  Polling or cursor-based reads should remain available for reconnect,
  catch-up, and environments where SignalR is unavailable.

The API surface should therefore separate three concerns inside the Control
Plane boundary: facts that can be observed, notification projection rules that
convert those facts into user-facing items, and notification APIs that expose
query/mutation/change behavior to shell clients. Combined hosts can collapse
those pieces into one process. Split hosts should use the same Control Plane
notification APIs without changing the notification records or CoreShell
presenters.

The change-signal contract should be invalidation-oriented rather than
state-transfer-oriented. A SignalR message can include a user notification
cursor, affected notification IDs, unread count hints, or a broad
refresh-required signal, but the UI should treat it as a prompt to query the
Control Plane notification API. This keeps in-process events, SignalR, and
future transports aligned with the same source-of-truth store.

The first CloudShell adapter can be modest. For a combined development host,
the Control Plane notification subsystem can use an in-memory store while
subscribing to in-process operation events or polling operation status. A
team-hosted or multi-UI deployment can replace that with a durable Control
Plane notification store and a remote change signal without changing CoreShell
presenters.

The Control Plane notification subsystem is also the right place for product
policy:

- which operation types create notifications
- whether a notification is transient, durable, or both
- how operation severities map to notification severities
- whether completion updates replace an in-progress notification or create a
  related item
- which audience receives a resource-owned notification
- which link target is safe and most useful for the current user
- which provider diagnostics are summarized and which are left behind a
  details link

Resource and operation services should expose enough identity and correlation
for the Control Plane notification subsystem to do its job, but they should
not phrase those facts as toasts, notification center rows, read state, or
dismissible UI items.

## Async operation handoff model

Notifications become most valuable when Control Plane requests accept work
that continues after the HTTP request has returned. Resource creation,
artifact validation, deployment application, start/stop actions, replica
changes, and recovery work should not require the UI request to remain open
until every provider and runtime step completes. The Control Plane should have
a deliberate asynchronous operation handoff path for those workflows.

The preferred shape is:

1. The request handler validates authorization and command shape.
2. The Control Plane creates or updates a durable operation record with a
   stable operation ID, requested action, actor, resource correlation, and
   initial status.
3. The request returns an accepted response or domain result that includes the
   operation ID and any immediately available resource reference.
4. A background operation handler processes the accepted command outside the
   request lifetime.
5. The handler emits operation/resource facts as it moves through accepted,
   queued, running, succeeded, failed, canceled, or needs-attention states.
6. Notification rules map those facts to notification events and update the
   correlated per-recipient notification instances.
7. Connected shells receive a change signal and re-query the notification
   source; resource detail, activity, and diagnostics views query the durable
   operation/resource records.

The background operation handler should be the owner of execution progress,
retries, provider dispatch, and failure details. The notification subsystem
should be a projection over that operation state, not the operation state
itself. This keeps user feedback responsive without making notifications a
workflow engine.

Accepted operations should carry enough correlation for notifications to
update one item instead of producing unrelated rows:

- operation ID
- resource ID when one exists, or a planned resource ID for create flows
- operation kind, such as create, update, delete, start, stop, deploy,
  validate artifact, reconcile, or recover
- acting principal or producer identity
- source request/correlation/trace identifiers
- optional deployment, artifact revision, replica group, health, diagnostic,
  or provider dispatch identifiers

The first implementation can start in-process with a queue and background
service. The important boundary is that the public Control Plane contract is
operation-oriented: clients submit work, get an operation reference, and then
observe state through the operation/resource APIs and notification projection.
A later deployment can move handlers into separate worker processes if the
same accepted-operation records and notification facts are preserved.

## Producer backlog

Initial notification producers should focus on workflows where the user has
committed an action and the outcome is not immediately visible. Track producer
cases here so implementation slices can stay narrow and deliberate.

| Priority | Case | Why it matters | Create/update behavior | Status |
| --- | --- | --- | --- | --- |
| 1 | Lifecycle action progress | Start, stop, restart, and pause are already concrete user actions and can become long-running without changing the user workflow. This is the simplest useful case because the resource already exists and the target link is known. Delete should follow once delete has the same accepted-operation shape. | Create one in-progress notification for the acting user when the lifecycle event starts. Update the same notification to succeeded, failed, or needs attention when the correlated lifecycle result arrives. | First slice implemented for resource lifecycle events |
| 2 | Resource create progress | Create flows are high-value because users leave the create form and need confidence that accepted work is still running. The harder part is handling planned resource IDs and failures before a detail page exists. | Create one in-progress notification for the acting user after command acceptance. Update it with a resource target on success, or with an operation/diagnostics target on failure. | Planned |
| 3 | Resource update progress | Edit flows may trigger provider work, deployment apply, identity provisioning, endpoint mapping, or runtime reconciliation after save. | Create or update an in-progress notification for the acting user when save starts async work. Update the correlated notification on apply success, failure, or needs-attention diagnostics. | Planned |
| 4 | Artifact upload, validation, and apply | Upload and validation workflows have obvious progress/failure states and useful diagnostic targets. | Use progress notifications while a package is uploaded, validated, committed, or applied. Update to failed with validation/provider diagnostics, or succeeded with the resulting revision/resource target. | Planned |
| 5 | Deployment or replica reconciliation | Deployment records and replica slot states already expose runtime progress that users may need to know about after a command returns. | Update an existing operation notification when deployment records or replica slot states move from applying/reconciling to active, failed, or needs attention. Create a notification only when no parent operation notification exists. | Planned |
| 6 | Provider dispatch failure | The Control Plane may accept work and then fail before the user can inspect a useful resource page. | Create or update a failure notification for the acting user with safe summary text and an operation/diagnostics target. | Planned |
| 7 | Background health and recovery | These are useful only when actionable; routine health polling would be noisy. | Notify resource owners or operators for repeated liveness failure, recovery exhausted, or manual intervention required. Suppress routine healthy/probing transitions. | Planned |
| 8 | Provider diagnostics available | Provider diagnostics can require user action, but may contain sensitive or verbose details. | Create warning notifications only for user-actionable, notification-safe diagnostics. Link to the owning diagnostics/logs view for details. | Planned |

Update semantics should prefer one correlated notification for one user-visible
operation. A long-running operation can move from in-progress to succeeded,
failed, or needs-attention without creating a new notification for each
progress event. New notifications should be reserved for distinct outcomes,
different audiences, or follow-up work that is not the same operation.

## Notification rules

CloudShell notification creation should be rule-driven. Control Plane events
are the inputs; notification rules are the configuration that turns selected
events into CoreShell notification events and recipient instances.

A rule should be able to describe:

- the event type or event family it matches
- optional filters such as resource type, resource class, provider ID,
  operation kind, operation status, severity, actor, group, or environment
- whether the rule creates a new notification event, updates an existing
  correlated notification event, suppresses notification output, or only emits
  a transient toast
- the audience selection strategy, such as actor, resource owners, group
  members, users with a role, all users, or a host-defined resolver
- title, message, severity, status, source label, and link mapping
- whether the notification is durable, transient, or both
- correlation behavior, such as grouping all progress events for one operation
  under one notification event
- optional throttling, coalescing, or deduplication policy

Rules may start as code or Control Plane configuration. A later slice can
decide whether administrators can edit them through the UI. The important
boundary is that rules belong to the Control Plane notification subsystem, not
to resource execution, provider, or orchestration logic. Resource and operation
services emit domain facts with stable identity, status, actor, resource,
operation, and diagnostic references; notification rules decide which of those
facts should become user-facing shell notifications.

The rule engine should fail closed for sensitive data. If a rule cannot safely
map an event into notification-safe text and links, it should suppress the
notification or produce a generic item that links to an authorized details
view instead of copying provider payloads into the notification body.

An initial rule set could include:

- create an in-progress notification for the acting user when a Resource
  Manager create command is accepted
- update the same notification when the correlated operation succeeds or fails
- create an error notification for the acting user when provider dispatch
  fails before a resource detail page is available
- create a warning notification for resource owners when a background provider
  reconciliation produces a user-actionable diagnostic
- suppress noisy health or progress events that are already represented by an
  existing operation notification

## Audience model

The first contract should support product-neutral audience descriptors such as:

- `User`: intended for one stable user identifier.
- `Group`: intended for one stable group identifier.
- `Role`: intended for users matching a host-defined role.
- `AllUsers`: intended for every user in the current shell environment.
- `Custom`: intended for a host-defined audience kind and value.

CoreShell should treat these as descriptors. It can filter only when the host
gives it a current audience context or resolver. CloudShell should own the
mapping from these descriptors to authenticated principals, groups, roles, and
permissions.

Audience targeting and authorization are related but distinct. A notification
may target `AllUsers`, but CloudShell still must not show links or details that
the current user is not allowed to inspect. Producers should avoid placing
secret values or sensitive provider payloads in notification text because
notifications may be fanned out broadly.

After audience resolution, the store should persist or expose per-recipient
instances when the notification is user-visible. Group and all-users targeting
are producer conveniences, not shared user state. Acknowledgement, dismiss,
archive, read state, and future notification actions belong to the recipient
instance.

## Record shape

A notification event should be small and stable:

- stable event ID
- title and message
- severity: informational, success, warning, error
- status: active, in progress, succeeded, failed, or needs attention
- source label and optional source kind
- created, updated, and optional completed timestamps
- optional audience descriptors
- optional route or href target
- optional correlation ID, operation ID, or resource ID
- optional attributes for host-owned data
- presentation data such as toast eligibility, scheduled visibility,
  time-to-live, auto-dismiss behavior, and optional template key

A per-recipient notification instance should include:

- stable instance ID
- optional event ID when derived from a broader event
- recipient user ID or host-owned recipient key
- title, message, severity, source, and target link copied or projected from
  the event
- read, acknowledged, dismissed, and archived timestamps or flags
- instance-created and instance-updated timestamps
- optional action descriptors and per-user action state
- optional toast behavior, scheduled visibility, time-to-live, and
  auto-dismiss data
- optional renderer/template key and the small structured data needed by that
  renderer

Records should carry enough context for the shell to render a useful item, but
they should not embed large payloads, logs, provider-native operation data, or
secrets.

## Template and renderer model

The default renderer should stay capable: title, message, severity, source,
progress state, target link, actions, dismiss, and acknowledge. Templates are
for cases where the host wants a richer or more compact presentation without
changing the notification storage model.

Examples include:

- operation progress with a spinner, resource name, phase, and target link
- completed operation summary with primary action and secondary diagnostics
- approval or confirmation request with structured action buttons
- provider diagnostic warning with affected resource and recommended action
- deployment or artifact summary with revision, environment, and status

CoreShell should avoid encoding these as CloudShell resource concepts. Instead,
the item can carry a template key such as `operation-progress` or a
host-qualified key such as `cloudshell.resource-operation`, plus small
renderer data in attributes or a future typed payload. The renderer interprets
that data for its known template and decides how to compose the toast or
notification-center item. If the template is unknown, the standard fields
should still be enough to render a useful generic notification. Action
handling, default links, acknowledgement, dismissal, visibility, and lifetime
behavior should remain common shell behavior around the template.

## Async operation feedback

Asynchronous actions should have a predictable UI destination. For example,
after a user commits a create-resource flow:

1. Resource Manager accepts the create command and publishes an in-progress
   notification event targeted to the acting user, group, or environment
   audience.
2. The notification provider resolves the audience and creates per-user
   instances, returning the instance reference or operation-specific
   equivalent to the producer.
3. The shell shows a toast and adds the current user's instance to the
   notification center.
4. The operation producer updates the event, and the provider updates matching
   user instances when the operation succeeds, fails, or needs attention.
5. The notification links to the resource, operation details, activity log, or
   diagnostics view when that target exists.

For progress-only feedback, a producer can publish a toast with
`AutoDismiss = Never`, keep the returned `CoreShellToast.Id`, dismiss that toast
when the operation completes, and then publish a separate success or failure
toast with the default time-to-live. Notification-backed toasts use the same
idea, except dismissing or acknowledging the toast presentation does not have
to delete the notification-center item unless the implementation chooses that
behavior.

This mirrors the Azure-style pattern: the user gets immediate feedback in a
specific shell location while the durable operation record remains available
for investigation.

## Extension model

Extensions should be able to:

- publish notification events through the host-provided producer adapter. In
  CloudShell, that adapter should write to the Control Plane notification API.
- include source identity so the shell can label where a notification came
  from
- target a user, group, role, all users, or a host-defined audience
- provide a route/href into an extension-owned page
- update a logical event while the provider owns per-user instance state
- listen for notification changes through `ICoreShellNotificationService` when
  they host their own focused surfaces

Extensions should not bypass the host's audience and authorization filtering.
The host remains responsible for deciding which notification records are
visible to the current user and which actions or links are enabled.

## Implementation slices

### Slice 1: CoreShell sample proof

- Add CoreShell notification instance, query, acknowledge/dismiss, and
  change-subscription contracts. Initial minimal UI contract is implemented
  through `ICoreShellNotificationService`.
- Added optional notification actions, action handling, and notification-side
  toast behavior hints.
- Added `ICoreShellToastService` for toast-only transient signals that do not
  create notification instances.
- Added `ICoreShellNotificationProducer` as the CoreShell publish/update
  adapter contract. Hosts can back it with an in-process implementation or a
  remote client. CloudShell should back it with the Control Plane notification
  API.
- Added a sample-owned in-memory implementation for the CoreShell Fluent UI
  sample.
- Added a CoreShell Fluent UI sample notification center and toast presenter.
- Added a sample asynchronous action that publishes an in-progress
  notification with an action and loading indicator, then updates it to
  succeeded.
- Added sample toast-only background task behavior that shows a linked toast
  without creating notification-center history.
- Added focused CoreShell tests for the minimal UI contract and default
  registration behavior, including producer-side publish/update references.

### Slice 2: CloudShell adapter

- Added the first Control Plane notification records, in-memory notification
  store, notification manager surface, resource-event observer hook, and
  default resource-event projection into per-recipient notification instances.
- Added Control Plane notification query/mutation APIs and producer APIs for
  listing, creating, acknowledging, dismissing, and invoking custom
  notification actions on notification instances.
- Added the first Control Plane notification change signal for in-process
  hosts.
- Registered the first CloudShell UI adapter so CoreShell UI presenters read
  Control Plane-owned notification instances through
  `ICoreShellNotificationService`.
- Register producer adapters so Resource Manager, workers, and split-host
  clients publish/update notifications through the Control Plane notification
  API.
- Add resource-operation notification producers for the first high-value
  workflows, such as create, upload/apply artifact, start, stop, and failed
  provider dispatch.
- Fan out group/all-users notifications into per-user instances when the
  CloudShell host has an authenticated user set or can resolve recipients.
- Link notifications to the relevant resource or operation details.
- Keep the initial storage choice explicit: in-memory for development,
  database-backed only when persistence or split/multi-UI hosting requires it.

### Slice 3: Remote and multi-user behavior

- Add Control Plane notification APIs and a change-signal adapter for split
  hosts or multiple connected clients, using SignalR as the preferred
  CloudShell transport.
- Add user/group/all-users audience resolution against CloudShell identity.
- Add database persistence if notifications must survive restarts or be shared
  across UI host instances.
- Add query cursors or checkpoints so clients can recover from missed signals.
- Define how late-joining users receive group or all-users notifications that
  were published before their first connection.
- Decide how the Control Plane notification subsystem should map Control Plane
  facts to notification instances, notification-backed toasts, or toast-only
  signals.

### Slice 4: Preference and retention policy

- Add user-level notification preferences only after producer categories and
  audience semantics are stable.
- Add retention policy for durable notifications.
- Add archive/delete behavior and unread counters if the product needs them.

## Open questions

- Should CoreShell expose one combined notification service, or split record
  query, mutation, and change subscription into separate interfaces from the
  start?
- Should toasts be represented as a presentation hint on notifications, or as
  a separate transient event stream that can optionally reference a durable
  notification?
- Which audience descriptors are needed in the first implementation without
  importing CloudShell identity concepts into CoreShell?
- Should operation progress be modeled as updates to one notification, a
  sequence of related notifications, or a notification linked to a separate
  operation/procedure record?
- How should simple single-user hosts derive or represent the implicit
  recipient key for notification instances?
- When a notification is published to a group or all users, should fan-out
  occur eagerly at publish time or lazily when each user queries?
- What minimum cursor/checkpoint shape is needed so remote clients can recover
  from missed change signals?
- Which Control Plane storage and retention shape is needed for notification
  events and per-recipient instances in split-host deployments?
