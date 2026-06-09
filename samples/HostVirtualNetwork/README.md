# Host Virtual Network Sample

This sample declares a virtual network that uses the macOS host networking
provider to materialize an endpoint mapping.

The resource graph declares:

- `networking:host-macos`: the macOS host networking provider.
- `application:vnet-api`: a stopped ASP.NET Core target app endpoint at
  `http://localhost:5291`.
- `network:sample-vnet`: a virtual network with public endpoint
  `http://localhost:5290`.
- `mapping:api-public`: a mapping from the virtual network endpoint to the API
  endpoint through `networking:host-macos`.

Run the sample:

```bash
dotnet run --project samples/HostVirtualNetwork/CloudShell.HostVirtualNetwork.csproj -- --urls http://localhost:5011
```

On macOS, `networking:host-macos` is projected as an active host networking
resource. Use the virtual network's `Reconcile endpoint mappings` action to
start a local TCP proxy from `localhost:5290` to `localhost:5291`.

On other operating systems, the virtual network still demonstrates the resource
model, endpoint request, and provider selection, but the macOS provider is not
activated.
