using System.Globalization;
using System.Text;

namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceOrchestratorServiceInstance(
    string Name,
    int ReplicaOrdinal,
    int ReplicaCount,
    string? RuntimeRevisionId = null);

public static class ResourceOrchestratorServiceInstances
{
    public static IReadOnlyList<ResourceOrchestratorServiceInstance> CreateDefaultInstances(
        ResourceOrchestratorService service)
    {
        var runtimeRevision = NormalizeRuntimeRevisionId(service.RuntimeRevisionId);
        return runtimeRevision is null
            ? CreateInstances(service, runtimeRevision)
            : CreateRevisionInstances(service, runtimeRevision);
    }

    public static IReadOnlyList<ResourceOrchestratorServiceInstance> CreateRevisionInstances(
        ResourceOrchestratorService service,
        string runtimeRevisionId)
    {
        var revision = NormalizeRuntimeRevisionId(runtimeRevisionId) ?? "revision";
        return CreateInstances(service, revision);
    }

    private static IReadOnlyList<ResourceOrchestratorServiceInstance> CreateInstances(
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

        return instances;
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

public sealed record ResourceOrchestratorServiceProcedureContext(
    ResourceProcedureContext ResourceContext,
    ResourceOrchestratorService Service);

public sealed record ResourceOrchestratorServiceInstanceContext(
    ResourceProcedureContext ResourceContext,
    ResourceOrchestratorService Service,
    ResourceOrchestratorServiceInstance Instance);

public sealed record ResourceOrchestratorDeploymentProcedureContext(
    ResourceProcedureContext ResourceContext,
    ResourceOrchestratorService Service,
    ResourceOrchestratorDeployment Deployment);

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
