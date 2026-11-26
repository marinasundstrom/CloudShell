# Resource operations workspace

Resources is the CloudShell operations workspace for developers and operators.
It starts with a full-width, compact resource inventory. Selecting a resource
reduces the inventory to a resizable left pane and opens that resource's
customizable dashboard on the right. Dashboard, Logs, Traces, and applicable
standard/provider panes change the adjacent content without removing the list.
Expand returns to the full inventory. All resources selects system scope.
Resource graph in the inventory footer, or Open full graph in the Relationships
widget, opens the existing interactive graph at `/resources/graph`.
Compact rows reveal the primary start/stop command and an overflow action menu
on hover or keyboard focus; touch layouts expose those controls directly.
Capabilities are fetched in a batch, not once per rendered row.

The ordinary shell navigation remains intact. Observability, Logs, Traces,
Settings, and other product areas still open their standalone pages. The
workspace reuses their content components; it does not wrap every shell page.

## Layout and navigation

Compact density is the default; Comfortable is a persisted user preference.
Inventory search filters the list without changing the selected resource or
signal scope. The list and content scroll independently. On narrow screens,
Resources toggles the inventory drawer; the selected scope remains labeled.

The default root landing opens the system dashboard unless a host has configured
another start route. The Resources menu opens the full inventory. The previous inventory preview and home overview are
replaced by this experience.

| Target | Route |
| --- | --- |
| Full inventory | `/resources` |
| System dashboard | `/resources?operation=dashboard` |
| Resource dashboard | `/resources/{escaped-resource-id}` |
| Resource logs/traces | `/resources/{escaped-resource-id}/logs` and `/resources/{escaped-resource-id}/traces` |
| System logs/traces inside Resources | `/resources?operation=logs` and `/resources?operation=traces` |
| Resource configuration | `/resources/{escaped-resource-id}/configuration` when supported; `/overview` provides detailed generic information otherwise |
| Contributed operational pane | Resource view route, or a composed pane selected by `pane=<section-id>` on the dashboard route |

Stable resource IDs identify selections. Display names are labels, never
addresses. Resource-scoped log and trace views retain their existing source and
correlation query parameters. Switching resources retains the active standard
investigation pane and compatible time query parameters, resetting source and
trace-specific filters. Browser navigation reconstructs scope from the URL.

Configuration retains provider forms, Apply/Cancel behavior, and its grouped
navigation. Standard operational tabs and explicitly classified custom tabs
appear in workspace navigation instead of configuration navigation. Editing
form fields with an apply handler prompts before leaving with unsaved changes.

## Dashboards

There is one system dashboard and one dashboard for each resource. The overview
provides information at a glance. Specialized panes retain larger tables,
editors, diagnostic tools, and detailed investigation.

Generic defaults include lifecycle status, attention summaries, endpoints,
resource relationships, and recent activity. Resource log/trace previews are
included by default when their projected signal metadata supports them.
Users can still add available signal widgets explicitly. Previews are bounded
snapshots, not streaming log readers. Full Logs supports its existing live
sessions.

RabbitMQ contributes a default type-specific widget showing ready and
unacknowledged messages, consumers, message-rate diagrams, and queue-depth
bars. Broker status and Topology remain separate operational panes. The broker
widget uses the existing `IRabbitMQBrokerDashboardProvider`; split UI hosts
need a working provider adapter, just as the detailed RabbitMQ pane does. An
unconfigured provider displays unavailable data rather than fabricated values.

The generic relationship widget shows declared dependencies, parentage, and
ownership, filtered to visible authorized resources. It refreshes lifecycle
state with inventory, preserves positions on status-only updates, and uses
clickable nodes to select resources. It is bounded to 24 nodes; selecting a
resource narrows it to its neighborhood. It is not a traffic/service map.

## Customization precedence and storage

Effective layout precedence is:

1. User's saved layout for this resource (or the system dashboard).
2. Organization's saved layout for this resource (or the system dashboard).
3. Resource-type/host defaults, falling back to applicable extension defaults.

Edit dashboard supports Add, Remove, Move earlier/later, Restore defaults,
Save, and Cancel. Save stores a personal layout. Save for organization requires
`shell.configure`, enforced by the shared settings service as well as the UI.
Personal overrides remain intact when organization defaults change. Use
organization defaults removes the personal override. Existing open sessions
read new shared defaults when the dashboard is next loaded.

`OperationsDashboardOptions`, bound from `Shell:Dashboards`, optionally defines
`ResourceTypes` (a dictionary of type IDs to ordered section-ID arrays) and
`System` (an ordered section-ID array). Without explicit host defaults, sections
marked `operations.default=true` form the defaults for each applicable type.

