using System.Globalization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Networking;

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
        var port = request.Port ?? request.Assignment switch
        {
            ResourceEndpointAssignment.Auto =>
                request.TargetPort ?? AssignLocalPort(networkId, request.Name, autoLocalPortStart, autoLocalPortEnd),
            ResourceEndpointAssignment.ProviderDefault => request.TargetPort,
            _ => null
        };
        var address = port is null
            ? $"{protocol}://{host}"
            : $"{protocol}://{host}:{port.Value.ToString(CultureInfo.InvariantCulture)}";

        return ResourceEndpointNetworkMapping.ForEndpoint(
            networkId,
            request.Name,
            address,
            request.Exposure,
            networkResourceId: networkId,
            sourceEndpointName: request.Name);
    }

    public ResourceEndpointNetworkMapping ResolveServiceEndpoint(
        string serviceId,
        ServicePort port,
        int autoLocalPortStart,
        int autoLocalPortEnd)
    {
        var host = FirstNonEmpty(port.IPAddress, port.Host, DefaultHost)!;
        var exposedPort = port.Port ?? port.Assignment switch
        {
            ResourceEndpointAssignment.Auto =>
                port.TargetPort > 0
                    ? port.TargetPort
                    : AssignLocalPort(serviceId, port.Name, autoLocalPortStart, autoLocalPortEnd),
            ResourceEndpointAssignment.ProviderDefault => port.TargetPort,
            _ => null
        };
        var address = exposedPort is null
            ? $"{port.Protocol}://{host}"
            : $"{port.Protocol}://{host}:{exposedPort.Value.ToString(CultureInfo.InvariantCulture)}";
        return ResourceEndpointNetworkMapping.ForEndpoint(
            serviceId,
            port.Name,
            address,
            port.Exposure,
            networkResourceId: port.NetworkResourceId,
            sourceEndpointName: port.Name);
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
