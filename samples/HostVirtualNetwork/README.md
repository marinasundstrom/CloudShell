# Host Virtual Network Sample

This sample declares a virtual network that uses the local host networking
provider to materialize an endpoint mapping on macOS, Linux, or Windows.

The resource graph declares:

- `networking:host-local`: the local host networking provider.
- `application.aspnet-core-project:vnet-api`: a stopped ASP.NET Core target app endpoint at
  `http://localhost:5291`.
- `network:sample-vnet`: a virtual network with public endpoint
  `http://localhost:5292`.
- `mapping:api-public`: a mapping from the virtual network endpoint to the API
  endpoint through `networking:host-local`.

Run the sample:

```bash
dotnet run --project samples/HostVirtualNetwork/CloudShell.HostVirtualNetwork.csproj -- --urls http://localhost:5011
```

Optional endpoint-port settings:

- `HostVirtualNetwork:TargetPort`: target API port. Defaults to `5291`.
- `HostVirtualNetwork:VirtualNetworkPort`: virtual-network public endpoint
  port. Defaults to `5292`.

`networking:host-local` is projected as an active host networking
resource on macOS, Linux, and Windows. Use the virtual network's
`Reconcile endpoint mappings` action to start a local TCP proxy from
`localhost:5292` to `localhost:5291`.

The virtual network's `Reconcile endpoint mappings` action is wired to a
sample-local runtime bridge. The bridge projects the resolved resources into
the existing Resource Manager endpoint-mapping provisioner contract, then
delegates to the local host networking provisioner. This is the runtime glue
for the sample; the resource graph providers still only declare and project
networking configuration.

The sample now declares only Resource Definitions-backed resources. The old
direct Resource Manager comparison path has been removed. The remaining
sample-local bridge validates that `networking:host-local`,
`application.aspnet-core-project:vnet-api`, and `network:sample-vnet` can start
the API and materialize the public ingress without the old application-provider
aggregate.

This provider is the portable MVP baseline. It does not create OS-native
virtual adapters, firewall rules, NAT rules, or network isolation. Future
OS-specific providers can materialize the same endpoint mapping model through
native Linux or Windows networking facilities when those capabilities are
available.

## Porting Status

- Ported: local host networking, ASP.NET Core target API, virtual-network
  endpoint/mapping declaration, Resource Manager projection, reconcile handoff
  to the existing local host endpoint-mapping provisioner, and removal of the
  old provider comparison branch. Smoke coverage starts the API, executes the
  reconcile action through the Control Plane API, and verifies the public
  ingress reaches the API health endpoint without old provider records.
- Remaining: generalized endpoint-mapping provisioner integration outside the
  sample bridge, UI registration/update flow, live mapping count projection,
  runtime diagnostics, and later isolation of OS-specific networking behavior
  behind Resource Manager/runtime boundaries.
