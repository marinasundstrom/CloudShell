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

`networking:host-local` is projected as an active host networking resource on
macOS, Linux, and Windows. Use the virtual network's
`Reconcile endpoint mappings` action to start a local TCP proxy from
`localhost:5290` to `localhost:5291`.

This provider is the portable MVP baseline. It does not create OS-native
virtual adapters, firewall rules, NAT rules, or network isolation. Future
OS-specific providers can materialize the same endpoint mapping model through
native Linux or Windows networking facilities when those capabilities are
available.
