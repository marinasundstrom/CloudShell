using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Options;
using ControlPlaneResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalDockerContainerApplicationReplicaSlotMaterializationProvider(
    ILocalContainerApplicationCommandRunner commandRunner,
    IOptions<LocalDockerContainerApplicationRuntimeOptions> options) : IResourceReplicaSlotMaterializationProvider
{
    private const string RunningStatus = "running";
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
            var result = await commandRunner.RunAsync(
                "docker",
                [
                    "container",
                    "inspect",
                    "--format",
                    "{{.State.Status}}\t{{ index .Config.Labels \"cloudshell.replica-group-id\" }}",
                    containerName
                ],
                cancellationToken,
                throwOnError: false);

            if (result.ExitCode != 0)
            {
                continue;
            }

            var parts = result.Output
                .Trim()
                .Split('\t', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 &&
                string.Equals(parts[0], RunningStatus, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parts[1], replicaGroup.Id, StringComparison.OrdinalIgnoreCase))
            {
                slots.Add(slot.Ordinal);
            }
        }

        return slots;
    }
}
