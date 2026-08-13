using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Options;
using ControlPlaneResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalDockerContainerApplicationReplicaSlotMaterializationProvider(
    IContainerHostRuntime containerHostRuntime,
    IOptions<LocalDockerContainerApplicationRuntimeOptions> options) : IResourceReplicaSlotMaterializationProvider
{
    private readonly LocalDockerContainerApplicationRuntimeOptions options = options.Value;

    public bool CanGetMaterializedReplicaSlots(
        ControlPlaneResource resource,
        ResourceOrchestratorReplicaGroup replicaGroup) =>
        string.Equals(
            resource.EffectiveTypeId,
            ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase) &&
        options.TryGetApplication(resource.Id, out _);

    public async Task<IReadOnlySet<int>> GetMaterializedReplicaSlotsAsync(
        ControlPlaneResource resource,
        ResourceOrchestratorReplicaGroup replicaGroup,
        CancellationToken cancellationToken = default)
    {
        if (!options.TryGetApplication(resource.Id, out var definition))
        {
            return new HashSet<int>();
        }

        var slots = new HashSet<int>();
        foreach (var slot in replicaGroup.Slots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var containerName = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(
                definition,
                slot.Ordinal);
            var result = await containerHostRuntime.InspectContainerAsync(
                new(resource.Id, containerName),
                cancellationToken);
            if (result.Container?.State == ContainerHostContainerState.Running)
            {
                slots.Add(slot.Ordinal);
            }
        }

        return slots;
    }
}
