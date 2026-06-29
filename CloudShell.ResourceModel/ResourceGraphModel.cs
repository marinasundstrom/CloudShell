namespace CloudShell.ResourceModel;

public sealed class ResourceGraphModel(IResourceStateProvider stateProvider)
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private ResourceGraphSnapshot? _snapshot;

    public async ValueTask<ResourceGraphSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            return await GetSnapshotCoreAsync(cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async ValueTask<ResourceGraphSnapshot> ReloadAsync(
        CancellationToken cancellationToken = default) =>
        await RefreshAsync(ResourceGraphRefreshContext.Full, cancellationToken);

    public async ValueTask<ResourceGraphSnapshot> RefreshAsync(
        ResourceGraphRefreshContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await _sync.WaitAsync(cancellationToken);
        try
        {
            var storedSnapshot = await stateProvider.GetSnapshotAsync(cancellationToken);
            _snapshot = context.IsFullGraph || _snapshot is null
                ? storedSnapshot
                : RefreshSelectedResources(_snapshot, storedSnapshot, context.ResourceIds!);

            return _snapshot;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async ValueTask<ResourceGraphChangeTracker> CreateChangeTrackerAsync(
        CancellationToken cancellationToken = default) =>
        new(await GetSnapshotAsync(cancellationToken));

    public async ValueTask<ResourceGraphCommitResult> CommitAsync(
        ResourceGraphChangeSet changes,
        ResourceGraphCommitContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(context);

        await _sync.WaitAsync(cancellationToken);
        try
        {
            return await CommitCoreAsync(changes, context, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async ValueTask<ResourceGraphSnapshot> GetSnapshotCoreAsync(
        CancellationToken cancellationToken)
    {
        _snapshot ??= await stateProvider.GetSnapshotAsync(cancellationToken);
        return _snapshot;
    }

    private async ValueTask<ResourceGraphCommitResult> CommitCoreAsync(
        ResourceGraphChangeSet changes,
        ResourceGraphCommitContext context,
        CancellationToken cancellationToken)
    {
        var storedSnapshot = await stateProvider.GetSnapshotAsync(cancellationToken);
        _snapshot = storedSnapshot;

        if (changes.BaseVersion != storedSnapshot.Version)
        {
            return new ResourceGraphCommitResult(
                storedSnapshot.Version,
                null,
                [CreateVersionConflict(changes.BaseVersion, storedSnapshot.Version)],
                ResourceGraphCommitSummary.VersionConflict(changes.BaseVersion, storedSnapshot.Version));
        }

        var result = await stateProvider.CommitAsync(
            changes,
            context,
            cancellationToken);

        if (result.IsCommitted)
        {
            _snapshot = result.Snapshot;
        }
        else if (result.Summary.Status == ResourceGraphCommitStatus.VersionConflict)
        {
            _snapshot = await stateProvider.GetSnapshotAsync(cancellationToken);
        }

        return result;
    }

    private static ResourceGraphSnapshot RefreshSelectedResources(
        ResourceGraphSnapshot currentSnapshot,
        ResourceGraphSnapshot storedSnapshot,
        IReadOnlyList<string> resourceIds)
    {
        var selectedIds = new HashSet<string>(resourceIds, StringComparer.OrdinalIgnoreCase);
        var resources = currentSnapshot.Resources.ToDictionary(
            resource => resource.EffectiveResourceId,
            resource => resource,
            StringComparer.OrdinalIgnoreCase);

        foreach (var resourceId in selectedIds)
        {
            resources.Remove(resourceId);
        }

        foreach (var resource in storedSnapshot.Resources.Where(resource =>
            selectedIds.Contains(resource.EffectiveResourceId)))
        {
            resources[resource.EffectiveResourceId] = resource;
        }

        return currentSnapshot with
        {
            Resources = resources.Values.ToArray()
        };
    }

    private static ResourceDefinitionDiagnostic CreateVersionConflict(
        ResourceGraphVersion expected,
        ResourceGraphVersion current) =>
        ResourceDefinitionDiagnostic.Error(
            ResourceDefinitionDiagnosticCodes.ResourceGraphVersionConflict,
            $"Resource graph version '{expected}' is stale. Current version is '{current}'.");
}
