# Resource Manager UI Extensions

Resource Manager UI extensions are resource-specific UI contributions built on
the base [UI extension architecture](ui.md). They run in the CloudShell UI app
and should use domain managers such as `IResourceManager`, not internal
Control Plane stores or provider implementations.

Use this surface for Add Resource forms, update components, generated details
customization, resource tabs, detail routes, and resource UI actions. Pair it
with a [Control Plane resource provider](control-plane-resource-providers.md)
when the resource type needs provider-backed behavior.

A Control Plane resource provider may be paired with a Resource UI provider.
The Resource UI provider owns the interactive presentation for that resource
type and can compose with predefined Resource Manager views in two ways:

1. Add provider-owned sections to a predefined tab such as Endpoints or DNS.
2. Add a new tab or replace a predefined tab entirely with provider-owned
   content.

## Resource Types

Resource types are the user-facing contract for adding resources. An extension
registers each type with `AddResourceType<TRegistrationComponent>`.

Resource type and UI registration are separate from Control Plane resource
provider registration. CloudShell UI and the Control Plane are distinct apps,
even when a development host runs both inside one ASP.NET Core process. A
Control Plane provider package can register services that project and operate
resources, while the UI extension registers the Resource Manager experience for
those resources.

The registration component is rendered inside the common Add Resource page
after the user chooses the type from a dropdown. It owns the type-specific
form, validation, discovery hints, and the call to
`IResourceRegistrationStore.RegisterAsync`.

Resource providers are not shown as a product concept in the UI. They are
implementation services that resource types use to map external systems into
CloudShell resources.

## Create Experiences

CloudShell should support more than one Resource Manager create experience
over the same underlying resource-creation contract.

The intended model is:

- **Resource gallery** as the default `/resources/add` entry point. It should
  help users discover resource types through search, categories, and
  provider-owned descriptions.
- **Quick create** as the direct
  `/resources/add?type=<resource-type-id>` shortcut. This is the compact,
  single-page form path for experienced users, bookmarks, deep links, and
  app-centric or provider-centric "add related resource" actions. Contextual
  quick-create links should pass a local `returnUrl` so Cancel and successful
  registration return the user to the resource page and tab that initiated the
  create flow.
- **Inline create** as a contextual modal or drawer from a resource page when
  the user is creating a closely related resource, such as a DNS name mapping,
  route, volume, or permission grant. Inline create should prefill the current
  resource context and stay limited to focused adjacent resources; users
  should still be able to reach the full Add Resource flow when they need the
  complete registration surface.
- **Guided create** as a future wizard-based path for resource types that need
  prerequisites, multi-step configuration, provider selection, or richer
  validation. This should remain a presentation choice over the same create
  command model rather than a separate resource-registration mechanism.

For MVP, the priority is keeping quick-create flows understandable and stable.
Inline create, gallery, and wizard paths should be added without changing the
underlying resource-type contribution model or the registration components'
ownership of type-specific inputs and validation.

## Health Check Defaults

Resource types can provide default health checks through
`ResourceTypeProbeOptions`. This gives the Add Resource UI the same
health-check enablement path available to programmatic declarations such as
`WithHttpHealthCheck(...)`, while keeping enablement on the resource instance
explicit.

```csharp
builder.AddResourceType<Pages.RegisterAcmeService>(
    "acme.service",
    "Acme service",
    "Register an Acme service endpoint.",
    "server",
    20,
    probeOptions: new ResourceTypeProbeOptions(
    [
        new ResourceHealthCheck(
            "/healthz",
            EndpointName: "http",
            Name: "health",
            Source: ResourceProbeSource.ForHttp("/healthz", "http"))
    ]));
```

When a selected resource type has default health checks, the common Add
Resource page shows an "Enable resource health checks" checkbox. The checkbox
only controls the resource being created. If it is cleared, no health checks
are written to that resource even though the type supports them.

Registration components can consume the selected instance settings through the
cascading `ResourceRegistrationProbeContext` and copy them into their
provider-specific definition:

```csharp
[CascadingParameter]
public ResourceRegistrationProbeContext? ProbeContext { get; set; }

var definition = new AcmeServiceDefinition(
    id,
    name,
    healthChecks: ProbeContext?.GetSelectedHealthChecks());
```

Default checks are expectations, not proof that every resource instance
implements the endpoint. ASP.NET Core projects are a good example: a type may
provide `/healthz` or `/alive` defaults, but the app must map those endpoints.
If it does not, the Resources UI will correctly show an unhealthy or unknown
result unless the user disables the checks for that resource.

