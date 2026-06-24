namespace CloudShell.ResourceDefinitions;

public interface IResourceStateProvider
{
    ValueTask<ResourceGraphSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ResourceGraphCommitResult> CommitAsync(
        ResourceGraphChangeSet changes,
        ResourceGraphCommitContext context,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryResourceStateProvider(
    IEnumerable<ResourceState>? resources = null) : IResourceStateProvider
{
    private readonly Dictionary<string, ResourceState> _resources = (resources ?? [])
        .ToDictionary(
            resource => resource.EffectiveResourceId,
            resource => resource,
            StringComparer.OrdinalIgnoreCase);
    private ResourceGraphVersion _version = ResourceGraphVersion.Initial;

    public ValueTask<ResourceGraphSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(CreateSnapshot());

    public ValueTask<ResourceGraphCommitResult> CommitAsync(
        ResourceGraphChangeSet changes,
        ResourceGraphCommitContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(context);

        if (changes.HasErrors)
        {
            return ValueTask.FromResult(new ResourceGraphCommitResult(
                _version,
                null,
                changes.Diagnostics,
                ResourceGraphCommitSummary.Rejected(changes, _version)));
        }

        if (changes.BaseVersion != _version)
        {
            return ValueTask.FromResult(new ResourceGraphCommitResult(
                _version,
                null,
                [
                    ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceGraphVersionConflict,
                        $"Resource graph version '{changes.BaseVersion}' is stale. Current version is '{_version}'.")
                ],
                ResourceGraphCommitSummary.VersionConflict(changes.BaseVersion, _version)));
        }

        if (changes.AcceptedResources.Count == 0)
        {
            return ValueTask.FromResult(new ResourceGraphCommitResult(
                _version,
                CreateSnapshot(),
                [],
                ResourceGraphCommitSummary.NoChanges(_version)));
        }

        var nextVersion = _version.Next();
        var timestamp = context.Timestamp ?? DateTimeOffset.UtcNow;

        foreach (var accepted in changes.AcceptedResources)
        {
            var acceptedState = accepted.AcceptedState!;
            _resources.TryGetValue(acceptedState.EffectiveResourceId, out var currentState);
            _resources[acceptedState.EffectiveResourceId] =
                acceptedState
                    .WithRevision(acceptedState.Revision.Next()) with
                    {
                        CreatedAt = acceptedState.CreatedAt ?? currentState?.CreatedAt ?? timestamp,
                        LastModifiedAt = timestamp
                    };
        }

        _version = nextVersion;

        var snapshot = CreateSnapshot();

        return ValueTask.FromResult(new ResourceGraphCommitResult(
            _version,
            snapshot,
            [],
            ResourceGraphCommitSummary.Committed(changes, _version, snapshot)));
    }

    private ResourceGraphSnapshot CreateSnapshot() =>
        new(_version, _resources.Values.ToArray());
}
