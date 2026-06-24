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
    string? PrincipalId = null);

public sealed record ResourceGraphCommitResult(
    ResourceGraphVersion Version,
    ResourceGraphSnapshot? Snapshot,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public bool IsCommitted => Snapshot is not null && !HasErrors;
}

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
            var snapshot = await GetSnapshotCoreAsync(cancellationToken);
            if (changes.BaseVersion != snapshot.Version)
            {
                return new ResourceGraphCommitResult(
                    snapshot.Version,
                    null,
                    [CreateVersionConflict(changes.BaseVersion, snapshot.Version)]);
            }

            var result = await stateProvider.CommitAsync(
                changes,
                context,
                cancellationToken);

            if (result.IsCommitted)
            {
                _snapshot = result.Snapshot;
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
                changes.Diagnostics));
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
                ]));
        }

        if (changes.AcceptedResources.Count == 0)
        {
            return ValueTask.FromResult(new ResourceGraphCommitResult(
                _version,
                CreateSnapshot(),
                []));
        }

        var nextVersion = _version.Next();

        foreach (var accepted in changes.AcceptedResources)
        {
            _resources[accepted.AcceptedState!.EffectiveResourceId] =
                accepted.AcceptedState with { Version = nextVersion.ToString() };
        }

        _version = nextVersion;

        return ValueTask.FromResult(new ResourceGraphCommitResult(
            _version,
            CreateSnapshot(),
            []));
    }

    private ResourceGraphSnapshot CreateSnapshot() =>
        new(_version, _resources.Values.ToArray());
}