Resource-scoped Health views are available when the resource type supports
health checks or when the resource already declares checks. Current and
historical views use retained `ResourceHealthSummary` snapshots keyed by
resource ID, so the resource page and common Health page can correlate the same
polling history.

Resource-scoped Health should remain focused on the selected resource. The
common Health page may later use the same retained snapshots to render
status-page-style timelines for explicit health scopes such as systems,
services, capabilities, or other operator-defined groups. That aggregate view
requires a separate health-scope model that declares contributing resources and
checks, aggregation rules, and drill-down behavior; it should not treat normal
resource groups as the only system-health boundary.

## Resource Manager Projection

The shell generates a default resource detail view from the projected
`Resource` when a provider does not contribute a specialized view. The
generated view shows stable identity, class, endpoints, attributes,
dependencies, health checks, actions, and observability details. The built-in
route is `/resources/{resourceId}` for the default Overview view, while
provider-contributed tabs and predefined views use
`/resources/{resourceId}/{view}`. Resource Manager derives `{view}` from the
local `ResourceViewId.Identifier` by convention, so
`management:access-control` resolves to `access-control`. The legacy
`/resources/{resourceId}/details?tab=<group>:<view>` route remains supported
for compatibility.

Resources can set `DetailRoute` to link to an extension-owned view. This
supports the familiar cloud-portal pattern where a resource opens its own
operational workspace.

Resource types can also contribute tabs or an update component. Those
provider-owned views override the generated default for resources of that type.
Resource Manager passes `ResourceId` to contributed tab components. Generated
views may receive additional Resource Manager-owned context such as resource
type, log selection, trace selection, or telemetry scope. A provider replacing
a predefined view should load any additional data it owns through domain
managers instead of depending on those internal generated-view parameters.

## Predefined Resource Views

Resource views are the tabs and detail surfaces shown for a resource in
Resource Manager. Providers can contribute resource views for complete
resource-specific workflows or replace selected built-in views when the
generated view is not appropriate.

CoreShell owns the generic shell primitives used to render those tabs, such as
pages, routes, navigation targets, and tabbed layout presenters. Resource
Manager owns the resource-page model layered on top of those primitives:
predefined resource views, resource tab groups, generated resource tabs,
provider-owned resource tabs, tab ordering, visibility, and routing between
resource-detail views. Do not push Resource Manager-specific resource-page
semantics into CoreShell; a different CoreShell-based product area should be
able to use the same layout primitives without inheriting Resource Manager's
resource tab taxonomy.

The Resource Manager tab resolver combines projected resource shape,
capabilities, available operational data, authorization, and provider tab
contributions into the resource's effective view set. Provider contributions
with the same predefined view ID replace the generated fallback. The resolver
also retains which views it generated so the host can pass Resource
Manager-specific context only to generated components that accept it. The
resource page consumes this resolved model; CoreShell receives only the
resulting generic tab layout and route targets.

Resource Manager also owns resource-view selection semantics. Public resource
detail paths use the predefined view's local identifier, while the legacy
`tab=<group>:<view>` query accepts the canonical `ResourceViewId`. Legacy
`/details` and `/edit` paths continue to defer to that query. Missing,
ambiguous, and malformed selections produce a Resource Manager recovery state
that links back to Overview; they are not CoreShell routing concepts.
Resource Manager also retains a narrow route-binding compatibility parser for
requests where an encoded canonical `<group>:<view>` suffix is folded into the
resource ID route value. It splits only canonical view IDs, preserving normal
hierarchical resource IDs and ordinary trailing path names.

Resource Manager also resolves action-control state at its UI boundary. The
same policy combines Control Plane action capability reasons, read-only mode,
resource-management restrictions, and in-flight execution state for resource
list/detail surfaces and inline/overflow commands, so every presentation
remains disabled and explanatory under the same conditions.
Generated Overview content consumes action readiness for diagnostics but does
not dispatch actions itself; the containing resource detail page owns action
controls, confirmation, execution progress, and procedure feedback.

CloudShell also has predefined resource detail views for common concerns such as
Overview, Configuration, Endpoints, DNS, Environment, Storage, Identity, and
Activity. These views are identified by the constants in
`ResourcePredefinedViewIds`.

Predefined view IDs are logical hierarchical IDs represented by
`ResourceViewId`:

- `GroupId` identifies the concern group, such as `general` or `networking`.
- `Identifier` identifies the view inside that group, such as `overview` or
  `endpoints`.
- `Value` is the canonical serialized form used in compatibility query
  strings and internal references, for example `general:overview` or
  `networking:endpoints`. The public path segment is conventionally the
  `Identifier` within the current resource page.

