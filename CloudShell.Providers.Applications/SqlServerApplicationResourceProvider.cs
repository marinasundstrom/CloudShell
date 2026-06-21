using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Authorization;

namespace CloudShell.Providers.Applications;

internal sealed class SqlServerApplicationResourceProvider(
    IApplicationResourceProjectionSource projections,
    ApplicationResourceService applications)
    : ApplicationResourceTypeProvider(projections, applications),
    IResourcePermissionGrantStatusProvider
{
    public const string ProviderId = ApplicationResourceProviderIds.SqlServer;

    public override string Id => ProviderId;

    string IResourcePermissionGrantStatusProvider.ProviderId => ProviderId;

    protected override ApplicationResourceProjection Projection { get; } = new(
        application => string.Equals(
            application.ResourceType,
            ApplicationResourceTypes.SqlServer,
            StringComparison.OrdinalIgnoreCase),
        _ => "SQL Server",
        ApplicationResourceProjectionSupport.GetContainerVersion,
        ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
        _ => ResourceClass.Service);

    public bool CanGetStatus(ResourcePermissionGrantStatusRequest request) =>
        string.Equals(request.TargetResource.EffectiveTypeId, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(request.Grant.Permission, DatabaseResourceOperationPermissions.ReadWrite, StringComparison.OrdinalIgnoreCase);

    public Task<ResourcePermissionGrantStatus> GetStatusAsync(
        ResourcePermissionGrantStatusRequest request,
        CancellationToken cancellationToken = default) =>
        Applications.GetSqlServerPermissionGrantStatusAsync(request, cancellationToken);
}
