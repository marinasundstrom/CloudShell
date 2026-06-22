using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private int ResolveLocalPort(string resourceId, ServicePort port)
    {
        if (port.Port is not null)
        {
            return Math.Max(1, port.Port.Value);
        }

        var start = Math.Max(1, options.AutoLocalPortStart);
        var end = Math.Max(start, options.AutoLocalPortEnd);
        var range = end - start + 1;
        return start + (int)(StableHash($"{resourceId}:{port.Name}") % (uint)range);
    }

    private int ResolveReplicaProbeLocalPort(
        string resourceId,
        ServicePort port,
        int replicaOrdinal)
    {
        var start = Math.Max(1, options.AutoLocalPortStart);
        var end = Math.Max(start, options.AutoLocalPortEnd);
        var range = end - start + 1;
        var normalizedReplicaOrdinal = Math.Max(1, replicaOrdinal);
        return start + (int)(StableHash(
            $"{resourceId}:replica:{normalizedReplicaOrdinal.ToString(CultureInfo.InvariantCulture)}:{port.Name}") %
            (uint)range);
    }

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
