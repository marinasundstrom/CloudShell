# Host Virtual Network Sample

This sample declares a virtual network that uses the local host networking
provider to materialize an endpoint mapping on macOS, Linux, or Windows.

The resource graph declares:

- `networking:host-local`: the local host networking provider.
- `application:vnet-api`: a stopped ASP.NET Core target app endpoint at
  `http://localhost:5291`.
- `network:sample-vnet`: a virtual network with public endpoint
  `http://localhost:5290`.
- `mapping:api-public`: a mapping from the virtual network endpoint to the API
  endpoint through `networking:host-local`.

Run the sample:

```bash
dotnet run --project samples/HostVirtualNetwork/CloudShell.HostVirtualNetwork.csproj -- --urls http://localhost:5011
```

Optional endpoint-port settings:

- `HostVirtualNetwork:TargetPort`: target API port. Defaults to `5291`.
- `HostVirtualNetwork:VirtualNetworkPort`: legacy virtual-network public
  endpoint port. Defaults to `5290`.
- `HostVirtualNetwork:GraphVirtualNetworkPort`: graph virtual-network public
  endpoint port. Defaults to `5292`.
- `HostVirtualNetwork:GraphOnly`: when `true`, omits the old application
  provider registration and old `networking:host-local`, `application:vnet-api`,
  and `network:sample-vnet` records. Defaults to `true`; set it to `false`
  only when running the side-by-side comparison path against the old providers.

`networking:host-local` is projected as an active host networking resource on
macOS, Linux, and Windows. Use the virtual network's
`Reconcile endpoint mappings` action to start a local TCP proxy from
`localhost:5290` to `localhost:5291`.

The sample carries graph-backed POC resources through the Resource Definitions
bridge:

- `networking:graph-host-local`: graph-backed local host networking.
- `application.aspnet-core-project:graph-vnet-api`: graph-backed ASP.NET Core
  target API projection for the same project and local endpoint.
- `network:graph-sample-vnet`: graph-backed virtual network with typed startup
  dependencies on the graph host-networking and API resources, plus a public
  endpoint at `http://localhost:5292`, endpoint address mapping, and
  source-to-target endpoint mapping shape like the legacy virtual network.

The graph virtual network's `Reconcile endpoint mappings` action is wired to a
sample-local runtime bridge. The bridge projects the resolved graph resources
into the existing Resource Manager endpoint-mapping provisioner contract, then
delegates to the local host networking provisioner. This is the runtime glue
for the sample; the graph providers still only declare and project networking
configuration.

Graph-only mode keeps that bridge active while removing the legacy sample
resource records. It is the switch-readiness path for validating that
`networking:graph-host-local`, `application.aspnet-core-project:graph-vnet-api`,
and `network:graph-sample-vnet` can start the graph-backed API and materialize
the graph public ingress without the old application-provider aggregate.

This provider is the portable MVP baseline. It does not create OS-native
virtual adapters, firewall rules, NAT rules, or network isolation. Future
OS-specific providers can materialize the same endpoint mapping model through
native Linux or Windows networking facilities when those capabilities are
available.

## Porting Status

- Ported: graph local host networking, graph ASP.NET Core target API, graph
  virtual-network endpoint/mapping declaration, Resource Manager projection,
  graph-only declaration mode, and graph reconcile handoff to the existing
  local host endpoint-mapping provisioner. Smoke coverage starts the graph API,
  executes the graph reconcile action through the Control Plane API in
  graph-only mode, and verifies the graph public ingress reaches the API health
  endpoint without old provider records.
- Remaining: full provider switch, UI registration/update flow, live mapping
  count projection, runtime diagnostics, and later isolation of OS-specific
  networking behavior behind Resource Manager/runtime boundaries.
