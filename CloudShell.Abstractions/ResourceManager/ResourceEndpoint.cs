namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceEndpoint(
    string Name,
    string Address,
    string Protocol,
    ResourceExposureScope Exposure = ResourceExposureScope.Local)
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
        ResourceExposureScope exposure = ResourceExposureScope.Local) =>
        new(name, address, protocol, exposure);

    public static ResourceEndpoint Http(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local) =>
        FromAddress(name, $"http://{host}:{port}", "http", exposure);

    public static ResourceEndpoint Https(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local) =>
        FromAddress(name, $"https://{host}:{port}", "https", exposure);

    public static ResourceEndpoint Tcp(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local) =>
        FromAddress(name, $"tcp://{host}:{port}", "tcp", exposure);

    public static ResourceEndpoint Udp(
        string name,
        string host,
        int port,
        ResourceExposureScope exposure = ResourceExposureScope.Local) =>
        FromAddress(name, $"udp://{host}:{port}", "udp", exposure);

    public static ResourceEndpoint Logical(
        string name,
        string address,
        string protocol = "logical",
        ResourceExposureScope exposure = ResourceExposureScope.Private) =>
        FromAddress(name, address, protocol, exposure);
}
