using System.Globalization;
using System.Text;

namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceOrchestratorServiceInstance(
    string Name,
    int ReplicaOrdinal,
    int ReplicaCount,
    string? RuntimeRevisionId = null);

public sealed record ResourceOrchestratorReplicaGroup(
    string Id,
    string ServiceId,
    string? RuntimeRevisionId,
    int RequestedReplicas,
    IReadOnlyList<ResourceOrchestratorServiceInstance> Instances)
{
    public int MaterializedReplicas => Instances.Count;
}

public static class ResourceOrchestratorReplicaGroups
{
    public static ResourceOrchestratorReplicaGroup CreateDefaultReplicaGroup(
        ResourceOrchestratorService service) =>
        CreateReplicaGroup(service, NormalizeRuntimeRevisionId(service.RuntimeRevisionId));

    public static ResourceOrchestratorReplicaGroup CreateRevisionReplicaGroup(
        ResourceOrchestratorService service,
        string runtimeRevisionId)
    {
        var revision = NormalizeRuntimeRevisionId(runtimeRevisionId) ?? "revision";
        return CreateReplicaGroup(service, revision);
    }

    private static ResourceOrchestratorReplicaGroup CreateReplicaGroup(
        ResourceOrchestratorService service,
        string? runtimeRevisionId)
    {
        var instances = new ResourceOrchestratorServiceInstance[service.Replicas];
        for (var replica = 1; replica <= service.Replicas; replica++)
        {
            instances[replica - 1] = new ResourceOrchestratorServiceInstance(
                runtimeRevisionId is null
                    ? CreateDefaultInstanceName(service.Name, replica, service.Replicas)
                    : CreateRevisionInstanceName(service.Name, runtimeRevisionId, replica, service.Replicas),
                replica,
                service.Replicas,
                runtimeRevisionId);
        }

        return new ResourceOrchestratorReplicaGroup(
            CreateReplicaGroupId(service.Name, runtimeRevisionId),
            service.Name,
            runtimeRevisionId,
            service.Replicas,
            instances);
    }

    public static string CreateDefaultServiceName(string resourceId)
    {
        var builder = new StringBuilder(resourceId.Length);
        foreach (var character in resourceId.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var name = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(name)
            ? "cloudshell-container-app"
            : $"cloudshell-{name}";
    }

    public static string CreateDefaultInstanceName(
        string serviceName,
        int replicaOrdinal,
        int replicaCount) =>
        replicaCount <= 1
            ? serviceName
            : $"{serviceName}-replica-{Math.Max(1, replicaOrdinal).ToString(CultureInfo.InvariantCulture)}";

    public static string CreateRevisionInstanceName(
        string serviceName,
        string runtimeRevisionId,
        int replicaOrdinal,
        int replicaCount)
    {
        var revision = NormalizeRuntimeRevisionId(runtimeRevisionId) ?? "revision";
        return replicaCount <= 1
            ? $"{serviceName}-{revision}"
            : $"{serviceName}-{revision}-replica-{Math.Max(1, replicaOrdinal).ToString(CultureInfo.InvariantCulture)}";
    }

    public static string CreateReplicaGroupId(
        string serviceName,
        string? runtimeRevisionId)
    {
        var revision = NormalizeRuntimeRevisionId(runtimeRevisionId);
        return revision is null
            ? $"{serviceName}-replicas"
            : $"{serviceName}-{revision}-replicas";
    }

    private static string? NormalizeRuntimeRevisionId(string? runtimeRevisionId)
    {
        if (string.IsNullOrWhiteSpace(runtimeRevisionId))
        {
            return null;
        }

        var builder = new StringBuilder(runtimeRevisionId.Length);
        foreach (var character in runtimeRevisionId.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var revision = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(revision)
            ? null
            : revision;
    }
}

public static class ResourceOrchestratorServiceInstances
{
    public static IReadOnlyList<ResourceOrchestratorServiceInstance> CreateDefaultInstances(
        ResourceOrchestratorService service) =>
        ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service).Instances;

    public static IReadOnlyList<ResourceOrchestratorServiceInstance> CreateRevisionInstances(
        ResourceOrchestratorService service,
        string runtimeRevisionId) =>
        ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(service, runtimeRevisionId).Instances;

    public static ResourceOrchestratorReplicaGroup CreateDefaultReplicaGroup(
        ResourceOrchestratorService service) =>
        ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);

    public static ResourceOrchestratorReplicaGroup CreateRevisionReplicaGroup(
        ResourceOrchestratorService service,
        string runtimeRevisionId) =>
        ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(service, runtimeRevisionId);

    public static string CreateDefaultServiceName(string resourceId) =>
        ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(resourceId);

    public static string CreateDefaultInstanceName(
        string serviceName,
        int replicaOrdinal,
        int replicaCount) =>
        ResourceOrchestratorReplicaGroups.CreateDefaultInstanceName(serviceName, replicaOrdinal, replicaCount);

    public static string CreateRevisionInstanceName(
        string serviceName,
        string runtimeRevisionId,
        int replicaOrdinal,
        int replicaCount) =>
        ResourceOrchestratorReplicaGroups.CreateRevisionInstanceName(
            serviceName,
            runtimeRevisionId,
            replicaOrdinal,
            replicaCount);

    public static string CreateReplicaGroupId(
        string serviceName,
        string? runtimeRevisionId) =>
        ResourceOrchestratorReplicaGroups.CreateReplicaGroupId(serviceName, runtimeRevisionId);
}

public sealed record ResourceOrchestratorServiceProcedureContext(
    ResourceProcedureContext ResourceContext,
    ResourceOrchestratorService Service,
    ResourceOrchestratorReplicaGroup? ReplicaGroup = null);

public sealed record ResourceOrchestratorServiceInstanceContext(
    ResourceProcedureContext ResourceContext,
    ResourceOrchestratorService Service,
    ResourceOrchestratorServiceInstance Instance,
    ResourceOrchestratorReplicaGroup? ReplicaGroup = null);

public sealed record ResourceOrchestratorDeploymentProcedureContext(
    ResourceProcedureContext ResourceContext,
    ResourceOrchestratorService Service,
    ResourceOrchestratorDeployment Deployment,
    ResourceOrchestratorReplicaGroup? ReplicaGroup = null);

public interface IResourceOrchestratorServiceProcedureProvider
{
    bool CanExecuteOrchestratorService(
        Resource resource,
        ResourceAction action);

    Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default);

    Task PrepareOrchestratorServiceAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    Task ExecuteOrchestratorServiceInstanceAsync(
        ResourceOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default);

    Task CompleteOrchestratorDeploymentAsync(
        ResourceOrchestratorDeploymentProcedureContext context,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
