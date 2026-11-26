using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public sealed record ResourceAttentionSignal(
    ResourceSignalSeverity Severity,
    string Tooltip);

internal static class ResourceAttentionSignalResolver
{
    public static ResourceAttentionSignal? Resolve(
        Resource resource,
        ResourceHealthSummary? health,
        ResourceFailureSignal? failure,
        Microsoft.Extensions.Localization.IStringLocalizer<CloudShell.Hosting.Localization.SharedResource> localizer)
    {
        if (failure is not null)
            return new(failure.Severity, ResourceFailureSignalDisplay.GetTooltip(failure, localizer));
        if (health?.Status == ResourceHealthStatus.Unhealthy)
            return new(ResourceSignalSeverity.Error, localizer["Health checks report this resource as unhealthy. Open its dashboard to investigate."].Value);
        if (ResourceLifecycleDisplay.HasStateHealthIncongruity(resource, health))
            return new(ResourceSignalSeverity.Warning, localizer["Health checks report healthy while the resource is {0}. Verify the lifecycle state, probe target, and possible leftover process.", resource.State!].Value);
        if (resource.State == ResourceState.Degraded)
            return new(ResourceSignalSeverity.Warning, localizer["This resource is degraded. Open its dashboard to investigate."].Value);
        if (resource.State == ResourceState.Unknown)
            return new(ResourceSignalSeverity.Warning, localizer["The lifecycle state of this resource is unknown."].Value);
        return null;
    }
}
