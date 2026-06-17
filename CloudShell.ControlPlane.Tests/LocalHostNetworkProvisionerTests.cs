using System.Net;
using System.Net.Sockets;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;

namespace CloudShell.ControlPlane.Tests;

public sealed class LocalHostNetworkProvisionerTests
{
    [Fact]
    public async Task ProvisionEndpointMappingAsync_ForwardsTcpTrafficThroughLocalHostNetworking()
    {
        var sourcePort = GetFreePort();
        var targetListener = new TcpListener(IPAddress.Loopback, 0);
        targetListener.Start();
        var targetPort = ((IPEndPoint)targetListener.LocalEndpoint).Port;
        var targetTask = AcceptOneConnectionAsync(targetListener);
        await using var provisioner = new LocalHostNetworkProvisioner();

        var context = CreateContext(sourcePort, targetPort);
        Assert.True(provisioner.CanProvisionEndpointMapping(context));

        await provisioner.ProvisionEndpointMappingAsync(context);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, sourcePort);
        await using var stream = client.GetStream();
        await using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(stream, leaveOpen: true);

        await writer.WriteLineAsync("ping");
        var response = await reader.ReadLineAsync();

        Assert.Equal("pong:ping", response);
        Assert.Equal("ping", await targetTask);
    }

    [Fact]
    public async Task ProvisionEndpointMappingAsync_RequiresEndpointNetworkMappings()
    {
        var sourcePort = GetFreePort();
        var targetPort = GetFreePort();
        await using var provisioner = new LocalHostNetworkProvisioner();

        var context = CreateContext(sourcePort, targetPort, includeEndpointNetworkMappings: false);
        Assert.True(provisioner.CanProvisionEndpointMapping(context));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.ProvisionEndpointMappingAsync(context));

        Assert.Contains("must use a mapped absolute host:port address", exception.Message);
    }

    private static ResourceEndpointMappingProvisioningContext CreateContext(
        int sourcePort,
        int targetPort,
        bool includeEndpointNetworkMappings = true)
    {
        ResourceEndpoint[] networkEndpoints = includeEndpointNetworkMappings
            ? [ResourceEndpoint.Contract("public", "tcp", ResourceExposureScope.Public, sourcePort)]
            : [ResourceEndpoint.Tcp("public", "localhost", sourcePort, ResourceExposureScope.Public)];
        ResourceEndpointNetworkMapping[] networkEndpointNetworkMappings = includeEndpointNetworkMappings
            ?
            [
                new ResourceEndpointNetworkMapping(
                    "network:sample:endpoint-network-mapping:public",
                    "public",
                    new ResourceEndpointReference("network:sample", "public"),
                    $"tcp://localhost:{sourcePort}",
                    ResourceExposureScope.Public,
                    SourceEndpointName: "public")
            ]
            : [];
        ResourceEndpoint[] targetEndpoints = includeEndpointNetworkMappings
            ? [ResourceEndpoint.Contract("tcp", "tcp", ResourceExposureScope.Local, targetPort)]
            : [ResourceEndpoint.Tcp("tcp", "localhost", targetPort, ResourceExposureScope.Local)];
        ResourceEndpointNetworkMapping[] targetEndpointNetworkMappings = includeEndpointNetworkMappings
            ?
            [
                new ResourceEndpointNetworkMapping(
                    "application:api:endpoint-network-mapping:tcp",
                    "tcp",
                    new ResourceEndpointReference("application:api", "tcp"),
                    $"tcp://localhost:{targetPort}",
                    ResourceExposureScope.Local,
                    SourceEndpointName: "tcp")
            ]
            : [];
        var network = new Resource(
            "network:sample",
            "Sample Network",
            "Network",
            "CloudShell",
            "logical",
            ResourceState.Running,
            networkEndpoints,
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.VirtualNetworkResourceType,
            ResourceClass: ResourceClass.Network,
            EndpointNetworkMappings: networkEndpointNetworkMappings);
        var target = new Resource(
            "application:api",
            "API",
            "Application",
            "Applications",
            "local",
            ResourceState.Running,
            targetEndpoints,
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            EndpointNetworkMappings: targetEndpointNetworkMappings);
        var provider = new Resource(
            LocalHostNetworkProvider.ResourceId,
            "Local Host Networking",
            "Host Networking",
            "CloudShell",
            "host",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            Capabilities: [new(ResourceCapabilityIds.NetworkingEndpointMapper)]);
        var definition = new NetworkResourceDefinition(
            network.Id,
            network.Name,
            Kind: NetworkResourceKind.Virtual);
        var mapping = new ResourceEndpointMappingDefinition(
            "mapping:api",
            "API",
            new ResourceEndpointReference(network.Id, "public"),
            new ResourceEndpointReference(target.Id, "tcp"),
            network.Id,
            provider.Id);

        return new ResourceEndpointMappingProvisioningContext(
            network,
            definition,
            mapping,
            network.Endpoints.Single(),
            target,
            target.Endpoints.Single(),
            provider,
            new TestResourceManagerStore([network, target, provider]),
            network.GetEndpointNetworkMapping("public"),
            target.GetEndpointNetworkMapping("tcp"));
    }

    private static async Task<string?> AcceptOneConnectionAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        listener.Stop();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        await using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
        var request = await reader.ReadLineAsync();
        await writer.WriteLineAsync($"pong:{request}");
        return request;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class TestResourceManagerStore(IReadOnlyList<Resource> resources) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<Resource> GetAvailableResources() => resources;

        public IReadOnlyList<Resource> GetResources() => resources;

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string id) =>
            resources.FirstOrDefault(resource => string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) => GetResource(resourceId) is not null;
    }
}
