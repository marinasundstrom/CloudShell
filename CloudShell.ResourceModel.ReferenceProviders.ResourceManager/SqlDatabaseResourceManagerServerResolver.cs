using CloudShell.ResourceModel.ResourceManager;

namespace CloudShell.ResourceModel.ReferenceProviders.ResourceManager;

public sealed class SqlDatabaseResourceManagerServerResolver(
    ResourceGraphModel graphModel,
    ResourceGraphResolver graphResolver) : ISqlDatabaseServerResolver
{
    private readonly ResourceGraphModel _graphModel =
        graphModel ?? throw new ArgumentNullException(nameof(graphModel));
    private readonly ResourceGraphResolver _graphResolver =
        graphResolver ?? throw new ArgumentNullException(nameof(graphResolver));

    public async ValueTask<Resource?> ResolveServerAsync(
        Resource database,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(context);

        if (!SqlDatabaseResourceTypeProvider.TryGetServerDependencyResourceId(
                database.State,
                out var serverResourceId))
        {
            return null;
        }

        var scopedServer = context.FindResource(serverResourceId);
        if (scopedServer is not null)
        {
            return scopedServer;
        }

        var snapshot = await _graphModel.GetSnapshotAsync(cancellationToken);
        var resolution = _graphResolver.ResolveResource(
            snapshot,
            serverResourceId);

        return resolution.HasErrors ? null : resolution.Resource;
    }
}
