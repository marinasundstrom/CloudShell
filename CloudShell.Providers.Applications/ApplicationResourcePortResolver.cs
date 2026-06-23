using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourcePortResolver(ApplicationProviderOptions options)
{
    public int ResolveLocalPort(string resourceId, ServicePort port)
    {
        if (port.Port is not null)
        {
            return Math.Max(1, port.Port.Value);
        }

        var (start, range) = GetAutoPortRange();
        return start + (int)(ApplicationResourceHash.StableHash($"{resourceId}:{port.Name}") % (uint)range);
    }

    public int ResolveReplicaProbeLocalPort(
        string resourceId,
        ServicePort port,
        ResourceOrchestratorServiceInstance instance)
    {
        var (start, range) = GetAutoPortRange();
        var normalizedReplicaOrdinal = Math.Max(1, instance.ReplicaOrdinal);
        var groupKey = string.IsNullOrWhiteSpace(instance.RuntimeRevisionId)
            ? resourceId
            : $"{resourceId}:revision:{instance.RuntimeRevisionId}";
        return start + (int)(ApplicationResourceHash.StableHash(
            $"{groupKey}:replica:{normalizedReplicaOrdinal.ToString(CultureInfo.InvariantCulture)}:{port.Name}") %
            (uint)range);
    }

    private (int Start, int Range) GetAutoPortRange()
    {
        var start = Math.Max(1, options.AutoLocalPortStart);
        var end = Math.Max(start, options.AutoLocalPortEnd);
        return (start, end - start + 1);
    }
}
