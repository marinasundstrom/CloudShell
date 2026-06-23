using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    public bool CanMonitor(Resource resource)
    {
        if (IsRuntimeContainerReplica(resource))
        {
            return !string.IsNullOrWhiteSpace(resource.OwnerResourceId) &&
                store.GetApplication(resource.OwnerResourceId) is { } owner &&
                IsReplicaModeEnabled(ResolveDefinition(owner));
        }

        if (!ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId))
        {
            return false;
        }

        var application = store.GetApplication(resource.Id);
        if (application is null)
        {
            return false;
        }

        return true;
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

        var application = GetApplication(resource.Id);
        if (application is null)
        {
            return null;
        }

        if (IsContainerBacked(application))
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
                DisplayName,
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
                DisplayName,
                timestamp,
                [],
                "Unavailable",
                "The application process could not be observed.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            DisplayName,
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
                DisplayName,
                timestamp,
                [],
                "Unavailable",
                "Container metrics are available only while the resource is running.");
        }

        var engine = await ResolveStaticContainerHostAsync(application, cancellationToken);
        if (engine is null)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                DisplayName,
                timestamp,
                [],
                "Unavailable",
                "Container metrics require a configured container host.");
        }

        var service = CreateActiveContainerOrchestratorService(application);
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
        var replicaGroup = CreateDefaultContainerReplicaGroup(deployment.Spec.Service);
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
                DisplayName,
                DateTimeOffset.UtcNow,
                metrics,
                "Available",
                $"{available.ToString(CultureInfo.InvariantCulture)}/{replicaGroup.MaterializedReplicas.ToString(CultureInfo.InvariantCulture)} replica monitoring snapshots are available.");
        }

        if (available > 0)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                DisplayName,
                DateTimeOffset.UtcNow,
                metrics,
                "Degraded",
                $"{available.ToString(CultureInfo.InvariantCulture)}/{replicaGroup.MaterializedReplicas.ToString(CultureInfo.InvariantCulture)} replica monitoring snapshots are available.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            DisplayName,
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
                DisplayName,
                timestamp,
                [],
                "Unavailable",
                "Container replica metrics are available only while the replica is running.");
        }

        var owner = string.IsNullOrWhiteSpace(resource.OwnerResourceId)
            ? null
            : GetApplication(resource.OwnerResourceId);
        if (owner is null)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                DisplayName,
                timestamp,
                [],
                "Unavailable",
                "Container replica metrics require the owning application resource.");
        }

        var engine = await ResolveStaticContainerHostAsync(owner, cancellationToken);
        if (engine is null)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                DisplayName,
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
                DisplayName,
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
                "Applications",
                timestamp,
                [],
                "Unavailable",
                string.IsNullOrWhiteSpace(result.Error)
                    ? unavailableMessage
                    : result.Error.Trim());
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            "Applications",
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
}
