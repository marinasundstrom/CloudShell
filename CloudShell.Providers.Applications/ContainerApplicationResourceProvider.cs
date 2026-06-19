using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationResourceProvider(
    IApplicationResourceProjectionSource projections,
    ApplicationResourceService applications)
    : ApplicationResourceTypeProvider(projections, applications)
{
    public const string ProviderId = ApplicationResourceProviderIds.ContainerApplication;

    public override string Id => ProviderId;

    protected override ApplicationResourceProjection Projection { get; } = new(
        application => ApplicationResourceTypes.IsContainerApp(application.ResourceType),
        _ => "Container app",
        ApplicationResourceProjectionSupport.GetContainerVersion,
        ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
        _ => ResourceClass.Container);
}
