using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class SqlServerApplicationResourceProvider(
    IApplicationResourceProjectionSource projections,
    ApplicationResourceService applications)
    : ApplicationResourceTypeProvider(projections, applications)
{
    public const string ProviderId = ApplicationResourceProviderIds.SqlServer;

    public override string Id => ProviderId;

    protected override ApplicationResourceProjection Projection { get; } = new(
        application => string.Equals(
            application.ResourceType,
            ApplicationResourceTypes.SqlServer,
            StringComparison.OrdinalIgnoreCase),
        _ => "SQL Server",
        ApplicationResourceProjectionSupport.GetContainerVersion,
        ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
        _ => ResourceClass.Service);
}
