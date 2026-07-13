# Notifications and toasts

CloudShell uses CoreShell notification and toast contracts for user-visible
operation feedback. CoreShell owns the product-neutral UI-facing contracts and
presentation rules. CloudShell owns the resource-operation facts, notification
production policy, audience resolution, authorization, and Control Plane
storage.

Notifications and toasts are related but not identical:

- A notification is queryable per-user state. In CloudShell, resource and
  operation notifications should come from the Control Plane notification
  subsystem because the Control Plane owns resource inventory, lifecycle,
  procedures, diagnostics, authorization, and resource events.
- A toast is transient presentation. A notification can request toast
  presentation, and a toast-only signal can exist for UI-local feedback that
  should not create notification-center history.
- A plain toast uses the normal time-to-live. An in-progress toast stays
  visible while the work is running. When a pinned in-progress toast is updated
  to a terminal state without an explicit auto-dismiss override, the in-memory
  CoreShell toast service returns it to the normal toast lifetime.
- Publishing a notification or toast returns the created item. Producers should
  keep the returned ID when they need to update, dismiss, or replace the
  visible feedback later.

## Ownership

| Concern | Owner | Notes |
| --- | --- | --- |
| Core contracts | CoreShell | `ICoreShellNotificationService`, `ICoreShellNotificationProducer`, `ICoreShellToastService`, notification/toast records, actions, targets, status, severity, visibility, and lifetime data. |
| Notification source | Host | CloudShell uses the Control Plane notification subsystem. Other CoreShell hosts can use in-memory, fetched, remote, or custom sources. |
| Toast-only source | Host/UI | CloudShell registers a scoped in-memory CoreShell toast service for UI-local transient feedback. Hosts can replace it. |
| Resource-operation facts | Control Plane | Resource lifecycle, create/update, artifact, deployment, health, recovery, and diagnostics facts should be emitted as domain facts, not UI concepts. |
| Notification rules | Control Plane | Rules decide which facts create, update, suppress, or fan out notifications. Rules also decide audience, correlation, severity, status, target, and safe text. |
| Presentation | UI host | CloudShell chooses Fluent UI rows, cards, templates, icons, action placement, unread counts, and toast stack limits. |

## Scenario matrix

| Scenario | Feedback type | Why | Current status |
| --- | --- | --- | --- |
| Resource Manager settings saved | Toast-only | UI-local preference confirmation. It does not need notification history. | Implemented with stable toast ID. |
| Resource template exported | Toast-only | Local export/copy-style confirmation. It does not mutate the resource graph. | Implemented with stable toast ID. |
| Resource template apply | Notification-backed | Mutates Control Plane resource state and may create, update, or fail resources. | First Control Plane producer implemented. |
| Resource create | Notification-backed | User commits a domain operation and may leave the create UI before provider work completes. | First Control Plane event projection implemented. |
| Resource lifecycle action | Notification-backed | Start, stop, pause, and restart can be long-running and need one correlated status item. | First Control Plane event projection implemented. |
| Resource image or replica update | Notification-backed | Resource update may dispatch provider work and should update one correlated operation item. | First image and replica update projection implemented. |
| Artifact upload, validation, and apply | Notification-backed | Upload/validate/apply has progress, failure diagnostics, revision targets, and resource targets. | Local upload/validation progress toasts implemented; artifact apply uses a distinct Control Plane notification shape. |
| Deployment or replica reconciliation | Notification-backed when user-visible | Reconciliation should update an existing operation when possible and avoid noisy low-level progress. | Start-driven materialization, generic deployment apply, and replica repair slices implemented; broader projection planned. |
| Background health and recovery | Notification-backed only when actionable | Routine health polling is noise. Threshold-crossing failures, recovery attempts, exhausted recovery, and manual intervention should notify. | First health/recovery projection implemented. |
| Provider diagnostics available | Notification-backed only when safe and actionable | Diagnostics can be sensitive or verbose. Notifications should link to authorized details and carry only safe summary text. | Planned. |
| Local copy, export, view preference, or panel state | Toast-only or inline | Short-lived UI feedback, not Control Plane state. | Use `ICoreShellToastService` only when inline feedback is not enough. |

