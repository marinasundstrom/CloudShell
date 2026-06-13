# Service Discovery

CloudShell currently supports application-level service discovery for local
development and descriptor-based orchestration. This is the Aspire-compatible
configuration shape used by `Microsoft.Extensions.ServiceDiscovery`, not the
future network-level service discovery resource capability.

## Current Model

Applications declare endpoint references with `WithReference(...)`.
Executable applications and container apps also opt in with
`WithServiceDiscovery()`. ASP.NET Core project resources enable the same
mapping automatically when `WithReference(...)` is used, although keeping
`WithServiceDiscovery()` in a sample declaration can make the intent explicit.

When the application starts, CloudShell projects endpoints from referenced
resources into environment variables that bind to the .NET configuration
`services` section:

```text
services__<resource-name-or-id>__<endpoint-name-or-scheme>__0=<endpoint-address>
```

For example, a resource named `Sample App Settings` with an `entries` endpoint
can be projected as:

```text
services__sample-app-settings__entries__0=http://localhost:5138/api/configuration/entries
```

CloudShell emits names based on both the referenced resource display name and
resource ID, normalized to lower-case dash-separated service names. Endpoint
keys are emitted from both the endpoint name and protocol when they differ.
Process-only endpoints are not projected.

Service discovery variables are generated from references, not from lifecycle
dependencies. Use `WithReference(...)` when the application should discover a
resource endpoint. Use `DependsOn(...)` when the resource graph needs startup
ordering. A resource can use both relationships.

## Application Requirements

CloudShell injects the configuration values, but application code that wants
Microsoft's logical URI resolution must reference and enable Microsoft's
service discovery package:

```xml
<PackageReference Include="Microsoft.Extensions.ServiceDiscovery" Version="10.4.0" />
```

```csharp
builder.Services.AddServiceDiscovery();
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddServiceDiscovery();
});
```

After that, `HttpClient` instances created through `IHttpClientFactory` can use
logical service URIs such as:

```csharp
builder.Services.AddHttpClient("settings", client =>
{
    client.BaseAddress = new Uri("https+http://sample-app-settings");
});
```

Applications can also read the projected endpoint value directly from
`IConfiguration`. CloudShell provides a small helper in
`CloudShell.Configuration`:

```csharp
var endpoint = builder.Configuration.GetResourceUri("sample-app-settings", "entries");
```

The helper only reads configuration. It does not replace Microsoft's service
discovery package when the application wants `HttpClient` logical URI
resolution.

## Identity And Authorization

Service discovery only locates endpoints. It does not authenticate the caller
and does not grant access.

Configuration Store, Secrets Vault, and other protected services should be
treated like any other referenced service:

- `WithReference(...)` and service discovery locate the service endpoint.
- `WithIdentity(...)` or `RequireIdentity()` assigns the calling resource
  identity.
- Resource grants such as `ReadEntries` or `ReadSecrets` authorize the identity
  against the target resource.

The current Configuration Store and Secrets Vault SDK clients still support
service-specific endpoint variables such as `CLOUDSHELL_CONFIGURATION_*` and
`CLOUDSHELL_SECRETS_*`. Those variables are a local development integration
path for the first client slice. They are separate from the resource identity
credential variables and should not be treated as the long-term discovery
model.

## Future Network-Level Discovery

The current model is per-application configuration projection. It works well
for local development, ASP.NET Core project resources, executables, and
descriptor-based orchestrators such as Docker Compose.

The post-MVP network-level model should be a resource capability owned by the
networking layer. It should allow services inside a host network or virtual
network to resolve CloudShell resources without every workload receiving
application-specific environment variables. That future capability should
complement this current model, not reinterpret resource identity or lifecycle
dependencies as endpoint discovery.
