using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceMonitoringProvider(
    IApplicationResourceDefinitionSource definitions,
    LocalProcessRunner localProcesses,
    ApplicationContainerDeploymentStore containerDeployments,
    ApplicationContainerHostResolver containerHosts,
    ApplicationProviderOptions options) : IResourceMonitoringProvider
{
    private const string ProviderDisplayName = "Applications";
    private static readonly ApplicationWorkloadConfigurationFactory WorkloadConfigurationFactory = new();
    private static readonly ApplicationContainerOrchestratorDeploymentFactory ContainerOrchestratorDeploymentFactory = new();
    private static readonly ApplicationContainerRevisionService ContainerRevisionService = new();
    private static readonly ContainerApplicationRuntimeRevisionPolicy ContainerRuntimeRevisionPolicy = new();

    public bool CanMonitor(Resource resource)
    {
        if (IsRuntimeContainerReplica(resource))
        {
            return !string.IsNullOrWhiteSpace(resource.OwnerResourceId) &&
                definitions.GetApplication(resource.OwnerResourceId) is { } owner &&
                IsReplicaModeEnabled(owner);
        }

        return ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
            definitions.GetApplication(resource.Id) is not null;
    }

    public async Task<ResourceMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        if (!CanMonitor(resource))
        {
            return null;
        }

        var timestamp = DateTimeOffset.UtcNow;
        if (IsRuntimeContainerReplica(resource))
        {
            return await GetRuntimeContainerMonitoringSnapshotAsync(
                resource,
                timestamp,
                cancellationToken);
        }

        var application = definitions.GetApplication(resource.Id);
        if (application is null)
        {
            return null;
        }

        if (ApplicationResourceProjectionSupport.IsContainerBacked(application))
        {
            return await GetContainerBackedMonitoringSnapshotAsync(
                resource,
                application,
                timestamp,
                cancellationToken);
        }

        if (resource.State != ResourceState.Running)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Application process metrics are available only while the resource is running.");
        }

        var processSnapshot = await localProcesses.GetMonitoringSnapshotAsync(
            ApplicationProcessDefinitions.Create(application),
            cancellationToken);
        if (processSnapshot is null)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "The application process could not be observed.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            processSnapshot.Timestamp,
            LocalProcessMonitoringMetrics.CreateMetricSamples(processSnapshot, "application process"),
            "Available",
            "Application process metrics.");
    }

    private async Task<ResourceMonitoringSnapshot?> GetContainerBackedMonitoringSnapshotAsync(
        Resource resource,
        ApplicationResourceDefinition application,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        if (resource.State != ResourceState.Running)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Container metrics are available only while the resource is running.");
        }

        var engine = await containerHosts.ResolveStaticAsync(application, cancellationToken);
        if (engine is null)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Container metrics require a configured container host.");
        }

        var deployment = CreateDefaultContainerOrchestratorDeployment(
            application,
            resource.State ?? ResourceState.Unknown,
            runtimeRevisionScoped: true);
        var service = deployment.Spec.Service;
        if (IsReplicaModeEnabled(application))
        {
            return await GetReplicatedContainerMonitoringSnapshotAsync(
                resource,
                application,
                engine,
                timestamp,
                cancellationToken);
        }

        return await GetContainerMonitoringSnapshotAsync(
            resource,
            engine,
            GetContainerName(service),
            timestamp,
            "Container runtime metrics.",
            "The container runtime did not return a stats snapshot.",
            cancellationToken);
    }

    private async Task<ResourceMonitoringSnapshot> GetReplicatedContainerMonitoringSnapshotAsync(
        Resource resource,
        ApplicationResourceDefinition application,
        ContainerHostDescriptor engine,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var state = resource.State ?? ResourceState.Unknown;
        var deployment = CreateDefaultContainerOrchestratorDeployment(
            application,
            state,
            runtimeRevisionScoped: true);
        var metrics = new List<ResourceMetricSample>();
        var unavailableMessages = new List<string>();
        var available = 0;
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(deployment.Spec.Service);
        var instances = replicaGroup.Instances;

        foreach (var instance in instances)
        {
            var replicaResource = CreateRuntimeContainerResource(application, deployment, replicaGroup, instance, state);
            var snapshot = await GetContainerMonitoringSnapshotAsync(
                replicaResource,
                engine,
                instance.Name,
                timestamp,
                "Container replica runtime metrics.",
                "The container runtime did not return a stats snapshot for the replica.",
                cancellationToken);

            if (string.Equals(snapshot.Status, "Available", StringComparison.OrdinalIgnoreCase))
            {
                available++;
                metrics.AddRange(snapshot.Metrics.Select(metric => ApplyRuntimeContainerMetricScope(metric, instance, deployment.RevisionId)));
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.Message))
            {
                unavailableMessages.Add($"Replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)}: {snapshot.Message}");
            }
        }

        if (available == replicaGroup.MaterializedReplicas)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                DateTimeOffset.UtcNow,
                metrics,
                "Available",
                $"{available.ToString(CultureInfo.InvariantCulture)}/{replicaGroup.MaterializedReplicas.ToString(CultureInfo.InvariantCulture)} replica monitoring snapshots are available.");
        }

        if (available > 0)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                DateTimeOffset.UtcNow,
                metrics,
                "Degraded",
                $"{available.ToString(CultureInfo.InvariantCulture)}/{replicaGroup.MaterializedReplicas.ToString(CultureInfo.InvariantCulture)} replica monitoring snapshots are available.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            timestamp,
            [],
            "Unavailable",
            unavailableMessages.Count == 0
                ? "The container runtime did not return stats snapshots for any replica."
                : string.Join(' ', unavailableMessages));
    }

    private async Task<ResourceMonitoringSnapshot?> GetRuntimeContainerMonitoringSnapshotAsync(
        Resource resource,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        if (resource.State != ResourceState.Running)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Container replica metrics are available only while the replica is running.");
        }

        var owner = string.IsNullOrWhiteSpace(resource.OwnerResourceId)
            ? null
            : definitions.GetApplication(resource.OwnerResourceId);
        if (owner is null)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Container replica metrics require the owning application resource.");
        }

        var engine = await containerHosts.ResolveStaticAsync(owner, cancellationToken);
        if (engine is null)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Container replica metrics require a configured container host.");
        }

        var containerName = GetAttribute(resource, ResourceAttributeNames.RuntimeContainerName);
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Container replica metrics require a projected runtime container name.");
        }

        return await GetContainerMonitoringSnapshotAsync(
            resource,
            engine,
            containerName,
            timestamp,
            "Container replica runtime metrics.",
            "The container runtime did not return a stats snapshot for the replica.",
            cancellationToken);
    }

    private ResourceOrchestratorDeployment CreateDefaultContainerOrchestratorDeployment(
        ApplicationResourceDefinition application,
        ResourceState state,
        bool runtimeRevisionScoped = false)
    {
        var revision = ContainerRevisionService.GetEffectiveRevision(application);
        return ContainerOrchestratorDeploymentFactory.CreateDeployment(
            application,
            state,
            CreateWorkloadConfiguration(application),
            runtimeRevisionScoped &&
                ContainerRuntimeRevisionPolicy.ShouldUseRevisionScopedRuntimeInstances(
                    application,
                    revision,
                    containerDeployments.ListRevisions(application.Id)));
    }

    private ResourceWorkloadConfiguration CreateWorkloadConfiguration(
        ApplicationResourceDefinition application) =>
        WorkloadConfigurationFactory.Create(
            application,
            application.EnvironmentVariables,
            GetEffectiveObservability(application));

    private ResourceObservability GetEffectiveObservability(ApplicationResourceDefinition definition) =>
        definition.Observability ??
        (options.EnableObservabilityByDefault
            ? ResourceObservability.Default
            : ResourceObservability.None);

    private static Resource CreateRuntimeContainerResource(
        ApplicationResourceDefinition application,
        ResourceOrchestratorDeployment deployment,
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorServiceInstance instance,
        ResourceState state)
    {
        var service = deployment.Spec.Service;
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentId] = deployment.Id,
            [ResourceAttributeNames.DeploymentServiceId] = deployment.ServiceId,
            [ResourceAttributeNames.DeploymentRevision] = deployment.RevisionId,
            [ResourceAttributeNames.DeploymentReplicaGroupId] = replicaGroup.Id,
            [ResourceAttributeNames.RuntimeKind] = "containerReplica",
            [ResourceAttributeNames.RuntimeContainerName] = instance.Name,
            [ResourceAttributeNames.RuntimeReplicaOrdinal] = instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.RuntimeReplicaCount] = instance.ReplicaCount.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.RuntimeRevision] = deployment.RevisionId,
            [ResourceAttributeNames.RuntimeMaterialization] = "orchestratorMaterialized"
        };

        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerImage, service.Workload.Image);
        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerHostId, service.Workload.ContainerHostId);
        AddIfNotEmpty(
            attributes,
            ResourceAttributeNames.DeploymentEnvironmentRevisionId,
            application.DeploymentEnvironmentRevisionId);

        return new Resource(
            ApplicationResourceNames.CreateRuntimeContainerResourceId(application.Id, instance.ReplicaOrdinal),
            instance.Name,
            "Container replica",
            ProviderDisplayName,
            "local",
            state,
            [],
            deployment.RevisionId,
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: application.Id,
            TypeId: "runtime.container",
            ResourceClass: ResourceClass.Container,
            Attributes: attributes,
            Capabilities:
            [
                new(ResourceCapabilityIds.Monitoring),
                new(ResourceCapabilityIds.LogSources)
            ],
            Source: ResourceSource.Orchestrator,
            ManagementMode: ResourceManagementMode.RuntimeManaged,
            Visibility: ResourceVisibility.Hidden,
            OwnerResourceId: application.Id,
            CleanupBehavior: ResourceCleanupBehavior.DeleteWithOwner);
    }

    private static async Task<ResourceMonitoringSnapshot> GetContainerMonitoringSnapshotAsync(
        Resource resource,
        ContainerHostDescriptor engine,
        string containerName,
        DateTimeOffset timestamp,
        string availableMessage,
        string unavailableMessage,
        CancellationToken cancellationToken)
    {
        var result = await ApplicationContainerHostCommands.CaptureAsync(
            engine,
            ["stats", "--no-stream", "--format", "{{json .}}", containerName],
            cancellationToken);
        if (result.ExitCode != 0 ||
            !ApplicationContainerMonitoringMetrics.TryParseStatsJson(
                result.Output,
                DateTimeOffset.UtcNow,
                out var containerSnapshot))
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                string.IsNullOrWhiteSpace(result.Error)
                    ? unavailableMessage
                    : result.Error.Trim());
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            containerSnapshot.Timestamp,
            ApplicationContainerMonitoringMetrics.CreateMetricSamples(containerSnapshot),
            "Available",
            availableMessage);
    }

    private static ResourceMetricSample ApplyRuntimeContainerMetricScope(
        ResourceMetricSample metric,
        ResourceOrchestratorServiceInstance instance,
        string revision)
    {
        var attributes = new Dictionary<string, string>(metric.Attributes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            [TelemetryAttributeNames.RuntimeReplicaOrdinal] = instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture),
            [TelemetryAttributeNames.RuntimeReplicaCount] = instance.ReplicaCount.ToString(CultureInfo.InvariantCulture),
            [TelemetryAttributeNames.RuntimeContainerName] = instance.Name,
            [TelemetryAttributeNames.DeploymentRevision] = revision
        };

        return metric with
        {
            DisplayName = $"Replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)} {metric.DisplayName ?? metric.Name}",
            Attributes = attributes
        };
    }

    private static string GetContainerName(ResourceOrchestratorService service, int replica = 1) =>
        ResourceOrchestratorReplicaGroups.CreateDefaultInstanceName(
            service.Name,
            replica,
            service.Replicas);

    private static bool IsRuntimeContainerReplica(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, "runtime.container", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            GetAttribute(resource, ResourceAttributeNames.RuntimeKind),
            "containerReplica",
            StringComparison.OrdinalIgnoreCase);

    private static string GetAttribute(Resource resource, string name) =>
        resource.ResourceAttributes.TryGetValue(name, out var value)
            ? value
            : string.Empty;

    private static bool IsReplicaModeEnabled(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        application.ReplicasEnabled;

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
