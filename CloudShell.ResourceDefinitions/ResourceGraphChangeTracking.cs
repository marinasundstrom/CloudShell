namespace CloudShell.ResourceDefinitions;

public readonly record struct ResourceGraphVersion(long Value)
{
    public static ResourceGraphVersion Initial { get; } = new(0);

    public ResourceGraphVersion Next() => new(Value + 1);

    public override string ToString() => Value.ToString();
}

public interface IResourceStateProvider
{
    ValueTask<ResourceGraphSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ResourceGraphCommitResult> CommitAsync(
        ResourceGraphChangeSet changes,
        ResourceGraphCommitContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceGraphSnapshot(
    ResourceGraphVersion Version,
    IReadOnlyList<ResourceState> Resources);

public sealed record ResourceGraphChangeSet(
    ResourceGraphVersion BaseVersion,
    IReadOnlyList<ResourceChangeApplyResult> Resources,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors =>
        Diagnostics.Any(diagnostic => diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error) ||
        Resources.Any(resource => resource.HasErrors);

    public IReadOnlyList<ResourceChangeApplyResult> AcceptedResources =>
        Resources.Where(resource => resource.IsAccepted).ToArray();

    public IEnumerable<ResourceDefinition> ToIncrementalDefinitions() =>
        AcceptedResources.Select(resource => resource.ChangeSet.ToIncrementalDefinition());
}

public sealed record ResourceGraphCommitContext(
    string? EnvironmentId = null,
    string? PrincipalId = null,
    DateTimeOffset? Timestamp = null);

public sealed record ResourceGraphCommitResult(
    ResourceGraphVersion Version,
    ResourceGraphSnapshot? Snapshot,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics,
    ResourceGraphCommitSummary Summary)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public bool IsCommitted => Snapshot is not null && !HasErrors;
}

public enum ResourceGraphCommitStatus
{
    Committed,
    NoChanges,
    Rejected,
    VersionConflict
}

public sealed record ResourceGraphCommitSummary(
    ResourceGraphCommitStatus Status,
    ResourceGraphVersion BaseVersion,
    ResourceGraphVersion ResultVersion,
    int AcceptedResourceCount,
    int AttributeChangeCount,
    int CapabilityChangeCount,
    IReadOnlyList<ResourceGraphResourceChangeSummary> Resources,
    string Message)
{
    public static ResourceGraphCommitSummary NoChanges(
        ResourceGraphVersion version) =>
        new(
            ResourceGraphCommitStatus.NoChanges,
            version,
            version,
            0,
            0,
            0,
            [],
            "No resource graph changes were committed.");

    public static ResourceGraphCommitSummary Rejected(
        ResourceGraphChangeSet changes,
        ResourceGraphVersion currentVersion) =>
        new(
            ResourceGraphCommitStatus.Rejected,
            changes.BaseVersion,
            currentVersion,
            changes.AcceptedResources.Count,
            CountAttributeChanges(changes),
            CountCapabilityChanges(changes),
            SummarizeAcceptedResources(changes, snapshot: null),
            "Resource graph changes were rejected.");

    public static ResourceGraphCommitSummary VersionConflict(
        ResourceGraphVersion expectedVersion,
        ResourceGraphVersion currentVersion) =>
        new(
            ResourceGraphCommitStatus.VersionConflict,
            expectedVersion,
            currentVersion,
            0,
            0,
            0,
            [],
            $"Resource graph version '{expectedVersion}' is stale. Current version is '{currentVersion}'.");

    public static ResourceGraphCommitSummary Committed(
        ResourceGraphChangeSet changes,
        ResourceGraphVersion committedVersion,
        ResourceGraphSnapshot snapshot) =>
        new(
            ResourceGraphCommitStatus.Committed,
            changes.BaseVersion,
            committedVersion,
            changes.AcceptedResources.Count,
            CountAttributeChanges(changes),
            CountCapabilityChanges(changes),
            SummarizeAcceptedResources(changes, snapshot),
            $"Committed {changes.AcceptedResources.Count} resource change(s).");

    private static int CountAttributeChanges(ResourceGraphChangeSet changes) =>
        changes.AcceptedResources.Sum(resource => resource.ChangeSet.AttributeChanges.Count);

    private static int CountCapabilityChanges(ResourceGraphChangeSet changes) =>
        changes.AcceptedResources.Sum(resource => resource.ChangeSet.CapabilityChanges.Count);

    private static IReadOnlyList<ResourceGraphResourceChangeSummary> SummarizeAcceptedResources(
        ResourceGraphChangeSet changes,
        ResourceGraphSnapshot? snapshot) =>
        changes.AcceptedResources
            .Select(resource =>
            {
                var resourceId = resource.ChangeSet.Resource.EffectiveResourceId;
                var committedState = snapshot?.Resources.FirstOrDefault(state =>
                    string.Equals(
                        state.EffectiveResourceId,
                        resourceId,
                        StringComparison.OrdinalIgnoreCase));

                return new ResourceGraphResourceChangeSummary(
                    resourceId,
                    resource.ChangeSet.Resource.Type.TypeId,
                    resource.ChangeSet.Resource.Revision,
                    committedState?.Revision,
                    resource.ChangeSet.AttributeChanges
                        .Select(change => change.AttributeId)
                        .ToArray(),
                    resource.ChangeSet.CapabilityChanges
                        .Select(change => change.CapabilityId)
                        .ToArray());
            })
            .ToArray();
}

public sealed record ResourceGraphResourceChangeSummary(
    string ResourceId,
    ResourceTypeId TypeId,
    ResourceRevision PreviousRevision,
    ResourceRevision? CommittedRevision,
    IReadOnlyList<ResourceAttributeId> AttributeChanges,
    IReadOnlyList<ResourceCapabilityId> CapabilityChanges);

public sealed class ResourceGraphChangeTracker(ResourceGraphSnapshot snapshot)
{
    private readonly List<ResourceChangeApplyResult> _resources = [];
    private readonly List<ResourceDefinitionDiagnostic> _diagnostics = [];

    public ResourceGraphVersion BaseVersion { get; } = snapshot.Version;

    public void Track(ResourceChangeApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _resources.Add(result);
        _diagnostics.AddRange(result.Diagnostics);
    }

    public ResourceGraphChangeSet GetChanges() =>
        new(BaseVersion, _resources.ToArray(), _diagnostics.ToArray());
}

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
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            _snapshot = await stateProvider.GetSnapshotAsync(cancellationToken);
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

    private static ResourceDefinitionDiagnostic CreateVersionConflict(
        ResourceGraphVersion expected,
        ResourceGraphVersion current) =>
        ResourceDefinitionDiagnostic.Error(
            ResourceDefinitionDiagnosticCodes.ResourceGraphVersionConflict,
            $"Resource graph version '{expected}' is stale. Current version is '{current}'.");
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
