# Load Balancers

Use a load balancer resource when CloudShell should own the stable routing
contract and a provider should materialize the actual proxy or gateway
configuration. Load balancers project as `cloudshell.loadBalancer` resources.

The resource is intentionally provider-neutral. The first provider is Traefik
in file-provider mode, but the resource model does not require Traefik, Docker,
or a container app implementation.

For related resource types, see [Container apps](container-apps.md),
[Application resources](application-resources.md), and the
[Load Balancer Resource Proposal](../proposals/load-balancer-resource.md).

## Resource Shape

A load balancer has:

- a provider name, such as `traefik`
- an optional host resource where provider-owned infrastructure should run
- public entrypoints, such as HTTP 80, HTTPS 443, or TCP 5432
- routes that map host/path/TCP rules to stable resource targets

Routes target resources and their projected endpoints. A route can also specify
a raw target port as an authoring convenience, but endpoint references are the
preferred stable contract.

When a provider materializes a container app through Docker Compose,
Kubernetes, or another runtime, the orchestrator-specific service or backend is
implementation detail of the container app. It is not a separate CloudShell
resource target. CloudShell routes should continue to target the stable
container app or another stable Resource Manager artifact; providers map that
target to their own service, backend, or endpoint model.

```csharp
var dockerHost = resources.AddDocker("docker:sample-host", "Sample Container Host");

var webApp = resources
    .AddContainerApplication("application:web", "Web App", "cloudshell/mock-web:1.0.0")
    .WithEndpoint("http", targetPort: 8080, port: 5080, protocol: "http");

var apiService = resources
    .AddContainerApplication("application:api", "API Service", "cloudshell/mock-api:1.0.0")
    .WithEndpoint("http", targetPort: 5000, port: 5081, protocol: "http");

var postgres = resources
    .AddContainerApplication("application:postgres", "Postgres", "cloudshell/mock-postgres:1.0.0")
    .WithEndpoint("postgres", targetPort: 5432, port: 55432, protocol: "tcp");

var lb = resources
    .AddLoadBalancer("public")
    .UseProvider("traefik")
    .UseHost(dockerHost)
    .ExposeHttp(80)
    .ExposeHttps(443)
    .ExposeTcp(5432, "postgres");

lb.MapHost("app.local", webApp, endpoint: "http");
lb.MapPath("api.local", "/v1", apiService, endpoint: "http");
lb.MapTcp(5432, postgres, endpoint: "postgres");
```

`AddLoadBalancer("public")` normalizes to `load-balancer:public`.

## Provider And Host

The provider selects the implementation, such as Traefik, Nginx, HAProxy, Envoy,
or a custom on-premise controller. The host selects the runtime or control
boundary where provider-owned infrastructure is materialized.

Use "container host" for the selectable CloudShell host resource and
"container runtime" for the implementation capability behind that host. Avoid
using "engine" as a CloudShell abstraction except for product-specific wording
such as Docker Engine.

`UseHost(...)` is optional. If omitted, provider execution should resolve the
configured default container host or provider-preferred host. When a provider
runs in a container, that implementation container is provider-owned runtime
state or a child resource, not a user-authored container app.

## Resource Manager

The generated Resource Manager views show:

- load-balancer provider and selected host attributes
- entrypoint, HTTP route, and TCP route counts
- the route table in the overview and resource blade
- dependencies on the selected host and target resources
- an `Apply load balancer configuration` resource action when routes exist

Users can create a load balancer from Resource Manager with **Add resource**,
then selecting **Load Balancer**. The add view also supports a resource-type
deep link:

```text
/resources/add?type=cloudshell.loadBalancer
```

The first UI registration slice supports the Traefik provider, optional
container-host selection, HTTP/HTTPS/TCP entrypoints, and initial HTTP or TCP
routes to an existing resource endpoint or raw target port.

The apply action validates the route targets and delegates to the selected
`ILoadBalancerProvider`.

## API Projection

Load balancer routes are part of the normal resource response:

```http
GET /api/control-plane/v1/resources/load-balancer%3Apublic
```

The response includes `loadBalancerRoutes` and normal hypermedia
`resourceActions`. Applying provider configuration uses the advertised resource
action URL:

```http
POST /api/control-plane/v1/resources/load-balancer%3Apublic/actions/applyLoadBalancerConfiguration
```

No separate load-balancer API group exists yet. A resource-type-specific API can
be added later if load balancers need a richer domain command surface than the
generic Resource Manager action model.

## Traefik Provider

`CloudShell.Providers.Traefik` currently supports file-provider mode. Applying
the load balancer writes Traefik dynamic configuration from CloudShell routes.
The provider supports:

- HTTP routers using `Host(...)` and `PathPrefix(...)`
- TCP routers using `HostSNI(...)`
- HTTP services with target URLs
- TCP services with target addresses

Configure the output directory when registering the provider:

```csharp
cloudShell.AddTraefikProvider(options =>
{
    options.DynamicConfigurationDirectory = "Data/traefik";
});
```

The provider does not yet start or manage a Traefik process/container. Container
mode should use the selected host and create provider-owned runtime state tied
to the load-balancer lifecycle.

## Sample

The `samples/LoadBalancer` project declares a selected container host, mock
web/API/TCP container app targets, and a Traefik-backed public load balancer.
Its smoke test invokes the advertised apply action and verifies the generated
Traefik dynamic configuration file.

Run it with:

```bash
dotnet run --project samples/LoadBalancer/CloudShell.LoadBalancer.csproj
```

## Current Limits

The first implementation focuses on the stable resource contract and Traefik
file-provider output. It does not yet provide:

- provider configuration preview in the UI
- editing multiple load-balancer routes after creation
- structured validation diagnostics before applying routes
- provider-managed Traefik container lifecycle
- TLS certificate resources or certificate binding
- weighted backend pools, traffic splitting, or provider-observed replica
  health
