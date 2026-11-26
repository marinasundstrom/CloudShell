# Operations workspace follow-up

## Status

- Status: Implemented core workspace; remaining enhancements below.
- Strategy fit: High; gives developers and operators relevant information at a glance.
- Canonical feature docs: [Resource operations workspace](../../operations-workspace.md).
- Remaining action: Validate the operator experience with real application graphs and provider diagnostics.
- Out of scope: Marketplace, multi-Control Plane aggregation, and arbitrary user-supplied widget code.

## Accepted interaction

Resources opens a full-width compact list. Selecting a resource narrows the
list and opens its dashboard on the right. Operational pane links change that
content without losing the inventory. Ordinary menu areas, including standalone
Observability, continue opening their existing pages. Configuration forms remain
intact, while standard and custom operational views live in workspace panes.

Each resource type supplies applicable default widgets. An organization can
customize a resource's layout, and users can apply personal overrides. The
system has its own dashboard. Overview widgets summarize; specialized panels
retain detailed diagnostics. RabbitMQ demonstrates broker diagrams in the
overview with separate broker-status/topology panes.

## Remaining enhancements

- Larger inventories: virtualization, richer grouping/filtering, and query cost
  tuning for remote Control Planes.
- Relationship diagrams: retained zoom/pan and larger-neighborhood exploration.
- Layout controls: configurable widget sizes and optional drag rearrangement,
  retaining keyboard equivalents.
- Shared settings: a multi-instance organization storage adapter and conflict
  handling beyond the initial single UI-host file store.
- Provider coverage: additional type-specific widgets and diagnostic panes,
  including completion of existing provider-specific split-host data adapters.
- Layout lifecycle: explicit cleanup policy for preferences of deleted or
  ephemeral resources.

The source screenshots and sketches informed the interaction, not an exact
Aspire clone or URL scheme. Current contracts and implementation details live
in the feature document rather than this proposal.
