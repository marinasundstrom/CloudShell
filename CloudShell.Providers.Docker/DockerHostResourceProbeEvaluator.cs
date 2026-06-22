using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Docker;

internal sealed class DockerHostResourceProbeEvaluator(
    DockerContainerResourceProvider provider) : IResourceProbeEvaluator
{
    public const string SourceKind = "docker.host";

    public bool CanEvaluate(Resource resource, ResourceHealthCheck check) =>
        IsDockerHost(resource) &&
        string.Equals(check.EffectiveSource.Kind, SourceKind, StringComparison.OrdinalIgnoreCase);

    public Task<ResourceHealthCheckResult> EvaluateAsync(
        Resource resource,
        ResourceHealthCheck check,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = provider.GetHostConnectionStatus(resource.Id);
        var health = status.IsConnected
            ? ResourceHealthStatus.Healthy
            : ResourceHealthStatus.Unhealthy;
        var detail = status.IsConnected
            ? $"Docker host API is reachable at {status.Endpoint}."
            : $"Docker host API is unavailable at {status.Endpoint}: {status.Error ?? "Unknown error"}";

        return Task.FromResult(new ResourceHealthCheckResult(
            check,
            health,
            detail,
            status.Endpoint,
            status.IsConnected
                ? ResourceHealthCheckOutcome.Responded
                : ResourceHealthCheckOutcome.Unresolved,
            status.LastChecked));
    }

    private static bool IsDockerHost(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, DockerContainerResourceProvider.HostResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resource.EffectiveTypeId, DockerContainerResourceProvider.LegacyEngineResourceType, StringComparison.OrdinalIgnoreCase);
}
