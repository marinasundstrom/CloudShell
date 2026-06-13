using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class HostScopedResourceShutdownService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    ILogger<HostScopedResourceShutdownService> logger) : IHostedService
{
    public const string ShutdownTrigger = "host-shutdown";

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var resourceManager = scope.ServiceProvider.GetRequiredService<IResourceManager>();
        var catalog = scope.ServiceProvider.GetRequiredService<IResourceOrchestrationCatalog>();
        var snapshot = await catalog.GetSnapshotAsync(cancellationToken);
        var candidates = GetHostScopedStopCandidates(snapshot).ToArray();

        foreach (var resource in OrderForShutdown(candidates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                LogDevelopmentLifecycle(
                    "Stopping host-scoped resource {ResourceId} during Control Plane shutdown.",
                    resource.Id);
                await resourceManager.ExecuteResourceActionAsync(
                    new ExecuteResourceActionCommand(
                        resource.Id,
                        ResourceActionIds.Stop,
                        IgnoreDependentWarning: true,
                        TriggeredBy: ShutdownTrigger),
                    cancellationToken);
                LogDevelopmentLifecycle(
                    "Stopped host-scoped resource {ResourceId} during Control Plane shutdown.",
                    resource.Id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Failed to stop host-scoped resource {ResourceId} during Control Plane shutdown.",
                    resource.Id);
            }
        }
    }

    private void LogDevelopmentLifecycle(string message, params object?[] args)
    {
        if (environment.IsDevelopment())
        {
            logger.LogInformation(message, args);
        }
    }

    private static IEnumerable<Resource> GetHostScopedStopCandidates(
        ResourceOrchestrationCatalogSnapshot snapshot)
    {
        foreach (var resource in snapshot.Resources)
        {
            if (resource.State == ResourceState.Stopped ||
                resource.StopAction is null ||
                !snapshot.Workloads.TryGetValue(resource.Id, out var workload) ||
                workload.Lifetime != ResourceLifetime.ControlPlaneScoped)
            {
                continue;
            }

            yield return resource;
        }
    }

    private static IEnumerable<Resource> OrderForShutdown(IReadOnlyList<Resource> resources)
    {
        var resourcesById = resources.ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        return resources
            .OrderBy(resource => GetDependentDepth(resource, resourcesById, []))
            .ThenBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static int GetDependentDepth(
        Resource resource,
        IReadOnlyDictionary<string, Resource> resourcesById,
        HashSet<string> visiting)
    {
        if (!visiting.Add(resource.Id))
        {
            return 0;
        }

        try
        {
            var depth = 0;
            foreach (var dependent in resourcesById.Values.Where(candidate =>
                         candidate.DependsOn.Contains(resource.Id, StringComparer.OrdinalIgnoreCase)))
            {
                depth = Math.Max(depth, 1 + GetDependentDepth(dependent, resourcesById, visiting));
            }

            return depth;
        }
        finally
        {
            visiting.Remove(resource.Id);
        }
    }
}
