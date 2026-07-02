using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GraphResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Providers;

public static class LocalDockerContainerApplicationRuntimeConventions
{
    public const string ApiResourceId = "application.container-app:api";

    public static readonly LocalDockerContainerApplicationRuntimeDefinition ReplicatedContainerHealthDefaults =
        new(ApiResourceId, "samples/ReplicatedContainerHealth/Api/CloudShell.ReplicatedContainerHealth.Api.csproj")
        {
            IngressContainerName = "cloudshell-replicated-health-api-ingress",
            IngressConfigurationDirectory = Path.Combine(
                "samples",
                "ReplicatedContainerHealth",
                "Data",
                "runtime-ingress"),
            ReplicaContainerNamePrefix = "cloudshell-replicated-health-api-replica-",
            ReplicaNetworkAliasPrefix = "cloudshell-replicated-health-api-replica-",
            ReplicaResourceIdPrefix = "runtime-container:application-container-app-api:replica-",
            ReplicaServiceNamePrefix = "replicated-container-health-api-replica-",
            RuntimeResourceProviderId = "replicated-container-health.runtime",
            RuntimeResourceProviderName = "Replicated Container Health runtime",
            RuntimeMaterialization = "sampleRuntime"
        };

    public static string CreateReplicaContainerName(int replica) =>
        CreateReplicaContainerName(ReplicatedContainerHealthDefaults, replica);

    public static string CreateReplicaContainerName(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        int replica) =>
        $"{definition.ReplicaContainerNamePrefix}{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static string CreateIngressContainerName() =>
        ReplicatedContainerHealthDefaults.IngressContainerName;

    public static string CreateIngressContainerName(
        LocalDockerContainerApplicationRuntimeDefinition definition) =>
        definition.IngressContainerName;

    public static string CreateReplicaResourceId(int replica) =>
        CreateReplicaResourceId(ReplicatedContainerHealthDefaults, replica);

    public static string CreateReplicaResourceId(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        int replica) =>
        $"{definition.ReplicaResourceIdPrefix}{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static string CreateReplicaNetworkAlias(int replica) =>
        CreateReplicaNetworkAlias(ReplicatedContainerHealthDefaults, replica);

    public static string CreateReplicaNetworkAlias(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        int replica) =>
        $"{definition.ReplicaNetworkAliasPrefix}{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static int ResolveReplicaProbePort(
        IConfiguration configuration,
        int replica,
        int mainEndpointPort) =>
        ResolveReplicaProbePort(
            ReplicatedContainerHealthDefaults,
            configuration,
            replica,
            mainEndpointPort);

    public static int ResolveReplicaProbePort(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        IConfiguration configuration,
        int replica,
        int mainEndpointPort)
    {
        var configuredStart = definition.ReplicaProbePortStart ??
            configuration.GetValue<int?>("ReplicatedContainerHealth:RuntimeProbePortStart");
        var start = configuredStart is > 0
            ? configuredStart.Value
            : Math.Max(1, mainEndpointPort + 100);

        return start + Math.Max(1, replica) - 1;
    }

    public static string ResolveReplicaGroupId(GraphResource resource) =>
        ResourceOrchestratorReplicaGroups.CreateReplicaGroupId(
            ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(resource.EffectiveResourceId),
            ResolveRuntimeRevisionId(resource));

    public static string ResolveRuntimeRevisionId(GraphResource resource)
    {
        var registry = resource.Attributes.GetString(ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry);
        if (string.IsNullOrWhiteSpace(registry))
        {
            registry = ContainerRegistryDefaults.Default;
        }

        var image = resource.Attributes.GetString(ContainerApplicationResourceTypeProvider.Attributes.ContainerImage)
            ?? throw new InvalidOperationException(
                "The container app image must be set before sample runtime can resolve the runtime revision.");
        var revisionKey = $"{registry.Trim()}\n{image.Trim()}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(revisionKey), hash);
        return $"rev-img-{Convert.ToHexString(hash[..6]).ToLowerInvariant()}";
    }
}
