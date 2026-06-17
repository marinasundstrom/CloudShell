using System.Globalization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public interface IHostLocalNetworkEnvironment
{
    string DefaultHost { get; }

    ResourceEndpoint ResolveNetworkEndpoint(
        string networkId,
        ResourceEndpointRequest request,
        int autoLocalPortStart,
        int autoLocalPortEnd);

    ResourceEndpoint ResolveServiceEndpoint(
        string serviceId,
        ServicePort port,
        int autoLocalPortStart,
        int autoLocalPortEnd);
}

public sealed class HostLocalNetworkEnvironment : IHostLocalNetworkEnvironment
{
    public string DefaultHost => "localhost";

    public ResourceEndpoint ResolveNetworkEndpoint(
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

        return ResourceEndpoint.FromAddress(
            request.Name,
            address,
            protocol,
            request.Exposure,
            request.TargetPort);
    }

    public ResourceEndpoint ResolveServiceEndpoint(
        string serviceId,
        ServicePort port,
        int autoLocalPortStart,
        int autoLocalPortEnd)
    {
        var host = FirstNonEmpty(port.IPAddress, port.Host, DefaultHost)!;
        var exposedPort = port.Port ??
            AssignLocalPort(serviceId, port.Name, autoLocalPortStart, autoLocalPortEnd);
        return ResourceEndpoint.FromAddress(
            port.Name,
            $"{port.Protocol}://{host}:{exposedPort.ToString(CultureInfo.InvariantCulture)}",
            port.Protocol,
            port.Exposure,
            port.TargetPort);
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
