using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceRuntimeOperations
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
}
