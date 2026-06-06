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

Keep breaking changes behind a new route and document, such as `/api/control-plane/v2` and `/openapi/control-plane-v2.json`. Shell integrations should pin the generated client to the major API version they support.

## Shell integrations

Shell integrations should depend on generated API clients rather than in-process control-plane services. A typical integration owns:

- a generated `v1` control-plane client
- extension-specific UI pages or hosted shell views
- configuration for the target control-plane service base URL
- authentication/token forwarding for calls from the shell to the control plane

This keeps the WebUI deployable and versionable separately from the service while preserving the extension model described in [shell customization design goals](shell-customization.md).

## Client generation

Use generated C# clients in shell integrations. The control-plane service should own the OpenAPI document; integrations consume a pinned document and generate typed clients during build or as part of package generation.

For .NET projects that still use the OpenAPI MSBuild reference flow, keep the reference in the integration `.csproj` instead of running ad hoc generator commands:

```xml
<ItemGroup>
  <OpenApiReference
    Include="OpenAPIs/control-plane-v1.json"
    CodeGenerator="NSwagCSharp"
    Namespace="CloudShell.Integrations.ControlPlane.V1"
    ClassName="CloudShellControlPlaneClient">
    <Options>/GenerateClientInterfaces:true /InjectHttpClient:true /DisposeHttpClient:false</Options>
  </OpenApiReference>
</ItemGroup>
```

For .NET 10 and later, do not depend on `Microsoft.Extensions.ApiDescription.Client` for new integration packages. Microsoft deprecated that package and its `OpenApiReference`/`dotnet openapi` MSBuild flow. Use generator-specific tooling instead, usually an explicit NSwag MSBuild target or a committed generated client refreshed by CI.

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
      "className": "CloudShellControlPlaneClient",
      "namespace": "CloudShell.Integrations.ControlPlane.V1",
      "injectHttpClient": true,
      "disposeHttpClient": false,
      "generateClientInterfaces": true
    }
  }
}
```

Prefer generating clients into the integration package, not into the shell host. That keeps integrations free to support different control-plane service versions. If a shell integration supports multiple API versions, generate separate clients and namespaces per version, for example `CloudShell.Integrations.ControlPlane.V1` and `CloudShell.Integrations.ControlPlane.V2`.
