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

public interface IResourceGraphStoreProjector<TRecord>
{
    string GetResourceId(TRecord record);

    ResourceState ToState(TRecord record);

    TRecord FromState(
        ResourceState state,
        TRecord? currentRecord = default);
}

public sealed class ResourceRecordGraphStoreProjector : IResourceGraphStoreProjector<ResourceRecord>
{
    public string GetResourceId(ResourceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.ResourceId;
    }

    public ResourceState ToState(ResourceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.ToState();
    }

    public ResourceRecord FromState(
        ResourceState state,
        ResourceRecord? currentRecord = null) =>
        ResourceRecord.FromState(state);
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

public class InMemoryProjectedResourceStateProvider<TRecord> : IResourceStateProvider
    where TRecord : notnull
{
    private readonly IResourceGraphStoreProjector<TRecord> _projector;
    private readonly Dictionary<string, TRecord> _records;
    private ResourceGraphVersion _version = ResourceGraphVersion.Initial;

    public InMemoryProjectedResourceStateProvider(
        IResourceGraphStoreProjector<TRecord> projector,
        IEnumerable<TRecord>? records = null)
    {
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _records = (records ?? [])
            .ToDictionary(
                projector.GetResourceId,
                record => record,
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<TRecord> GetRecords() =>
        _records.Values.ToArray();

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
            _records.TryGetValue(acceptedState.EffectiveResourceId, out var currentRecord);
            var currentState = currentRecord is null
                ? null
                : _projector.ToState(currentRecord);
            var committedState = acceptedState
                .WithRevision(acceptedState.Revision.Next()) with
                {
                    CreatedAt = acceptedState.CreatedAt ?? currentState?.CreatedAt ?? timestamp,
                    LastModifiedAt = timestamp
                };

            _records[committedState.EffectiveResourceId] = _projector.FromState(
                committedState,
                currentRecord);
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
        new(_version, _records.Values.Select(_projector.ToState).ToArray());
}

public sealed class InMemoryResourceRecordStateProvider(
    IEnumerable<ResourceRecord>? records = null)
    : InMemoryProjectedResourceStateProvider<ResourceRecord>(
        new ResourceRecordGraphStoreProjector(),
        records);
