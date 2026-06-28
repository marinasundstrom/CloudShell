# Host Virtual Network Sample

This sample declares a virtual network that uses the local host networking
provider to materialize an endpoint mapping on macOS, Linux, or Windows.

The resource graph declares:

- `networking:graph-host-local`: the local host networking provider.
- `application.aspnet-core-project:graph-vnet-api`: a stopped ASP.NET Core target app endpoint at
  `http://localhost:5291`.
- `network:graph-sample-vnet`: a virtual network with public endpoint
  `http://localhost:5292`.
- `mapping:graph-api-public`: a mapping from the virtual network endpoint to
  the API endpoint through `networking:graph-host-local`.

Run the sample:

```bash
dotnet run --project samples/HostVirtualNetwork/CloudShell.HostVirtualNetwork.csproj -- --urls http://localhost:5011
```

Optional endpoint-port settings:

- `HostVirtualNetwork:TargetPort`: target API port. Defaults to `5291`.
- `HostVirtualNetwork:GraphVirtualNetworkPort`: graph virtual-network public
  endpoint port. Defaults to `5292`.

`networking:graph-host-local` is projected as an active host networking
resource on macOS, Linux, and Windows. Use the virtual network's
`Reconcile endpoint mappings` action to start a local TCP proxy from
`localhost:5292` to `localhost:5291`.

The graph virtual network's `Reconcile endpoint mappings` action is wired to a
sample-local runtime bridge. The bridge projects the resolved graph resources
into the existing Resource Manager endpoint-mapping provisioner contract, then
delegates to the local host networking provisioner. This is the runtime glue
for the sample; the graph providers still only declare and project networking
configuration.

The sample now declares only the graph-backed resources. The old
`networking:host-local`, `application:vnet-api`, `network:sample-vnet`, and
`HostVirtualNetwork:GraphOnly` comparison path have been removed. The remaining
sample-local bridge validates that `networking:graph-host-local`,
`application.aspnet-core-project:graph-vnet-api`, and
`network:graph-sample-vnet` can start the graph-backed API and materialize the
graph public ingress without the old application-provider aggregate.

This provider is the portable MVP baseline. It does not create OS-native
virtual adapters, firewall rules, NAT rules, or network isolation. Future
OS-specific providers can materialize the same endpoint mapping model through
native Linux or Windows networking facilities when those capabilities are
available.

## Porting Status

- Ported: graph local host networking, graph ASP.NET Core target API, graph
  virtual-network endpoint/mapping declaration, Resource Manager projection,
  graph reconcile handoff to the existing local host endpoint-mapping
  provisioner, and removal of the old provider comparison branch. Smoke
  coverage starts the graph API, executes the graph reconcile action through
  the Control Plane API, and verifies the graph public ingress reaches the API
  health endpoint without old provider records.
- Remaining: generalized endpoint-mapping provisioner integration outside the
  sample bridge, UI registration/update flow, live mapping count projection,
  runtime diagnostics, and later isolation of OS-specific networking behavior
  behind Resource Manager/runtime boundaries.
