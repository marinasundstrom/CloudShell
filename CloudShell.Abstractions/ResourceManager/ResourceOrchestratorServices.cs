using System.Globalization;
using System.Text;

namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceOrchestratorServiceInstance(
    string Name,
    int ReplicaOrdinal,
    int ReplicaCount);

public static class ResourceOrchestratorServiceInstances
{
    public static IReadOnlyList<ResourceOrchestratorServiceInstance> CreateDefaultInstances(
        ResourceOrchestratorService service)
    {
        var instances = new ResourceOrchestratorServiceInstance[service.Replicas];
        for (var replica = 1; replica <= service.Replicas; replica++)
        {
            instances[replica - 1] = new ResourceOrchestratorServiceInstance(
                CreateDefaultInstanceName(service.Name, replica, service.Replicas),
                replica,
                service.Replicas);
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
}

public sealed record ResourceOrchestratorServiceProcedureContext(
    ResourceProcedureContext ResourceContext,
    ResourceOrchestratorService Service);

public sealed record ResourceOrchestratorServiceInstanceContext(
    ResourceProcedureContext ResourceContext,
    ResourceOrchestratorService Service,
    ResourceOrchestratorServiceInstance Instance);

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
}
