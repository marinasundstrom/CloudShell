using System.Globalization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public interface IHostLocalNetworkEnvironment
{
    string DefaultHost { get; }

    ResourceEndpointNetworkMapping ResolveNetworkEndpoint(
        string networkId,
        ResourceEndpointRequest request,
        int autoLocalPortStart,
        int autoLocalPortEnd);

    ResourceEndpointNetworkMapping ResolveServiceEndpoint(
        string serviceId,
        ServicePort port,
        int autoLocalPortStart,
        int autoLocalPortEnd);
}

public sealed class HostLocalNetworkEnvironment : IHostLocalNetworkEnvironment
{
    public string DefaultHost => "localhost";

    public ResourceEndpointNetworkMapping ResolveNetworkEndpoint(
        string networkId,
        ResourceEndpointRequest request,
        int autoLocalPortStart,
        int autoLocalPortEnd)
    {
        var protocol = request.ProtocolName;
        var host = FirstNonEmpty(request.IPAddress, request.Host, DefaultHost)!;
        var port = request.Port ??
            (request.Assignment is ResourceEndpointAssignment.Auto or ResourceEndpointAssignment.ProviderDefault
                ? AssignLocalPort(networkId, request.Name, autoLocalPortStart, autoLocalPortEnd)
                : null);
        var address = port is null
            ? $"{protocol}://{host}"
            : $"{protocol}://{host}:{port.Value.ToString(CultureInfo.InvariantCulture)}";

        return new ResourceEndpointNetworkMapping(
            $"{networkId}:endpoint-network-mapping:{request.Name}",
            request.Name,
            new ResourceEndpointReference(networkId, request.Name),
            address,
            request.Exposure,
            NetworkResourceId: networkId,
            SourceEndpointName: request.Name);
    }

    public ResourceEndpointNetworkMapping ResolveServiceEndpoint(
        string serviceId,
        ServicePort port,
        int autoLocalPortStart,
        int autoLocalPortEnd)
    {
        var host = FirstNonEmpty(port.IPAddress, port.Host, DefaultHost)!;
        var exposedPort = port.Port ??
            AssignLocalPort(serviceId, port.Name, autoLocalPortStart, autoLocalPortEnd);
        return new ResourceEndpointNetworkMapping(
            $"{serviceId}:endpoint-network-mapping:{port.Name}",
            port.Name,
            new ResourceEndpointReference(serviceId, port.Name),
            $"{port.Protocol}://{host}:{exposedPort.ToString(CultureInfo.InvariantCulture)}",
            port.Exposure,
            SourceEndpointName: port.Name);
    }

    private static int AssignLocalPort(
        string resourceId,
        string endpointName,
        int autoLocalPortStart,
        int autoLocalPortEnd)
    {
        var start = Math.Max(1, autoLocalPortStart);
        var end = Math.Max(start, autoLocalPortEnd);
        var range = end - start + 1;
        return start + (int)(StableHash($"{resourceId}:{endpointName}") % (uint)range);
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
