using System.Globalization;

namespace CloudShell.Abstractions.ResourceManager;

/// <summary>
/// Represents the endpoint a resource exposes. The endpoint name, protocol,
/// and target port describe the resource-owned contract. Concrete addresses
/// are projected through <see cref="ResourceEndpointNetworkMapping"/>.
/// </summary>
public sealed record ResourceEndpoint(
    string Name,
    string Protocol,
    ResourceExposureScope Exposure = ResourceExposureScope.Local,
    int? TargetPort = null)
{
    [Obsolete("Use ResourceEndpoint.Contract or protocol-specific factories with ResourceExposureScope.")]
    public ResourceEndpoint(
        string name,
        string address,
        string protocol,
        bool isExternal)
        : this(
            name,
            protocol,
            isExternal ? ResourceExposureScope.Public : ResourceExposureScope.Local,
            TryGetPort(address, out var port) ? port : null)
    {
    }

    public bool IsExternal =>
        Exposure is ResourceExposureScope.Network or ResourceExposureScope.Public;

    public bool TryGetPort(out int port)
    {
        if (TargetPort is { } targetPort)
        {
            port = targetPort;
            return true;
        }

        port = 0;
        return false;
    }

    public static bool TryGetUri(string? address, out Uri uri)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var parsed) &&
            !string.IsNullOrWhiteSpace(parsed.Host))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    public static bool TryGetPort(string? address, out int port)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Port > 0)
        {
            port = uri.Port;
            return true;
        }

        var separatorIndex = address?.LastIndexOf(':') ?? -1;
        if (separatorIndex >= 0 &&
            int.TryParse(
                address.AsSpan(separatorIndex + 1),
                CultureInfo.InvariantCulture,
                out port))
        {
            return true;
        }

        port = 0;
        return false;
    }

    public static ResourceEndpoint FromAddress(
        string name,
        string address,
        string protocol,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        int? targetPort = null) =>
        new(name, protocol, exposure, targetPort ?? (TryGetPort(address, out var port) ? port : null));

    public static ResourceEndpoint Contract(
        string name,
        string protocol,
        ResourceExposureScope exposure = ResourceExposureScope.Private,
        int? targetPort = null) =>
        new(name, protocol, exposure, targetPort);

    public static ResourceEndpoint Http(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        int? targetPort = null) =>
        Contract(name, "http", exposure, targetPort ?? port);

    public static ResourceEndpoint Https(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        int? targetPort = null) =>
        Contract(name, "https", exposure, targetPort ?? port);

    public static ResourceEndpoint Tcp(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        int? targetPort = null) =>
        Contract(name, "tcp", exposure, targetPort ?? port);

    public static ResourceEndpoint Udp(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        int? targetPort = null) =>
        Contract(name, "udp", exposure, targetPort ?? port);

    public static ResourceEndpoint Logical(
        string name,
        string address,
        string protocol = "logical",
        ResourceExposureScope exposure = ResourceExposureScope.Private,
        int? targetPort = null) =>
        FromAddress(name, address, protocol, exposure, targetPort);
}
