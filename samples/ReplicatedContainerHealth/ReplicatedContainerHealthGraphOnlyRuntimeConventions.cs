using Microsoft.Extensions.Configuration;
using System.Globalization;

internal static class ReplicatedContainerHealthGraphOnlyRuntimeConventions
{
    public const string GraphApiResourceId = "application.container-app:graph-api";

    public static string CreateReplicaContainerName(int replica) =>
        $"cloudshell-replicated-health-graph-api-replica-{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static string CreateIngressContainerName() =>
        "cloudshell-replicated-health-graph-api-ingress";

    public static string CreateReplicaResourceId(int replica) =>
        $"runtime-container:application-container-app-graph-api:replica-{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static string CreateReplicaNetworkAlias(int replica) =>
        $"cloudshell-replicated-health-graph-api-replica-{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static int ResolveReplicaProbePort(
        IConfiguration configuration,
        int replica,
        int mainEndpointPort)
    {
        var configuredStart = configuration.GetValue<int?>("ReplicatedContainerHealth:GraphOnlyProbePortStart");
        var start = configuredStart is > 0
            ? configuredStart.Value
            : Math.Max(1, mainEndpointPort + 100);

        return start + Math.Max(1, replica) - 1;
    }
}
