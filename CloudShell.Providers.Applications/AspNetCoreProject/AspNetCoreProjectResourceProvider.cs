using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class AspNetCoreProjectResourceProvider(
    IApplicationResourceProjectionSource projections,
    IApplicationResourceProviderOperations applications)
    : ApplicationResourceTypeProvider(projections, applications)
{
    public const string ProviderId = ApplicationResourceProviderIds.AspNetCoreProject;

    public override string Id => ProviderId;

    protected override ApplicationResourceProjection Projection { get; } = new(
        application => string.Equals(
            application.ResourceType,
            ApplicationResourceTypes.AspNetCoreProject,
            StringComparison.OrdinalIgnoreCase),
        _ => "ASP.NET Core project",
        application => ApplicationResourceProjectionSupport.FirstNonEmpty(
            Path.GetFileName(application.ProjectPath),
            "project") ?? "project",
        _ => ResourceWorkloadKind.AspNetCoreProject.ToString(),
        _ => ResourceClass.Project);
}
