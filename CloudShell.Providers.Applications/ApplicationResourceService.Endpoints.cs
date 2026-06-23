using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private int ResolveLocalPort(string resourceId, ServicePort port)
        => _ports.ResolveLocalPort(resourceId, port);

    private int ResolveReplicaProbeLocalPort(
        string resourceId,
        ServicePort port,
        ResourceOrchestratorServiceInstance instance)
        => _ports.ResolveReplicaProbeLocalPort(resourceId, port, instance);

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.Trim().ToLowerInvariant();

    private static string NormalizeContainerPublishProtocol(string? protocol) =>
        NormalizeProtocol(protocol) switch
        {
            "http" or "https" => "tcp",
            "udp" => "udp",
            "sctp" => "sctp",
            _ => "tcp"
        };
}
