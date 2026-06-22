using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Authorization;

namespace CloudShell.Providers.Applications;

internal sealed class SqlServerApplicationResourceProvider(
    IApplicationResourceProjectionSource projections,
    IApplicationResourceDefinitionSource definitions,
    IApplicationResourceProcedureOperations procedures,
    IApplicationResourceTemplateOperations templates,
    IApplicationResourceDeclarationOperations declarations,
    IApplicationResourceDescriptorOperations descriptors,
    IApplicationResourceActionAvailabilityOperations actions,
    ISqlServerApplicationResourceProviderOperations sqlServerApplications)
    : ApplicationResourceTypeProvider(
        projections,
        definitions,
        procedures,
        templates,
        declarations,
        descriptors,
        actions),
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
        sqlServerApplications.GetSqlServerPermissionGrantStatusAsync(request, cancellationToken);
}
