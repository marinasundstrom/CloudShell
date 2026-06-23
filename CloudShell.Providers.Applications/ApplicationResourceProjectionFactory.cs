using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceProjectionFactory(
    IApplicationRuntimeStateStore runtimeStates,
    Func<ApplicationResourceDefinition, ResourceState, bool, ResourceOrchestratorDeployment> createContainerDeployment)
{
    public IReadOnlyDictionary<string, string> CreateAttributes(
        ApplicationResourceDefinition application,
        ResourceState state,
        ApplicationResourceProjection projection)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.WorkloadKind] = projection.GetWorkloadKind(application),
            [ResourceAttributeNames.EndpointCount] = application.EndpointPorts.Count.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.VolumeMountCount] = application.VolumeMounts.Count.ToString(CultureInfo.InvariantCulture)
        };

        if (string.Equals(application.ResourceType, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            attributes[ResourceAttributeNames.DatabaseCount] =
                application.SqlDatabases.Count.ToString(CultureInfo.InvariantCulture);
        }

        AddVolumeMountAttributes(attributes, application, state);
        AddProjectAttributes(attributes, application);
        AddContainerAttributes(attributes, application, state);
        AddExecutableAttributes(attributes, application);

        return attributes;
    }

    public static IReadOnlyList<ResourceCapability> CreateCapabilities(
        ApplicationResourceDefinition application,
        IReadOnlyList<ResourceEndpoint> endpoints)
    {
        var capabilities = new List<ResourceCapability>
        {
            new(ResourceCapabilityIds.EnvironmentVariables),
            new(ResourceCapabilityIds.LogSources),
            new(ResourceCapabilityIds.Monitoring)
        };

        if (endpoints.Count > 0)
        {
            capabilities.Add(new(ResourceCapabilityIds.EndpointSource));
        }

        if (application.VolumeMounts.Count > 0)
        {
            capabilities.Add(new(ResourceCapabilityIds.StorageVolumeConsumer));
        }

        return capabilities;
    }

    private void AddVolumeMountAttributes(
        IDictionary<string, string> attributes,
        ApplicationResourceDefinition application,
        ResourceState state)
    {
        if (application.VolumeMounts.Count == 0)
        {
            return;
        }

        var runtimeMounts = runtimeStates.Get(application.Id)?.RuntimeVolumeMounts ?? [];
        var materializedCount = runtimeMounts.Count(mount =>
            string.Equals(
                mount.Status,
                ResourceVolumeMountMaterializationStatus.Materialized,
                StringComparison.OrdinalIgnoreCase));
        attributes[ResourceAttributeNames.VolumeMountMaterializedCount] =
            materializedCount.ToString(CultureInfo.InvariantCulture);
        attributes[ResourceAttributeNames.VolumeMountMaterializationStatus] =
            GetVolumeMountMaterializationStatus(
                application,
                state,
                runtimeMounts,
                materializedCount);
    }

    private static void AddProjectAttributes(
        IDictionary<string, string> attributes,
        ApplicationResourceDefinition application)
    {
        if (!IsProjectBacked(application))
        {
            return;
        }

        AddIfNotEmpty(attributes, ResourceAttributeNames.ProjectPath, application.ProjectPath);
        AddIfNotEmpty(attributes, ResourceAttributeNames.ProjectArguments, application.ProjectArguments);
        attributes[ResourceAttributeNames.ProjectHotReload] =
            application.AspNetCoreHotReload.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
    }

    private void AddContainerAttributes(
        IDictionary<string, string> attributes,
        ApplicationResourceDefinition application,
        ResourceState state)
    {
        if (!IsContainerBacked(application))
        {
            return;
        }

        var deployment = createContainerDeployment(
            application,
            state,
            true);
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(deployment.Spec.Service);
        var materializedReplicas = IsReplicaModeEnabled(application)
            ? replicaGroup.MaterializedReplicas
            : 0;
        var materializedReplicaSlots = IsReplicaModeEnabled(application)
            ? replicaGroup.Slots.Count
            : 0;

        attributes[ResourceAttributeNames.ContainerReplicas] =
            Math.Max(1, application.Replicas).ToString(CultureInfo.InvariantCulture);
        attributes[ResourceAttributeNames.ContainerReplicasEnabled] =
            IsReplicaModeEnabled(application).ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerImage, application.ContainerImage);
        attributes[ResourceAttributeNames.ContainerRegistry] = GetEffectiveContainerRegistry(application);
        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerBuildContext, application.ContainerBuildContext);
        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerDockerfile, application.ContainerDockerfile);
        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerHostId, application.ContainerHostId);
        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerRevision, deployment.Spec.WorkloadVersion);
        attributes[ResourceAttributeNames.DeploymentId] = deployment.Id;
        attributes[ResourceAttributeNames.DeploymentServiceId] = deployment.ServiceId;
        attributes[ResourceAttributeNames.DeploymentStatus] = ToAttributeValue(deployment.Status);
        attributes[ResourceAttributeNames.DeploymentRevision] = deployment.RevisionId;
        AddIfNotEmpty(
            attributes,
            ResourceAttributeNames.DeploymentEnvironmentRevisionId,
            application.DeploymentEnvironmentRevisionId);
        attributes[ResourceAttributeNames.DeploymentWorkloadVersion] = deployment.Spec.WorkloadVersion;
        attributes[ResourceAttributeNames.DeploymentRequestedReplicaSlots] =
            replicaGroup.RequestedReplicaSlots.ToString(CultureInfo.InvariantCulture);
        attributes[ResourceAttributeNames.DeploymentReplicaSlots] =
            materializedReplicaSlots.ToString(CultureInfo.InvariantCulture);
        attributes[ResourceAttributeNames.DeploymentReplicaCount] =
            materializedReplicas.ToString(CultureInfo.InvariantCulture);
        attributes[ResourceAttributeNames.DeploymentRequestedReplicas] =
            deployment.Spec.Service.Replicas.ToString(CultureInfo.InvariantCulture);
        attributes[ResourceAttributeNames.DeploymentMaterializedReplicas] =
            materializedReplicas.ToString(CultureInfo.InvariantCulture);
        attributes[ResourceAttributeNames.DeploymentProjectedReplicas] =
            materializedReplicas.ToString(CultureInfo.InvariantCulture);
        attributes[ResourceAttributeNames.DeploymentReplicaGroupId] = replicaGroup.Id;
        var replicaManagementPolicy = replicaGroup.EffectiveManagementPolicy;
        attributes[ResourceAttributeNames.DeploymentReplicaRestartMode] =
            replicaManagementPolicy.RestartMode.ToString();
        attributes[ResourceAttributeNames.DeploymentReplicaFailureThreshold] =
            replicaManagementPolicy.FailureThreshold.ToString(CultureInfo.InvariantCulture);
        attributes[ResourceAttributeNames.DeploymentReplicaMaxAttempts] =
            replicaManagementPolicy.MaxAttempts.ToString(CultureInfo.InvariantCulture);
    }

    private static void AddExecutableAttributes(
        IDictionary<string, string> attributes,
        ApplicationResourceDefinition application)
    {
        if (IsProjectBacked(application) || IsContainerBacked(application))
        {
            return;
        }

        AddIfNotEmpty(attributes, ResourceAttributeNames.ExecutablePath, application.ExecutablePath);
        AddIfNotEmpty(attributes, ResourceAttributeNames.ExecutableArguments, application.Arguments);
        AddIfNotEmpty(attributes, ResourceAttributeNames.WorkingDirectory, application.WorkingDirectory);
    }

    private static string GetVolumeMountMaterializationStatus(
        ApplicationResourceDefinition application,
        ResourceState state,
        IReadOnlyList<ResourceVolumeMountMaterialization> runtimeMounts,
        int materializedCount)
    {
        if (application.VolumeMounts.Count == 0)
        {
            return "notApplicable";
        }

        if (materializedCount == application.VolumeMounts.Count)
        {
            return "materialized";
        }

        if (materializedCount > 0)
        {
            return "partial";
        }

        if (runtimeMounts.Count > 0)
        {
            return "notActive";
        }

        return state == ResourceState.Running && IsContainerBacked(application)
            ? "unknown"
            : "notActive";
    }

    private static void AddIfNotEmpty(
        IDictionary<string, string> attributes,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes[name] = value.Trim();
        }
    }

    private static bool IsProjectBacked(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsAspNetCoreProject(application.ResourceType) ||
        !string.IsNullOrWhiteSpace(application.ProjectPath);

    private static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        ApplicationResourceProjectionSupport.IsContainerBacked(application);

    private static bool IsReplicaModeEnabled(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        application.ReplicasEnabled;

    private static string GetEffectiveContainerRegistry(ApplicationResourceDefinition application) =>
        string.IsNullOrWhiteSpace(application.ContainerRegistry)
            ? ContainerRegistryDefaults.Default
            : application.ContainerRegistry.Trim();

    private static string ToAttributeValue(ResourceOrchestratorDeploymentStatus status) =>
        status.ToString()[..1].ToLowerInvariant() + status.ToString()[1..];
}
