using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class HostScopedResourceShutdownService(
    IServiceScopeFactory scopeFactory,
    ILoggerFactory loggerFactory) : IHostedService
{
    private static readonly TimeSpan ShutdownCleanupTimeout = TimeSpan.FromSeconds(20);
    private readonly ILogger logger = loggerFactory.CreateLogger(CloudShellLogCategories.HostScopedResourceShutdown);

    public const string ShutdownTrigger = "host-shutdown";

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // The host token may already be cancelled when StopAsync runs after
        // server shutdown timeouts. Resource cleanup is bounded separately.
        _ = cancellationToken;
        using var cleanup = new CancellationTokenSource(ShutdownCleanupTimeout);
        var cleanupToken = cleanup.Token;

        await using var scope = scopeFactory.CreateAsyncScope();
        var orchestration = scope.ServiceProvider.GetRequiredService<ResourceOrchestrationService>();
        var catalog = scope.ServiceProvider.GetRequiredService<IResourceOrchestrationCatalog>();
        ResourceOrchestrationCatalogSnapshot snapshot;
        try
        {
            snapshot = await catalog.GetSnapshotAsync(cleanupToken);
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                exception,
                "Timed out reading host-scoped resources during Control Plane shutdown.");
            return;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to read host-scoped resources during Control Plane shutdown.");
            return;
        }

        var candidates = GetHostScopedStopCandidates(snapshot).ToArray();

        foreach (var resource in OrderForShutdown(candidates))
        {
            if (cleanupToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Timed out stopping host-scoped resources during Control Plane shutdown.");
                break;
            }

            try
            {
                LogLifecycle(
                    "Stopping host-scoped resource {ResourceId} during Control Plane shutdown.",
                    resource.Id);
                await orchestration.ExecuteActionAsync(
                    resource,
                    resource.StopAction!,
                    startDependencies: false,
                    new ShutdownAuthorizationService(),
                    cleanupToken,
                    triggeredBy: ShutdownTrigger,
                    cause: "Host shutdown");
                LogLifecycle(
                    "Stopped host-scoped resource {ResourceId} during Control Plane shutdown.",
                    resource.Id);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to stop host-scoped resource {ResourceId} during Control Plane shutdown.",
                    resource.Id);

                if (cleanupToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private void LogLifecycle(string message, params object?[] args) =>
        logger.LogInformation(message, args);

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

    private sealed class ShutdownAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => true;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => true;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => true;
    }
}
