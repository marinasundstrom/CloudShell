using Microsoft.Extensions.Configuration;
using System.Globalization;

internal static class ReplicatedContainerHealthRuntimeConventions
{
    public const string ApiResourceId = "application.container-app:api";

    public static string CreateReplicaContainerName(int replica) =>
        $"cloudshell-replicated-health-api-replica-{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static string CreateIngressContainerName() =>
        "cloudshell-replicated-health-api-ingress";

    public static string CreateReplicaResourceId(int replica) =>
        $"runtime-container:application-container-app-api:replica-{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static string CreateReplicaNetworkAlias(int replica) =>
        $"cloudshell-replicated-health-api-replica-{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static int ResolveReplicaProbePort(
        IConfiguration configuration,
        int replica,
        int mainEndpointPort)
    {
        var configuredStart = configuration.GetValue<int?>("ReplicatedContainerHealth:RuntimeProbePortStart");
        var start = configuredStart is > 0
            ? configuredStart.Value
            : Math.Max(1, mainEndpointPort + 100);

        return start + Math.Max(1, replica) - 1;
    }
}
