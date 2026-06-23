using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

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

        foreach (var candidate in OrderForShutdown(candidates))
        {
            var resource = candidate.Resource;
            if (cleanupToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Timed out stopping host-scoped resources during Control Plane shutdown.");
                break;
            }

            try
            {
                LogLifecycle(
                    resource,
                    "Stopping host-scoped resource {ResourceName} during Control Plane shutdown.",
                    ResourceDisplayLabels.GetQualifiedLabel(resource));
                await orchestration.ExecuteActionAsync(
                    resource,
                    candidate.Action,
                    startDependencies: false,
                    new ShutdownAuthorizationService(),
                    cleanupToken,
                    triggeredBy: ShutdownTrigger,
                    cause: "Host shutdown");
                LogLifecycle(
                    resource,
                    "Stopped host-scoped resource {ResourceName} during Control Plane shutdown.",
                    ResourceDisplayLabels.GetQualifiedLabel(resource));
            }
            catch (Exception exception)
            {
                using var logScope = ResourceLogScope.Begin(logger, resource);
                logger.LogWarning(
                    exception,
                    "Failed to stop host-scoped resource {ResourceName} during Control Plane shutdown.",
                    ResourceDisplayLabels.GetQualifiedLabel(resource));

                if (cleanupToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private void LogLifecycle(Resource resource, string message, params object?[] args)
    {
        using var scope = ResourceLogScope.Begin(logger, resource);
        logger.LogInformation(message, args);
    }

    private static IEnumerable<HostScopedStopCandidate> GetHostScopedStopCandidates(
        ResourceOrchestrationCatalogSnapshot snapshot)
    {
        foreach (var resource in snapshot.Resources)
        {
            if (!snapshot.Workloads.TryGetValue(resource.Id, out var workload) ||
                workload.Lifetime != ResourceLifetime.ControlPlaneScoped)
            {
                continue;
            }

            if (resource.State != ResourceState.Stopped &&
                resource.StopAction is { } stopAction)
            {
                yield return new HostScopedStopCandidate(resource, stopAction);
                continue;
            }

            if (resource.State == ResourceState.Stopped &&
                workload.Kind is ResourceWorkloadKind.ContainerImage or ResourceWorkloadKind.ContainerBuild)
            {
                yield return new HostScopedStopCandidate(resource, ResourceAction.Stop);
            }
        }
    }

    private static IEnumerable<HostScopedStopCandidate> OrderForShutdown(IReadOnlyList<HostScopedStopCandidate> candidates)
    {
        var resources = candidates.Select(candidate => candidate.Resource).ToArray();
        var resourcesById = resources.ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        return candidates
            .OrderBy(candidate => GetDependentDepth(candidate.Resource, resourcesById, []))
            .ThenBy(candidate => candidate.Resource.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Resource.Id, StringComparer.OrdinalIgnoreCase);
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

    private sealed record HostScopedStopCandidate(Resource Resource, ResourceAction Action);
}
