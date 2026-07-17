# Host Virtual Network Sample

This sample declares a virtual network that uses the local host networking
provider to materialize an endpoint mapping on macOS, Linux, or Windows. It
also declares manual private virtual-network addresses and DNS name mappings
that can be written as CoreDNS configuration.

The local TCP proxy and generated CoreDNS files are accepted
local-development MVP bridges. They prove the resource model, endpoint
mapping, and private-name authoring flow without requiring OS-native virtual
adapters, firewall/NAT rules, or network isolation on every developer
machine.

The resource graph declares:

- `cloudshell.hostNetworking.local:host-local`: the local host networking provider.
- `application.dotnet-app:vnet-api`: a stopped ASP.NET Core target app endpoint at
  `http://localhost:5291`, plus the private virtual-network endpoint
  `http://10.42.0.10:80`.
- `application.dotnet-app:vnet-worker`: a second stopped ASP.NET Core
  resource with the private virtual-network endpoint `http://10.42.0.11:80`.
- `cloudshell.virtualNetwork:sample-vnet`: a virtual network with public endpoint
  `http://localhost:5292`.
- `mapping:api-public`: a mapping from the virtual network endpoint to the API
  endpoint through `cloudshell.hostNetworking.local:host-local`.
- `cloudshell.dnsZone:sample-vnet-internal`: a private DNS zone using the
  provider-owned CoreDNS zone-file publishing adapter.
- `cloudshell.nameMapping:api-internal` and
  `cloudshell.nameMapping:worker-internal`: private names mapped to the
  service-specific virtual-network IP addresses.

Run the sample:

```bash
dotnet run --project samples/HostVirtualNetwork/CloudShell.HostVirtualNetwork.csproj -- --urls http://localhost:5011
```

Optional endpoint-port settings:

- `HostVirtualNetwork:TargetPort`: target API port. Defaults to `5291`.
- `HostVirtualNetwork:WorkerTargetPort`: second service host-local port.
  Defaults to `5293`.
- `HostVirtualNetwork:VirtualNetworkPort`: virtual-network public endpoint
  port. Defaults to `5292`.
- `HostVirtualNetwork:CoreDnsDirectory`: output directory for generated
  CoreDNS files. Defaults to `samples/HostVirtualNetwork/Data/coredns`.

`cloudshell.hostNetworking.local:host-local` is projected as an active host networking
resource on macOS, Linux, and Windows. Use the virtual network's
`Reconcile endpoint mappings` action to start a local TCP proxy from
`localhost:5292` to `localhost:5291`.

The virtual network's `Reconcile endpoint mappings` action is wired to the
provider-owned graph endpoint-mapping reconciler. The reconciler projects the
resolved graph resources into the Resource Manager endpoint-mapping provisioner
contract, then delegates physical proxy materialization to the Control Plane
local host networking provisioner. This keeps endpoint-mapping orchestration in
the provider boundary while host proxy runtime behavior remains owned by the
Control Plane runtime.

The sample now declares only Resource Definitions-backed resources. The old
direct Resource Manager comparison path and sample-local endpoint-mapping
bridge have been removed. The sample validates that
`cloudshell.hostNetworking.local:host-local`,
`application.dotnet-app:vnet-api`, and
`cloudshell.virtualNetwork:sample-vnet` can start the API and materialize the
public ingress without the old application-provider aggregate.

The DNS zone's `Reconcile name mappings` action writes a CoreDNS `Corefile`
and `cloudshell.hosts` file. This is a manual local proof of the network-level
model: services can each claim port `80` because they have different private
virtual-network IP addresses, and DNS names point at those IPs instead of
mutating the developer machine's hosts file.

This provider is the portable MVP baseline. It does not create OS-native
virtual adapters, firewall rules, NAT rules, or network isolation. Future
OS-specific providers can materialize the same endpoint mapping model through
native Linux or Windows networking facilities when those capabilities are
available.

## Porting Status

- Ported: local host networking, ASP.NET Core target API, virtual-network
  endpoint/mapping declaration, Resource Manager projection, reconcile handoff
  through the provider-owned graph endpoint-mapping reconciler to the existing
  local host endpoint-mapping provisioner, and removal of the old provider
  comparison branch. Smoke coverage starts the API, executes the reconcile
  action through the Control Plane API, and verifies the public ingress reaches
  the API health endpoint without old provider records.
- Remaining: UI registration/update flow, live mapping count projection,
  runtime diagnostics, and later isolation of OS-specific networking behavior
  behind Resource Manager/runtime boundaries.
