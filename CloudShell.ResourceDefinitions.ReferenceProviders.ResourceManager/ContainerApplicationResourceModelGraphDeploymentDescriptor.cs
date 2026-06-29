using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;

public sealed class ContainerApplicationResourceModelGraphDeploymentDescriptor :
    IResourceModelGraphDeploymentDescriptor
{
    private const string DefaultOrchestratorId = "default";
    private const string DefaultNetworkName = "cloudshell";

    public bool CanDescribeDeployment(
        ResourceManagerResource resource,
        Resource graphResource) =>
        graphResource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId;

    public async ValueTask<ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
        ResourceModelGraphDeploymentDescriptionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var container = new ContainerApplicationResource(context.GraphResource);
        var image = container.Image;
        if (string.IsNullOrWhiteSpace(image))
        {
            return null;
        }

        var revisionId = CreateRuntimeRevisionId(container);
        var service = new ResourceOrchestratorService(
            context.GraphResource.EffectiveResourceId,
            ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(
                context.GraphResource.EffectiveResourceId),
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                context.GraphResource.Name,
                Image: image.Trim(),
                Registry: string.IsNullOrWhiteSpace(container.Registry)
                    ? ContainerRegistryDefaults.Default
                    : container.Registry.Trim(),
                ContainerHostId: container.ContainerHostResourceId,
                Replicas: container.Replicas,
                ReplicasEnabled: container.Replicas > 1,
                Ports: ToServicePorts(container.EndpointRequests),
                VolumeMounts: ToVolumeMounts(await container.GetVolumesAsync(cancellationToken))),
            DependsOn: context.GraphResource.State.ResourceDependencyIds,
            Networks: [DefaultNetworkName])
        {
            RuntimeRevisionId = revisionId
        };
        var inputs = CreateDeploymentInputs(container, revisionId);
        var deployment = new ResourceOrchestratorDeployment(
            $"{service.Name}-deployment",
            DefaultOrchestratorId,
            context.GraphResource.EffectiveResourceId,
            service.Name,
            revisionId,
            new ResourceOrchestratorDeploymentSpec(
                service,
                revisionId,
                inputs),
            ToDeploymentStatus(context.Resource.State));
        var definition = deployment.Spec.CreateDeploymentDefinition(deployment.RevisionId);

        return deployment with
        {
            Spec = deployment.Spec with
            {
                Definition = definition
            }
        };
    }

    private static IReadOnlyDictionary<string, string> CreateDeploymentInputs(
        ContainerApplicationResource container,
        string revisionId)
    {
        var replicas = container.Replicas.ToString(CultureInfo.InvariantCulture);
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentRequestedReplicaSlots] = replicas,
            [ResourceAttributeNames.DeploymentRequestedReplicas] = replicas,
            [ResourceAttributeNames.ContainerRegistry] = string.IsNullOrWhiteSpace(container.Registry)
                ? ContainerRegistryDefaults.Default
                : container.Registry.Trim(),
            [ResourceAttributeNames.RuntimeRevision] = revisionId
        };

        if (!string.IsNullOrWhiteSpace(container.Image))
        {
            inputs[ResourceAttributeNames.ContainerImage] = container.Image.Trim();
        }

        return inputs;
    }

    private static IReadOnlyList<ServicePort> ToServicePorts(
        IReadOnlyList<NetworkingEndpointRequestValue> endpoints) =>
        endpoints
            .Select(endpoint =>
                new ServicePort(
                    endpoint.Name,
                    endpoint.TargetPort ?? endpoint.Port ?? 80,
                    endpoint.Port,
                    string.IsNullOrWhiteSpace(endpoint.Protocol)
                        ? "tcp"
                        : endpoint.Protocol.Trim(),
                    ParseEnum(endpoint.Exposure, ResourceExposureScope.Local),
                    ParseEnum(endpoint.Assignment, ResourceEndpointAssignment.ProviderDefault),
                    NetworkResourceId: TryGetReferenceResourceId(endpoint.Network),
                    Host: endpoint.Host))
            .ToArray();

    private static IReadOnlyList<ResourceVolumeMount> ToVolumeMounts(
        IReadOnlyList<VolumeMountDefinition> mounts) =>
        mounts
            .Select(mount =>
                new ResourceVolumeMount(
                    mount.Volume,
                    mount.TargetPath,
                    mount.ReadOnly))
            .ToArray();

    private static string CreateRuntimeRevisionId(ContainerApplicationResource container)
    {
        var registry = string.IsNullOrWhiteSpace(container.Registry)
            ? ContainerRegistryDefaults.Default
            : container.Registry.Trim();
        var image = container.Image?.Trim() ?? string.Empty;
        var revisionKey = $"{registry}\n{image}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(revisionKey), hash);
        return $"rev-img-{Convert.ToHexString(hash[..6]).ToLowerInvariant()}";
    }

    private static TEnum ParseEnum<TEnum>(
        string? value,
        TEnum fallback)
        where TEnum : struct =>
        !string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed)
                ? parsed
                : fallback;

    private static string? TryGetReferenceResourceId(ResourceReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        return reference.TryGetResourceId(out var resourceId)
                ? resourceId
                : null;
    }

    private static ResourceOrchestratorDeploymentStatus ToDeploymentStatus(
        CloudShell.Abstractions.ResourceManager.ResourceState? state) =>
        state switch
        {
            CloudShell.Abstractions.ResourceManager.ResourceState.Starting or
                CloudShell.Abstractions.ResourceManager.ResourceState.Stopping =>
                    ResourceOrchestratorDeploymentStatus.Applying,
            CloudShell.Abstractions.ResourceManager.ResourceState.Running =>
                ResourceOrchestratorDeploymentStatus.Active,
            CloudShell.Abstractions.ResourceManager.ResourceState.Degraded =>
                ResourceOrchestratorDeploymentStatus.Failed,
            _ => ResourceOrchestratorDeploymentStatus.Pending
        };
}
