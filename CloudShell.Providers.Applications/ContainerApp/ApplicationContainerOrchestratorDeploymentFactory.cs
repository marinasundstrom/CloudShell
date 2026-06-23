using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationContainerOrchestratorDeploymentFactory(
    ApplicationContainerRevisionService? revisions = null,
    string defaultNetworkName = "cloudshell",
    string defaultOrchestratorId = "default")
{
    private readonly ApplicationContainerRevisionService revisions = revisions ?? new();

    public ResourceOrchestratorService CreateService(
        ApplicationResourceDefinition application,
        ResourceWorkloadConfiguration workload) =>
        new(
            application.Id,
            CreateServiceName(application.Id),
            workload,
            Networks: [defaultNetworkName],
            ReplicaManagementPolicy: application.ReplicaManagementPolicy);

    public ResourceOrchestratorDeployment CreateDeployment(
        ApplicationResourceDefinition application,
        ResourceState state,
        ResourceWorkloadConfiguration workload,
        bool useRuntimeRevisionScopedInstances = false)
    {
        var service = CreateService(application, workload);
        var revision = revisions.GetEffectiveRevision(application);
        if (useRuntimeRevisionScopedInstances)
        {
            service = service with
            {
                RuntimeRevisionId = revision
            };
        }

        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentRequestedReplicaSlots] =
                service.Replicas.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.DeploymentRequestedReplicas] =
                service.Replicas.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.ContainerRegistry] = GetEffectiveContainerRegistry(application)
        };

        AddIfNotEmpty(inputs, ResourceAttributeNames.ContainerImage, application.ContainerImage);

        return new ResourceOrchestratorDeployment(
            CreateDeploymentId(application.Id),
            defaultOrchestratorId,
            application.Id,
            service.Name,
            revision,
            new ResourceOrchestratorDeploymentSpec(service, revision, inputs),
            GetDeploymentStatus(state));
    }

    public static string CreateServiceName(string resourceId) =>
        ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(resourceId);

    public static string CreateDeploymentId(string resourceId) =>
        $"{CreateServiceName(resourceId)}-deployment";

    public static ResourceOrchestratorDeploymentStatus GetDeploymentStatus(
        ResourceState state) =>
        state switch
        {
            ResourceState.Starting or ResourceState.Stopping => ResourceOrchestratorDeploymentStatus.Applying,
            ResourceState.Running => ResourceOrchestratorDeploymentStatus.Active,
            ResourceState.Degraded => ResourceOrchestratorDeploymentStatus.Failed,
            _ => ResourceOrchestratorDeploymentStatus.Pending
        };

    private static string GetEffectiveContainerRegistry(ApplicationResourceDefinition application) =>
        string.IsNullOrWhiteSpace(application.ContainerRegistry)
            ? ContainerRegistryDefaults.Default
            : application.ContainerRegistry.Trim();

    private static void AddIfNotEmpty(
        IDictionary<string, string> values,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[name] = value.Trim();
        }
    }
}