Because the public path omits `GroupId`, each contributed `Identifier` must be
unique, case-insensitively, within a resource type. Predefined view identifiers
are reserved. A provider that owns a replacement for a predefined view must use
the exact `ResourcePredefinedViewIds` value instead of declaring the same
identifier in another group. The extension builder rejects duplicate or
reserved route segments during registration so ambiguity cannot reach runtime
view selection.

Use `ResourceTabGroupIds`, `ResourceTabGroupTitles`, and
`ResourcePredefinedViewIds` instead of creating raw string literals in
providers or shell UI code.

The default Resource Manager tab groups are:

| ID | Title | Purpose | Typical views |
| --- | --- | --- | --- |
| `general` | General | Stable summary and basic resource configuration that most resources share. | Overview, Configuration |
| `application` | Application | Workload-specific configuration for application resources. | Deployment, Scale and replicas |
| `networking` | Networking | Connectivity, reachability, endpoint exposure, DNS, names, and routing. | Endpoints, DNS |
| `storage` | Storage | Volumes, mounts, storage ownership, and storage-provider details. | Storage, Volumes |
| `environment` | Environment | Resource-provided environment, settings, or value-collection views when they are the resource's primary domain. | Provider-specific environment views |
| `runtime` | Runtime | Provider/runtime implementation detail views that are useful for inspection but are not the stable resource contract. | Containers, replicas, provider runtime views |
| `telemetry` | Telemetry | Runtime/application signals emitted by or collected for the resource. | Logs, Traces, Metrics |
| `management` | Management | Control-plane and operational management concerns such as identity, access control, monitoring, activity, and generated environment editing. | Identity, Access control, Monitoring, Environment, Activity |

Most groups keep the relative order contributed by the resource type and
generated views. Telemetry and Management are ordered after the resource-domain
groups so runtime investigation and control-plane operations remain separate
from the resource's core configuration.

Prefer the group that describes the user's task, not the provider that
implements the data. For example, Identity belongs under Management because
identity binding and provisioning are control-plane access management
concerns. Access Control also belongs under Management because assigning and
revoking resource identity grants changes platform access intent. Logs,
traces, and telemetry metrics belong under Telemetry because they are runtime
investigation signals, while provider-owned resource monitoring belongs under
Management.

The generated Access Control tab appears under Management for every resource.
When the current resource has no identity binding, the tab tells the user to
set up an identity in the Identity tab before assigning access permissions.
When the resource has an identity, the tab groups grants by the target resource
being granted access, and lets users search target resources before assigning
or revoking grant intent. CloudShell records the desired grant; applying or
reconciling that grant against external identity systems remains the
responsibility of the selected identity provider.

Use the constants instead of hard-coded string literals when registering tabs,
building links, or contributing sections:

```csharp
ResourcePredefinedViewIds.Endpoints
ResourcePredefinedViewIds.Dns
ResourcePredefinedViewIds.Configuration
ResourceTabGroupTitles.Networking
ResourceTabGroupTitles.Storage
```

Predefined views are capability and shape driven. For example, the Resource
Manager can show Endpoints or DNS for resources that project endpoint data,
networking capabilities, or name-mapping shape even when a provider does not
own a custom tab. This keeps common concepts discoverable across providers.

The intended Resource Manager direction is that predefined views **light up**
from the resource model before providers add custom UI:

- **Projected resource shape** can light up predefined views. A resource with
  endpoints can get the Endpoints view, a resource with related name mappings
  can get DNS, a resource with volume mounts can get Storage, and a resource
  with lifecycle activity can get Activity.
- **Resource capabilities** can light up predefined views even before concrete
  data exists. A resource that declares endpoint-source, DNS-zone,
  volume-consumer, storage-provider, identity-consumer, log-source, or
  trace-source capabilities can expose the related concern view so users know
  where that concern will be configured or inspected.
- **Resource type declarations** can declare additional resource-specific
  views or sections. This lets a provider describe that every SQL Server
  resource has a Storage view, every container app has Deployment and Replicas
  views, or every Docker host has a Containers view without hardcoding those
  rules into the shell.
- **Provider-owned sections** can enrich a predefined view without replacing
  it. For example, an application provider can add exposure actions to
  `networking:endpoints`, while a DNS provider can add reconciliation details
  to `networking:dns`.
- **Provider-owned tabs** are reserved for complete resource-specific
  workflows or for replacing a built-in predefined view when the generated view
  is not appropriate for that resource type.

This keeps the Resource Manager UX extensible without fragmenting common
concerns. The shell owns the predefined concern vocabulary and generated
fallbacks; resource providers own resource-specific depth and interpretation.

