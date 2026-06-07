# Control plane API

CloudShell should treat control-plane services as separate products from the WebUI shell. The shell remains customizable and extensible; shell integrations connect the shell to control-plane services through versioned API contracts.

## Naming

Use **CloudShell Control Plane** for the service and **CloudShell Control Plane API** for its HTTP contract.

Avoid naming the WebUI host as the control plane. The WebUI is the shell surface. The control plane owns resource inventory, registrations, lifecycle procedures, logs, and provider-backed operational data.

## Versioning

The first HTTP API is versioned as `v1`:

```text
/api/control-plane/v1
```

The matching OpenAPI document is:

```text
/openapi/control-plane-v1.json
```

Configuration services expose a provider-owned runtime API under
`/api/configuration`. That API is separate from the Control Plane API because it
is consumed directly by resource processes and uses configuration-service access
tokens instead of user/session authentication. See
[Configuration services](configuration-services.md).

Keep breaking changes behind a new route and document, such as
`/api/control-plane/v2` and `/openapi/control-plane-v2.json`. Remote
control-plane adapters should pin the generated client to the major API version
they support.

## Shell integrations

Shell integrations should depend on the `IControlPlane` facade, or
one of its narrower resource, template, log, or trace manager facets, instead of
in-process control-plane stores, providers, or generated Web API clients. The
facade is intentionally domain-shaped: consumers ask to list resources, execute
resource actions, read logs, or import templates without caring whether those
operations are local service calls or HTTP requests.

In combined hosts, CloudShell registers `InProcessControlPlane`, which
adapts the existing services. In split hosts, an integration registers a remote
implementation that maps the same interfaces to generated OpenAPI clients.

```csharp
builder.Services.AddRemoteControlPlane(
    new Uri("https://control-plane.example.com"));
```

A typical remote adapter owns:

- a generated `v1` control-plane client
- configuration for the target control-plane service base URL
- authentication/token forwarding for calls from the shell to the control plane
- mapping between Web API DTOs and the CloudShell domain abstraction

This keeps the WebUI deployable and versionable separately from the service
while preserving the extension model described in
[shell customization design goals](shell-customization.md).

Provider packages that run inside the Control Plane can keep using lower-level
interfaces such as `IResourceManagerStore`, `IResourceRegistrationStore`,
`ILogStore`, and provider contracts. Those interfaces are implementation
interfaces for the service process; `IControlPlane` is the shell and
integration boundary.

## OpenAPI Client Generation

Use generated C# clients inside remote `IControlPlane`
implementations. The control-plane service should own the OpenAPI document; the
remote adapter consumes a pinned document and generates typed clients during
build or as part of package generation. UI and extension code should depend on
the domain managers, not the generated client.

The `CloudShell.ControlPlane.Client` package provides the default remote adapter
for the current API shape. A generated OpenAPI client can replace its internal
HTTP calls later without changing shell integrations.

For .NET projects that still use the OpenAPI MSBuild reference flow, keep the
reference in the adapter `.csproj` instead of running ad hoc generator commands:

```xml
<ItemGroup>
  <OpenApiReference
    Include="OpenAPIs/control-plane-v1.json"
    CodeGenerator="NSwagCSharp"
    Namespace="CloudShell.Integrations.ControlPlane.V1"
    ClassName="ControlPlaneApiClient">
    <Options>/GenerateClientInterfaces:true /InjectHttpClient:true /DisposeHttpClient:false</Options>
  </OpenApiReference>
</ItemGroup>
```

For .NET 10 and later, do not depend on
`Microsoft.Extensions.ApiDescription.Client` for new adapter packages. Microsoft
deprecated that package and its `OpenApiReference`/`dotnet openapi` MSBuild
flow. Use generator-specific tooling instead, usually an explicit NSwag MSBuild
target or a committed generated client refreshed by CI.

Recommended NSwag configuration:

```json
{
  "runtime": "Net110",
  "documentGenerator": {
    "fromDocument": {
      "url": "https://control-plane.example.com/openapi/control-plane-v1.json"
    }
  },
  "codeGenerators": {
    "openApiToCSharpClient": {
      "className": "ControlPlaneApiClient",
      "namespace": "CloudShell.Integrations.ControlPlane.V1",
      "injectHttpClient": true,
      "disposeHttpClient": false,
      "generateClientInterfaces": true
    }
  }
}
```

Prefer generating clients into the remote adapter package, not into the shell
host. That keeps shell integrations free from transport details and allows
different adapters to support different control-plane service versions. If an
adapter supports multiple API versions, generate separate clients and namespaces
per version, for example `CloudShell.Integrations.ControlPlane.V1` and
`CloudShell.Integrations.ControlPlane.V2`.