## Producer rules

CloudShell producers should follow these rules:

- Use notification-backed feedback for user-visible resource operations,
  provider work, diagnostics, and health/recovery facts owned by the Control
  Plane.
- Use toast-only feedback for UI-local facts that are useful immediately but do
  not belong in notification history.
- Prefer one correlated notification for one user-visible operation. Update it
  from in-progress to succeeded, failed, or needs-attention instead of creating
  a new row for every progress fact.
- Suppress routine or duplicate progress that is already represented by a
  higher-level operation notification.
- Keep notification text safe for the selected audience. Do not copy secret
  values or provider-owned sensitive payloads into notification titles,
  messages, actions, attributes, or targets.
- Prefer links to resource, operation, activity, diagnostics, or artifact
  revision views instead of embedding verbose details in the notification.
- Use stable toast IDs for repeated UI-local confirmations so the in-memory
  toast service replaces the current card instead of stacking duplicates.

## Progress patterns

For notification-backed resource operations:

1. The Control Plane emits a domain fact that the operation was accepted or
   started.
2. The notification projection rule creates one in-progress notification for
   the acting user or resolved audience.
3. The shell shows a toast and adds the instance to the notification center.
4. Later domain facts update the same correlated notification to succeeded,
   failed, or needs-attention.
5. The notification links to the resource, operation, activity, diagnostics, or
   artifact revision view when one exists.

For toast-only progress:

1. Publish a toast with `Status = InProgress` and
   `AutoDismiss = Never`.
2. Keep the returned `CoreShellToast.Id`.
3. Update that toast to a terminal status or dismiss it and publish a separate
   completion toast.
4. If the pinned toast is updated to a terminal status without an explicit
   auto-dismiss override, the reusable in-memory CoreShell toast service
   returns it to normal time-to-live auto-dismiss.

## CloudShell template keys

CloudShell-owned notification producers use stable template keys so shell
hosts can opt into richer renderers while keeping the default CoreShell
notification layout as the fallback.

| Constant | Template key | Producer |
| --- | --- | --- |
| `CloudShellNotificationTemplateKeys.ResourceLifecycleOperation` | `cloudshell.resource-lifecycle-operation` | Resource start, stop, pause, and restart events. |
| `CloudShellNotificationTemplateKeys.ResourceCreateOperation` | `cloudshell.resource-create-operation` | Resource creation events. |
| `CloudShellNotificationTemplateKeys.ResourceUpdateOperation` | `cloudshell.resource-update-operation` | Resource image, replica, and update events. |
| `CloudShellNotificationTemplateKeys.DeploymentApplyOperation` | `cloudshell.deployment-apply-operation` | Deployment apply events. |
| `CloudShellNotificationTemplateKeys.ResourceRecoveryOperation` | `cloudshell.resource-recovery-operation` | Resource recovery events. |
| `CloudShellNotificationTemplateKeys.ReplicaRepairOperation` | `cloudshell.replica-repair-operation` | Replica repair events. |
| `CloudShellNotificationTemplateKeys.ResourceTemplateApplyOperation` | `cloudshell.resource-template-apply-operation` | Resource template apply operations. |
| `CloudShellNotificationTemplateKeys.ApplicationArtifactApplyOperation` | `cloudshell.application-artifact-apply-operation` | Resource Manager application artifact apply operations. |

Template data should use `CloudShellNotificationAttributeNames` for shared
attribute names such as `operationKind`, `resourceId`, `eventType`,
`templateName`, `applyMode`, `traceId`, and `spanId`. Use
`CloudShellNotificationOperationKinds` for shared operation-kind values such as
`lifecycle`, `create`, `update`, `deployment`, `recovery`, `replicaRepair`,
`templateApply`, and `applicationArtifactApply`.

## Current gaps

- Notification records are in-memory in the local-development path.
- Split-hosting notification change delivery still needs a SignalR-backed
  adapter.
- Audience resolution is still local/default-operator oriented.
- Durable artifact upload and validation notification producers are deferred;
  local upload and validation progress currently uses toast-only feedback.
- Provider diagnostics notifications need safe summary and authorization rules.
- Rich CloudShell-specific notification templates are not implemented yet.
