# Load Balancers

Use a load balancer resource when the user wants gateway-level control over
stable routing and a provider should materialize the actual proxy or gateway
configuration. Load balancers project as `cloudshell.loadBalancer` resources.

For the normal single-application case, prefer container app ingress. A
replicated container app with an HTTP or TCP endpoint owns its exposed app
endpoint and the orchestrator starts or renders the app-specific ingress
implementation during the app start/restart flow. No separate load-balancer
resource or manual apply action is required for that path.

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
a raw target port as an authoring convenience. Endpoint references remain the
preferred stable contract for single-app targets because the container app
endpoint can already represent app-owned ingress over one or more replicas.
Port-based routes remain useful for advanced provider-owned routing scenarios.

When a provider materializes a container app through Docker Compose,
Kubernetes, or another runtime, the orchestrator-specific service, ingress, or
backend is implementation detail of the container app. It is not a separate
CloudShell resource target. CloudShell routes should continue to target the
stable container app or another stable Resource Manager artifact; providers map
that target to their own service, backend, or endpoint model.

```csharp
var dockerHost = resources.AddDocker("docker:sample-host", "Sample Container Host");

var webApp = resources
    .AddContainerApplication("application:web", "Web App", "nginx:1.27-alpine")
    .WithEndpoint("http", targetPort: 80, port: 5080, protocol: "http");

var apiService = resources
    .AddContainerApplication("application:api", "API Service", "traefik/whoami:v1.10")
    .WithEndpoint("http", targetPort: 80, port: 5081, protocol: "http")
    .WithReplicas(3);

var postgres = resources
    .AddContainerApplication("application:postgres", "Postgres", "postgres:16-alpine")
    .WithEndpoint("postgres", targetPort: 5432, port: 55432, protocol: "tcp")
    .WithEnvironment("POSTGRES_PASSWORD", "cloudshell")
    .WithEnvironment("POSTGRES_DB", "cloudshell");

var lb = resources
    .AddLoadBalancer("public")
    .UseProvider("traefik")
    .UseHost(dockerHost)
    .ExposeHttp(80)
    .ExposeHttps(443)
    .ExposeTcp(5432, "postgres");

lb.MapHost("app.local", webApp, port: 80);
lb.MapPath("api.local", "/v1", apiService, port: 80);
lb.MapTcp(5432, postgres, targetPort: 5432);
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

Load-balancer setup validates route shape before persisting the resource:
routes must reference a declared entrypoint, HTTP routes must use HTTP/HTTPS
entrypoints, TCP routes must use TCP entrypoints, and exact duplicate route
matches on the same entrypoint are rejected.

Resource action capabilities evaluate the same provider context used by apply
and lifecycle execution. If the selected provider, host resource, route target,
or target endpoint cannot be resolved, Resource Manager can show the reason
before the action is invoked.

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

`CloudShell.Providers.Traefik` supports file-provider mode and optional
provider-owned Docker runtime mode. Applying the load balancer writes Traefik
dynamic configuration from CloudShell routes. When runtime container management
is enabled, the provider also starts a Traefik container on the selected Docker
host and attaches it to the same default Docker network used by the default
container-app runner.
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
    options.ManageRuntimeContainer = true;
});
```

For Docker-backed container apps, prefer port-based routes when Traefik is
running as a container. The provider can then route to convention-named
container app instances on the shared Docker network instead of host-published
`localhost` ports.

## Sample

The `samples/LoadBalancer` project declares a selected container host, real
public container images for web/API/Postgres targets, a three-replica API
container app, and a Traefik-backed public load balancer. Running the sample
normally enables Traefik runtime container management. Its smoke test disables
runtime container startup and verifies the generated Traefik dynamic
configuration file, including Docker-network backends for the web app, API
replicas, and Postgres.

Run it with:

```bash
dotnet run --project samples/LoadBalancer/CloudShell.LoadBalancer.csproj
```

Start the target container apps from Resource Manager, then invoke **Apply load
balancer configuration** on the public load balancer. The HTTP routes match
configured host names:

```bash
curl --resolve app.local:80:127.0.0.1 http://app.local/
curl --resolve api.local:80:127.0.0.1 http://api.local/v1/get
```

## Current Limits

The current implementation focuses on the stable resource contract, Traefik
file-provider output, and Docker runtime startup for the sample path. It does
not yet provide:

- provider configuration preview in the UI
- editing multiple load-balancer routes after creation
- full structured validation diagnostics before applying routes
- provider-managed runtime probes and richer host diagnostics
- TLS certificate resources or certificate binding
- weighted backend pools, traffic splitting, provider-observed replica health,
  or dynamic backend membership beyond the current desired replica count
