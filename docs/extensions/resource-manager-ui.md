# Resource Manager UI Extensions

Resource Manager UI extensions are resource-specific UI contributions built on
the base [UI extension architecture](ui.md). They run in the CloudShell UI app
and should use domain managers such as `IResourceManager`, not internal
Control Plane stores or provider implementations.

Use this surface for Add Resource forms, update components, generated details
customization, resource tabs, detail routes, and resource UI actions. Pair it
with a [Control Plane resource provider](control-plane-resource-providers.md)
when the resource type needs provider-backed behavior.

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
            Name: "health")
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

## Resource Manager Projection

The shell generates a default resource detail view from the projected
`Resource` when a provider does not contribute a specialized view. The
generated view shows stable identity, class, endpoints, attributes,
dependencies, health checks, actions, and observability details. The built-in
route is `/resources/{resourceId}/details?tab=overview`; provider-contributed
tabs use the same route with their tab ID in the `tab` query parameter.

Resources can set `DetailRoute` to link to an extension-owned view. This
supports the familiar cloud-portal pattern where a resource opens its own
operational workspace.

Resource types can also contribute tabs or an update component. Those
provider-owned views override the generated default for resources of that type.

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