Personal layouts use `ICloudShellUserSettingsProvider` and its configured
local or remote backing store. `IOrganizationDashboardSettings` is a replaceable
UI-host service; the built-in implementation writes
`Data/organization-dashboards.json` under the UI content root, with permission
checks, serialized access, and atomic replacement. Organization means users of
that UI deployment, not a new tenant/domain model. Multiple UI replicas need a
shared implementation of this service; the built-in file store is for one
UI-host process. No new Control Plane API or resource payload is introduced.

Keys include the UI base URI and system/resource identity; resource-instance
layouts do not transfer by display name. Layouts are versioned lists of stable
widget IDs. Empty layouts are valid, and missing/disabled contributions retain
an unavailable placeholder so the saved ordering can recover. Invalid data
falls back to defaults with a message; save failures retain the draft. Saved
layouts contain no telemetry, executable code, or credentials.

## Extension contract (preview)

`OperationsWorkspaceIds.Page`, `.Widgets`, and `.Panes` expose normal CoreShell
section outlets. There is no separate widget registry. Contributions use
`AddCoreShellModule`, `ExtendSections`, and `AddSection<TComponent>`; normal
CoreShell authorization requirements apply.

```csharp
module.ExtendSections(OperationsWorkspaceIds.Page, OperationsWorkspaceIds.Widgets)
    .AddSection<MySummary>(
        CoreShellSectionId.Create(OperationsWorkspaceIds.Widgets, "my-summary"),
        "My summary", 25,
        new Dictionary<string, string>
        {
            [OperationsWorkspaceIds.Scope] = "resource",
            [OperationsWorkspaceIds.ResourceType] = "my.resource-type",
            [OperationsWorkspaceIds.DefaultWidget] = "true"
        });
```

Use `OperationsWorkspaceIds.Panes` for a detailed custom pane. Available metadata:

| Attribute | Meaning |
| --- | --- |
| `operations.scope` | `system`, `resource`, or `both` (default) |
| `operations.resource-type` | Optional exact resource type filter |
| `operations.default` | Include the widget in applicable default layouts |
| `operations.wide` | Request the full dashboard grid width |
| `operations.signal` | Built-in default gating for `logs` or `traces` |

Components receive the public cascading `OperationsWorkspaceContext`: selected
resource, visible inventory, health summaries, scope ID, freshness/loading
state, label function, and dashboard navigation helper. Treat it as a UI
projection, not a data authorization boundary. Use public domain managers for
data/actions; provider runtime and internal stores remain behind their existing
contracts. Use cancellation and bounded queries, and discard stale results on
scope changes. Widget/pane error boundaries isolate rendering failures.

Existing Resource Manager UI integrations can pass `isOperational: true` to
`AddResourceTab<TComponent>`. This is represented by
`ResourceTabContribution.IsOperational`. Standard Logs, Traces, Metrics,
Monitoring, Health, Activity, and Usage are operational automatically.
Configuration, environment, identity, and resource editing remain configuration
views. The `dashboard` public route segment is reserved for the customizable
overview; custom detail panels need their own identifier. RabbitMQ's old
`dashboard` detail route is now `broker-status`.

The resource-host sample contributes both a type-specific default widget and
an operational pane through these public outlets. Built-in RabbitMQ uses the
same widget path. CoreShell stays resource-neutral: the context, scope metadata,
and operational classification belong to CloudShell's Resource Manager UI.

## Parity and limits

- Combined/split hosts use the same inventory, log, trace, event, and health
  managers. Providers/SDKs/launchers supply normal resource projections; no
  dashboard metadata enters `ResourceDefinition` or exported templates.
- C#, TypeScript, Java, YAML, and other authoring paths receive equivalent
  dashboards from resource type/capability projection.
- Action buttons honor read-only mode and manager action capabilities; the
  Control Plane validates again on execution. Dependency conflicts retain
  their domain behavior.
- Inventory polling is every five seconds. Signal previews are bounded and
  refreshed from the workspace, with cancellation on scope changes/disposal.
  Large-graph virtualization and retained diagram zoom/pan are future work.
- Graph width is responsive and contributed widgets may request full width.
  Dashboard cards use shared rounded surfaces with shadows and faint borders
  in light mode, contrasting colors in dark mode, and no header dividers. Edit mode supports pointer drag handles
  (including touch) over a visible grid, with highlighted before/after drop
  targets. Drops reorder the draft; Save persists and Cancel restores the
  saved order. Keyboard users retain the Move earlier/later buttons.
  Widget widths still come from contributions; freeform positioning, resizing,
  and user-specific widget size editing are not included.
- Multiple named dashboards, organization tenants, shared-layout revision
  history, and cross-UI-host organization storage are not built in.
- Existing specialized provider adapters retain their existing split-host
  limitations; the widget layer does not introduce a new provider data API.
