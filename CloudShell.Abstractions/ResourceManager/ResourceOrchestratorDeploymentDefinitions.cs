using System.Globalization;
using System.Text.Json;

namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceOrchestratorDeploymentDefinitionTypes
{
    public const string Service = "cloudshell.orchestrator.service";

    public const string ReplicaGroup = "cloudshell.replica-group";
}

public sealed record ResourceOrchestratorDeploymentDefinition(
    string DefinitionVersion,
    IReadOnlyList<ResourceOrchestratorServiceDefinition>? Services = null,
    IReadOnlyList<ResourceOrchestratorResourceDefinition>? Resources = null)
{
    public const string CurrentDefinitionVersion = "1";

    public IReadOnlyList<ResourceOrchestratorServiceDefinition> DeploymentServices => Services ?? [];

    public IReadOnlyList<ResourceOrchestratorResourceDefinition> DeploymentResources => Resources ?? [];

    public static ResourceOrchestratorDeploymentDefinition FromService(
        ResourceOrchestratorService service,
        string workloadVersion,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadVersion);

        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        var replicaAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentWorkloadVersion] = workloadVersion,
            [ResourceAttributeNames.DeploymentRequestedReplicas] =
                service.Replicas.ToString(CultureInfo.InvariantCulture)
        };

        return new ResourceOrchestratorDeploymentDefinition(
            CurrentDefinitionVersion,
            Services:
            [
                new ResourceOrchestratorServiceDefinition(
                    service.Name,
                    ResourceOrchestratorDeploymentDefinitionTypes.Service,
                    CurrentDefinitionVersion,
                    Attributes: attributes,
                    Resources:
                    [
                        new ResourceOrchestratorResourceDefinition(
                            replicaGroup.Id,
                            ResourceOrchestratorDeploymentDefinitionTypes.ReplicaGroup,
                            CurrentDefinitionVersion,
                            Attributes: replicaAttributes)
                    ])
            ]);
    }
}

public sealed record ResourceOrchestratorServiceDefinition(
    string Name,
    string Type,
    string DefinitionVersion,
    JsonElement? Definition = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<ResourceOrchestratorResourceDefinition>? Resources = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ServiceAttributes => Attributes ?? EmptyAttributes;

    public IReadOnlyList<ResourceOrchestratorResourceDefinition> ServiceResources => Resources ?? [];
}

public sealed record ResourceOrchestratorResourceDefinition(
    string Name,
    string Type,
    string DefinitionVersion,
    JsonElement? Definition = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ResourceAttributes => Attributes ?? EmptyAttributes;
}
