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
- Remaining action: add CloudShell notification rules, operation producers,
  audience resolution, and durable or remote notification adapters.
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
- Let CloudShell publish resource-operation notifications from Resource
  Manager, Control Plane operations, providers, or extension-owned services
  while keeping operation history and diagnostics in their owning systems.
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
notification instance or event update. A notification center is a longer-lived
view over queryable per-recipient instances. The same notification may appear
in both places, only one place, or neither place if the host filters it out.

## Proposed CoreShell boundary

CoreShell should own stable shell-level contracts such as:

- notification event and per-recipient notification instance shapes
- a notification query contract
- publish/update contracts for hosts that allow shell-local producers
- acknowledge/dismiss contracts for per-recipient instances
- a change subscription contract
- audience descriptors that stay product-neutral
- presentation hints such as severity, status, route/href, source label, and
  toast eligibility

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

## Proposed CloudShell boundary

CloudShell can later integrate with the CoreShell contracts by providing:

- a database-backed notification store when notifications need to survive host
  restarts or be shared across UI instances
- a dedicated notifications service, or a UI-host adapter for simple
  deployments, that publishes notifications for resource creates, updates,
  starts, stops, artifact uploads, deployment attempts, provider failures, and
  other asynchronous workflows
- configurable notification rules that match Control Plane events and decide
  whether to create, update, suppress, or route notification events
- fan-out from event-level producer messages to per-user notification
  instances
- user/group/all-users audience resolution using CloudShell identity and
  authorization services
- a SignalR or polling change-signal adapter for connected UI clients
- Resource Manager links that take a notification to the relevant resource,
  operation, revision, log query, or diagnostics page

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
It owns resource state, accepted commands, operation/procedure records,
resource events, logs, traces, diagnostics, and provider dispatch. A separate
CloudShell notifications service can observe those domain facts and project
them into CoreShell notification events and per-user instances.

A typical resource-create flow should look like this:

1. The user commits a create-resource flow in Resource Manager.
2. Resource Manager sends the create command to the Control Plane with the
   acting principal and requested operation metadata.
3. The Control Plane validates the command, accepts or rejects it, records the
   durable operation/procedure state when the workflow is asynchronous, and
   emits domain facts such as `operation accepted`, `provider dispatch
   started`, `operation succeeded`, `operation failed`, or `diagnostics
   available`.
4. The notifications service consumes those domain facts through a deliberate
   integration point. That integration point could be an in-process domain
   event sink in combined hosts, a Control Plane event feed, operation-status
   polling, a message broker, or a future environment service API.
5. Configured notification rules match the Control Plane event, decide whether
   a user-facing notification should exist, map the event to notification-safe
   title/message/severity/status/link data, and choose the target audience.
6. The notifications service resolves the audience, creates or updates
   per-user notification instances, and stores only notification-safe text and
   links.
7. The notification change signal tells connected shell clients to refresh
   their current user's notification instances.
8. The shell renders a toast and notification-center item that links back to
   the durable resource, operation, activity, log, or diagnostics view.

This keeps dependencies one-way: CloudShell UI and notification integrations
can depend on Control Plane domain facts, but the Control Plane does not depend
on CoreShell UI services or toast concepts.

The first CloudShell adapter can be modest. For a combined development host,
it may run in the UI host process and use an in-memory notification store while
subscribing to in-process operation events or polling operation status. A
team-hosted or multi-UI deployment can replace that with a durable notification
store and a remote change signal without changing CoreShell presenters.

The notification service is also the right place for product policy:

- which operation types create notifications
- whether a notification is transient, durable, or both
- how operation severities map to notification severities
- whether completion updates replace an in-progress notification or create a
  related item
- which audience receives a resource-owned notification
- which link target is safe and most useful for the current user
- which provider diagnostics are summarized and which are left behind a
  details link

The Control Plane should expose enough operation identity and correlation for
that service to do its job, but it should not phrase those facts as toasts,
notification center rows, read state, or dismissible UI items.

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

Rules may start as code or host configuration. A later slice can decide whether
administrators can edit them through the UI. The important boundary is that
rules belong to the notification integration layer, not to the Control Plane
domain model. The Control Plane emits domain events with stable identity,
status, actor, resource, operation, and diagnostic references; notification
rules decide which of those events should become user-facing shell
notifications.

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
- optional attributes for host-owned metadata
- presentation hints such as toast eligibility or expiration

A per-recipient notification instance should include:

- stable instance ID
- optional event ID when derived from a broader event
- recipient user ID or host-owned recipient key
- title, message, severity, source, and target link copied or projected from
  the event
- read, acknowledged, dismissed, and archived timestamps or flags
- instance-created and instance-updated timestamps
- optional action descriptors and per-user action state

Records should carry enough context for the shell to render a useful item, but
they should not embed large payloads, logs, provider-native operation data, or
secrets.

## Async operation feedback

Asynchronous actions should have a predictable UI destination. For example,
after a user commits a create-resource flow:

1. Resource Manager accepts the create command and publishes an in-progress
   notification event targeted to the acting user, group, or environment
   audience.
2. The notification provider resolves the audience and creates per-user
   instances.
3. The shell shows a toast and adds the current user's instance to the
   notification center.
4. The operation producer updates the event, and the provider updates matching
   user instances when the operation succeeds, fails, or needs attention.
5. The notification links to the resource, operation details, activity log, or
   diagnostics view when that target exists.

This mirrors the Azure-style pattern: the user gets immediate feedback in a
specific shell location while the durable operation record remains available
for investigation.

## Extension model

Extensions should be able to:

- publish notification events through the host-provided notification
  integration service when the host exposes one
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
- Decide whether event and publish/update producer contracts belong in
  CoreShell or only in host-specific notification services.
- Added a sample-owned in-memory implementation for the CoreShell Fluent UI
  sample.
- Added a CoreShell Fluent UI sample notification center and toast presenter.
- Added a sample asynchronous action that publishes an in-progress
  notification and then updates it to succeeded.
- Added focused CoreShell tests for the minimal UI contract and default
  registration behavior. Producer-side record publication and update tests
  should land with the durable producer contract.

### Slice 2: CloudShell adapter

- Register a CloudShell notification integration that can publish Resource
  Manager and Control Plane operation notifications while CoreShell UI
  presenters read the resulting instances through `ICoreShellNotificationService`.
- Add resource-operation notification producers for the first high-value
  workflows, such as create, upload/apply artifact, start, stop, and failed
  provider dispatch.
- Fan out group/all-users notifications into per-user instances when the
  CloudShell host has an authenticated user set or can resolve recipients.
- Link notifications to the relevant resource or operation details.
- Keep the initial storage choice explicit: in-memory for development,
  database-backed only when persistence is required by the workflow.

### Slice 3: Remote and multi-user behavior

- Add a change-signal adapter for split hosts or multiple connected clients.
- Add user/group/all-users audience resolution against CloudShell identity.
- Add database persistence if notifications must survive restarts or be shared
  across UI host instances.
- Add query cursors or checkpoints so clients can recover from missed signals.
- Define how late-joining users receive group or all-users notifications that
  were published before their first connection.

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
- Where should notification persistence live in split-host CloudShell
  deployments: UI host, Control Plane, or an environment-level service?
