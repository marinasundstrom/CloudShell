namespace CloudShell.Abstractions.ResourceManager;

/// <summary>
/// Represents the endpoint a resource exposes. The endpoint name, protocol,
/// and target port describe the resource-owned contract. The address is kept
/// as a compatibility projection of the current endpoint-network mapping.
/// </summary>
public sealed record ResourceEndpoint(
    string Name,
    string Address,
    string Protocol,
    ResourceExposureScope Exposure = ResourceExposureScope.Local,
    int? TargetPort = null)
{
    [Obsolete("Use ResourceEndpoint.FromAddress or protocol-specific factories with ResourceExposureScope.")]
    public ResourceEndpoint(
        string name,
        string address,
        string protocol,
        bool isExternal)
        : this(
            name,
            address,
            protocol,
            isExternal ? ResourceExposureScope.Public : ResourceExposureScope.Local)
    {
    }

    public bool IsExternal =>
        Exposure is ResourceExposureScope.Network or ResourceExposureScope.Public;

    public static ResourceEndpoint FromAddress(
        string name,
        string address,
        string protocol,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        int? targetPort = null) =>
        new(name, address, protocol, exposure, targetPort);

    public static ResourceEndpoint Http(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        int? targetPort = null) =>
        FromAddress(name, $"http://{host}:{port}", "http", exposure, targetPort ?? port);

    public static ResourceEndpoint Https(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        int? targetPort = null) =>
        FromAddress(name, $"https://{host}:{port}", "https", exposure, targetPort ?? port);

    public static ResourceEndpoint Tcp(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        int? targetPort = null) =>
        FromAddress(name, $"tcp://{host}:{port}", "tcp", exposure, targetPort ?? port);

    public static ResourceEndpoint Udp(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        int? targetPort = null) =>
        FromAddress(name, $"udp://{host}:{port}", "udp", exposure, targetPort ?? port);

    public static ResourceEndpoint Logical(
        string name,
        string address,
        string protocol = "logical",
        ResourceExposureScope exposure = ResourceExposureScope.Private,
        int? targetPort = null) =>
        FromAddress(name, address, protocol, exposure, targetPort);
}