The Resource UI provider should choose the smallest extension point that
matches its need:

- **Use a normal resource tab** when the provider owns a complete workflow or
  view, such as SQL Server storage, container app deployment, or provider
  configuration.
- **Use the same predefined tab ID** when the provider intentionally replaces a
  predefined view for its resource type.
- **Use a predefined view section** when the provider wants to add
  provider-specific interpretation inside a common concern view without
  replacing the generated sections.

Predefined view sections are registered against a resource type and a
predefined view ID:

```csharp
builder.AddResourcePredefinedViewSection<Pages.AcmeEndpointPolicy>(
    "acme.gateway",
    ResourcePredefinedViewIds.Endpoints,
    "acme.endpoint-policy",
    "Endpoint policy",
    50);
```

The section component can declare any of the following optional parameters:

```csharp
[Parameter]
public string ResourceId { get; set; } = string.Empty;

[Parameter]
public string ViewId { get; set; } = string.Empty;

[Parameter]
public string SectionId { get; set; } = string.Empty;

[Parameter]
public Resource? Resource { get; set; }
```

CloudShell passes only the parameters declared by the section component. This
keeps small sections simple while still allowing richer sections to use the
projected `Resource` when the hosting view has already loaded it.

The section should load resource data through public domain managers such as
`IResourceManager`. It should not depend directly on provider stores or
Control Plane internals unless the UI and provider intentionally ship as one
in-process capability package.

Generated predefined views use an ordered section layout. Shell-owned sections
and provider-owned sections are sorted together by `Order`, then by title. A
provider can append content by choosing an order after the built-in sections,
or insert content between built-in sections by choosing an order in that range.
For example, a provider summary for Overview can use an order after the
standard Essentials section and before Runtime.

The current predefined-view contract is explicit and enforced by the extension
builder:

| Predefined view | Provider may replace tab by reusing the view ID | Provider may add sections |
| --- | --- | --- |
| `general:overview` | Yes | Yes |
| `general:configuration` | Yes | No |
| `networking:endpoints` | Yes | Yes |
| `networking:dns` | Yes | Yes |
| `management:identity` | Yes | Yes |
| `management:access-control` | Yes | Yes |
| `storage:volumes` | Yes | No |
| `management:activity` | Yes | Yes |
| `management:monitoring` | Yes | Yes |
| `management:environment` | Yes | No |
| `telemetry:logs` | Yes | No |
| `telemetry:traces` | Yes | No |
| `telemetry:metrics` | Yes | No |
| `storage:storage` | Yes | No |

This means:

- A provider can replace a predefined view by contributing a normal resource tab
  with the same predefined view ID when replacement is allowed.
- A provider cannot reuse a predefined view's public route segment under a
  different group, or contribute two tabs with the same public route segment
  for one resource type.
- A provider can contribute a predefined view section only for a view that
  explicitly supports sections.
- Unknown or non-extensible predefined-view section targets are rejected during
  extension registration instead of being accepted silently.

Use section contributions when the provider is adding interpretation,
summaries, diagnostics, or operations to a common concern. Use a custom tab or
predefined-tab replacement when the provider owns the whole workflow or when
the generated layout would fight the resource's mental model.

## Resource Actions and UI Actions

Resource actions are domain operations projected through
`Resource.ResourceActions` by the Control Plane provider. They can be guarded
by resource operation permissions and executed through `IResourceManager`.

A UI action is different from a resource action. UI actions are custom Resource
Manager behaviors attached by a UI extension, such as opening a wizard,
navigating to a provider view, or invoking a resource action with additional
presentation state. Resource Manager may display standard lifecycle resource
actions automatically. Custom UI actions must be registered by the UI resource
provider or extension that owns the shell presentation.

Use this rule:

- Register resource actions in the Control Plane provider when the operation
  belongs to the resource model and needs authorization, API access, or remote
  execution.
- Register UI actions in the Resource Manager UI integration when the behavior
  is presentation-specific or needs a custom user workflow.
- Let the UI action invoke a resource action when the workflow is a custom
  presentation of a domain operation.

## Resource Shape Expectations

Root resources are persisted registrations. Discovered resources stay hidden
until the user explicitly adds one through a resource type registration UI.
Descendants of a registered root can appear dynamically as sub-resources.

Resource groups are user-managed project boundaries owned by the platform, not
by providers. A root resource can be assigned to a group during registration,
and its sub-resources inherit that group for filtering and display.

Parent-child resource relationships are distinct from dependency
relationships: the parent controls containment in Resource Manager, while
`DependsOn(...)` records topology or ordering between any two resources.
