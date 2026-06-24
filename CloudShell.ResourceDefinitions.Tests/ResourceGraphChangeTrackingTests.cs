using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceGraphChangeTrackingTests
{
    [Fact]
    public async Task CommitAsync_PersistsAcceptedChangesWithSingleGraphVersion()
    {
        var createdAt = new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);
        var committedAt = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var stateProvider = new InMemoryResourceStateProvider(
            [CreateState("api", "./api"), CreateState("worker", "./worker", version: "4", createdAt: createdAt)]);
        var snapshot = await stateProvider.GetSnapshotAsync();
        var tracker = new ResourceGraphChangeTracker(snapshot);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);

        tracker.Track(await StageExecutablePathChangeAsync(
            snapshot,
            "application.executable:api",
            "./api-v2",
            applyDispatcher));
        tracker.Track(await StageExecutablePathChangeAsync(
            snapshot,
            "application.executable:worker",
            "./worker-v2",
            applyDispatcher));

        var commit = await stateProvider.CommitAsync(
            tracker.GetChanges(),
            new ResourceGraphCommitContext("local", "developer", committedAt));

        Assert.True(commit.IsCommitted);
        Assert.Equal(new ResourceGraphVersion(1), commit.Version);
        Assert.Equal(ResourceGraphCommitStatus.Committed, commit.Summary.Status);
        Assert.Equal(ResourceGraphVersion.Initial, commit.Summary.BaseVersion);
        Assert.Equal(new ResourceGraphVersion(1), commit.Summary.ResultVersion);
        Assert.Equal(2, commit.Summary.AcceptedResourceCount);
        Assert.Equal(2, commit.Summary.AttributeChangeCount);
        Assert.Equal(0, commit.Summary.CapabilityChangeCount);
        Assert.Equal(2, commit.Summary.Resources.Count);
        Assert.NotNull(commit.Snapshot);
        Assert.Equal("1", FindState(commit.Snapshot, "application.executable:api").Version);
        Assert.Equal("5", FindState(commit.Snapshot, "application.executable:worker").Version);
        var workerSummary = commit.Summary.Resources.Single(resource =>
            resource.ResourceId == "application.executable:worker");
        Assert.Equal(new ResourceRevision(4), workerSummary.PreviousRevision);
        Assert.Equal(new ResourceRevision(5), workerSummary.CommittedRevision);
        Assert.Contains(
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath,
            workerSummary.AttributeChanges);
        Assert.Equal(committedAt, FindState(commit.Snapshot, "application.executable:api").CreatedAt);
        Assert.Equal(createdAt, FindState(commit.Snapshot, "application.executable:worker").CreatedAt);
        Assert.All(commit.Snapshot.Resources, resource =>
            Assert.Equal(committedAt, resource.LastModifiedAt));
        Assert.Equal("./api-v2", FindState(commit.Snapshot, "application.executable:api")
            .ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Equal("./worker-v2", FindState(commit.Snapshot, "application.executable:worker")
            .ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task CommitAsync_PersistsResourceRevisionSeparatelyFromGraphVersion()
    {
        var committedAt = new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero);
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api", version: "9")]);
        var snapshot = await stateProvider.GetSnapshotAsync();
        var tracker = new ResourceGraphChangeTracker(snapshot);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);

        tracker.Track(await StageExecutablePathChangeAsync(
            snapshot,
            "application.executable:api",
            "./api-v2",
            applyDispatcher));

        var commit = await stateProvider.CommitAsync(
            tracker.GetChanges(),
            new ResourceGraphCommitContext(Timestamp: committedAt));

        Assert.Equal(new ResourceGraphVersion(1), commit.Version);
        Assert.NotNull(commit.Snapshot);
        Assert.Equal(new ResourceRevision(10), FindState(commit.Snapshot, "application.executable:api").Revision);
        Assert.Equal("10", FindState(commit.Snapshot, "application.executable:api").Version);
        Assert.Equal(committedAt, FindState(commit.Snapshot, "application.executable:api").LastModifiedAt);

        var projected = Resolve(FindState(commit.Snapshot, "application.executable:api"));
        Assert.Equal("10", projected.Version);
        Assert.Equal(new ResourceRevision(10), projected.Revision);
        Assert.Equal(committedAt, projected.CreatedAt);
        Assert.Equal(committedAt, projected.LastModifiedAt);
    }

    [Fact]
    public async Task CommitAsync_CanPersistOnlyIncrementalDefinitionsFromChangeSet()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var snapshot = await stateProvider.GetSnapshotAsync();
        var tracker = new ResourceGraphChangeTracker(snapshot);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);

        tracker.Track(await StageExecutablePathChangeAsync(
            snapshot,
            "application.executable:api",
            "./api-v2",
            applyDispatcher));

        var changes = tracker.GetChanges();
        var incremental = Assert.Single(changes.ToIncrementalDefinitions());

        Assert.Equal("api", incremental.Name);
        Assert.Equal("./api-v2", incremental.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Empty(incremental.CapabilityPayloads);
    }

    [Fact]
    public async Task CommitAsync_RejectsStaleGraphVersion()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var snapshot = await stateProvider.GetSnapshotAsync();
        var tracker = new ResourceGraphChangeTracker(snapshot);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);

        tracker.Track(await StageExecutablePathChangeAsync(
            snapshot,
            "application.executable:api",
            "./api-v2",
            applyDispatcher));
        var changes = tracker.GetChanges();

        Assert.True((await stateProvider.CommitAsync(
            changes,
            new ResourceGraphCommitContext())).IsCommitted);

        var staleCommit = await stateProvider.CommitAsync(
            changes,
            new ResourceGraphCommitContext());

        var diagnostic = Assert.Single(staleCommit.Diagnostics);
        Assert.False(staleCommit.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.VersionConflict, staleCommit.Summary.Status);
        Assert.Equal(ResourceGraphVersion.Initial, staleCommit.Summary.BaseVersion);
        Assert.Equal(new ResourceGraphVersion(1), staleCommit.Summary.ResultVersion);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceGraphVersionConflict, diagnostic.Code);
    }

    [Fact]
    public async Task CommitAsync_RejectsGraphWhenAnyResourceChangeWasRejected()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var snapshot = await stateProvider.GetSnapshotAsync();
        var tracker = new ResourceGraphChangeTracker(snapshot);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);

        tracker.Track(await StageExecutablePathChangeAsync(
            snapshot,
            "application.executable:api",
            "",
            applyDispatcher));

        var commit = await stateProvider.CommitAsync(
            tracker.GetChanges(),
            new ResourceGraphCommitContext());

        var diagnostic = Assert.Single(commit.Diagnostics);
        Assert.False(commit.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Rejected, commit.Summary.Status);
        Assert.Equal(0, commit.Summary.AcceptedResourceCount);
        Assert.Equal(0, commit.Summary.AttributeChangeCount);
        Assert.Equal("application.executable.pathRequired", diagnostic.Code);
        Assert.Equal(new ResourceGraphVersion(0), commit.Version);
    }

    [Fact]
    public async Task CommitAsync_DoesNotAdvanceVersionWhenThereAreNoChanges()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var snapshot = await stateProvider.GetSnapshotAsync();
        var tracker = new ResourceGraphChangeTracker(snapshot);

        var commit = await stateProvider.CommitAsync(
            tracker.GetChanges(),
            new ResourceGraphCommitContext());

        Assert.True(commit.IsCommitted);
        Assert.Equal(ResourceGraphVersion.Initial, commit.Version);
        Assert.Equal(ResourceGraphCommitStatus.NoChanges, commit.Summary.Status);
        Assert.Equal(0, commit.Summary.AcceptedResourceCount);
        Assert.Empty(commit.Summary.Resources);
    }

    [Fact]
    public async Task ResourceGraphModel_KeepsInMemorySnapshotInSyncAfterCommit()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var model = new ResourceGraphModel(stateProvider);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);
        var tracker = await model.CreateChangeTrackerAsync();
        var snapshot = await model.GetSnapshotAsync();

        tracker.Track(await StageExecutablePathChangeAsync(
            snapshot,
            "application.executable:api",
            "./api-v2",
            applyDispatcher));

        var commit = await model.CommitAsync(
            tracker.GetChanges(),
            new ResourceGraphCommitContext());
        var current = await model.GetSnapshotAsync();

        Assert.True(commit.IsCommitted);
        Assert.Equal(new ResourceGraphVersion(1), current.Version);
        Assert.Equal("./api-v2", FindState(current, "application.executable:api")
            .ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceGraphTransaction_StagesChangesAndCommitsThroughModel()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var model = new ResourceGraphModel(stateProvider);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);

        await using var transaction = await model.BeginTransactionAsync();
        transaction.Track(await StageExecutablePathChangeAsync(
            transaction.Snapshot,
            "application.executable:api",
            "./api-transaction",
            applyDispatcher));

        var changes = transaction.GetChanges();
        var commit = await transaction.CommitAsync(new ResourceGraphCommitContext());
        var current = await model.GetSnapshotAsync();

        Assert.Equal(ResourceGraphTransactionMode.Optimistic, transaction.Mode);
        Assert.False(transaction.IsExclusive);
        Assert.Equal(ResourceGraphVersion.Initial, transaction.BaseVersion);
        Assert.True(changes.HasChanges);
        Assert.True(commit.IsCommitted);
        Assert.Equal(new ResourceGraphVersion(1), current.Version);
        Assert.Equal("./api-transaction", FindState(current, "application.executable:api")
            .ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceGraphTransaction_CannotBeReusedAfterCommit()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var model = new ResourceGraphModel(stateProvider);

        await using var transaction = await model.BeginTransactionAsync();

        Assert.True((await transaction.CommitAsync(new ResourceGraphCommitContext())).IsCommitted);
        Assert.Throws<InvalidOperationException>(() => transaction.GetChanges());
    }

    [Fact]
    public async Task ResourceGraphTransaction_ExclusiveBlocksGraphAccessUntilDisposed()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var model = new ResourceGraphModel(stateProvider);

        await using var transaction = await model.BeginTransactionAsync(
            ResourceGraphTransactionOptions.Exclusive);
        var blockedSnapshot = model.GetSnapshotAsync().AsTask();

        await Task.Delay(50);

        Assert.False(blockedSnapshot.IsCompleted);

        await transaction.DisposeAsync();
        var snapshot = await blockedSnapshot;

        Assert.Equal(ResourceGraphTransactionMode.Exclusive, transaction.Mode);
        Assert.True(transaction.IsExclusive);
        Assert.Equal(transaction.BaseVersion, snapshot.Version);
    }

    [Fact]
    public async Task ResourceGraphTransaction_ExclusiveBlocksGraphCommitUntilDisposed()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var model = new ResourceGraphModel(stateProvider);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);

        await using var transaction = await model.BeginTransactionAsync(
            ResourceGraphTransactionOptions.Exclusive);
        var tracker = new ResourceGraphChangeTracker(transaction.Snapshot);
        tracker.Track(await StageExecutablePathChangeAsync(
            transaction.Snapshot,
            "application.executable:api",
            "./api-blocked",
            applyDispatcher));
        var blockedCommit = model
            .CommitAsync(
                tracker.GetChanges(),
                new ResourceGraphCommitContext())
            .AsTask();

        await Task.Delay(50);

        Assert.False(blockedCommit.IsCompleted);

        await transaction.DisposeAsync();
        var commit = await blockedCommit;

        Assert.True(commit.IsCommitted);
    }

    [Fact]
    public async Task ResourceGraphModel_RejectsStaleTrackedChangesBeforeProviderCommit()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var model = new ResourceGraphModel(stateProvider);
        var staleTracker = await model.CreateChangeTrackerAsync();
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);
        var snapshot = await model.GetSnapshotAsync();

        staleTracker.Track(await StageExecutablePathChangeAsync(
            snapshot,
            "application.executable:api",
            "./api-v2",
            applyDispatcher));

        var currentTracker = await model.CreateChangeTrackerAsync();
        currentTracker.Track(await StageExecutablePathChangeAsync(
            snapshot,
            "application.executable:api",
            "./api-v3",
            applyDispatcher));
        Assert.True((await model.CommitAsync(
            currentTracker.GetChanges(),
            new ResourceGraphCommitContext())).IsCommitted);

        var staleCommit = await model.CommitAsync(
            staleTracker.GetChanges(),
            new ResourceGraphCommitContext());

        var diagnostic = Assert.Single(staleCommit.Diagnostics);
        Assert.False(staleCommit.IsCommitted);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceGraphVersionConflict, diagnostic.Code);
    }

    [Fact]
    public async Task ResourceGraphModel_ChecksStoreVersionBeforeCommit()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var model = new ResourceGraphModel(stateProvider);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);
        var staleSnapshot = await model.GetSnapshotAsync();
        var staleTracker = new ResourceGraphChangeTracker(staleSnapshot);
        staleTracker.Track(await StageExecutablePathChangeAsync(
            staleSnapshot,
            "application.executable:api",
            "./api-from-model",
            applyDispatcher));

        var storeSnapshot = await stateProvider.GetSnapshotAsync();
        var storeTracker = new ResourceGraphChangeTracker(storeSnapshot);
        storeTracker.Track(await StageExecutablePathChangeAsync(
            storeSnapshot,
            "application.executable:api",
            "./api-from-store",
            applyDispatcher));
        Assert.True((await stateProvider.CommitAsync(
            storeTracker.GetChanges(),
            new ResourceGraphCommitContext())).IsCommitted);

        var staleCommit = await model.CommitAsync(
            staleTracker.GetChanges(),
            new ResourceGraphCommitContext());
        var current = await model.GetSnapshotAsync();

        Assert.False(staleCommit.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.VersionConflict, staleCommit.Summary.Status);
        Assert.Equal(new ResourceGraphVersion(1), staleCommit.Summary.ResultVersion);
        Assert.Equal(new ResourceGraphVersion(1), current.Version);
        Assert.Equal("./api-from-store", FindState(current, "application.executable:api")
            .ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceGraphModel_ReloadsSnapshotFromStateProvider()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var model = new ResourceGraphModel(stateProvider);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);
        var original = await model.GetSnapshotAsync();
        var providerSnapshot = await stateProvider.GetSnapshotAsync();
        var providerTracker = new ResourceGraphChangeTracker(providerSnapshot);

        Assert.Equal(ResourceGraphVersion.Initial, original.Version);

        providerTracker.Track(await StageExecutablePathChangeAsync(
            providerSnapshot,
            "application.executable:api",
            "./api-provider",
            applyDispatcher));
        Assert.True((await stateProvider.CommitAsync(
            providerTracker.GetChanges(),
            new ResourceGraphCommitContext())).IsCommitted);

        var reloaded = await model.ReloadAsync();

        Assert.Equal(new ResourceGraphVersion(1), reloaded.Version);
        Assert.Equal("./api-provider", FindState(reloaded, "application.executable:api")
            .ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceGraphModel_FullRefreshAdvancesCachedGraphVersion()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var model = new ResourceGraphModel(stateProvider);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);
        var original = await model.GetSnapshotAsync();
        var storeTracker = new ResourceGraphChangeTracker(await stateProvider.GetSnapshotAsync());

        storeTracker.Track(await StageExecutablePathChangeAsync(
            original,
            "application.executable:api",
            "./api-store",
            applyDispatcher));
        Assert.True((await stateProvider.CommitAsync(
            storeTracker.GetChanges(),
            new ResourceGraphCommitContext())).IsCommitted);

        var refreshed = await model.RefreshAsync(ResourceGraphRefreshContext.Full);

        Assert.Equal(new ResourceGraphVersion(1), refreshed.Version);
        Assert.Equal("./api-store", FindState(refreshed, "application.executable:api")
            .ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceGraphModel_PartialRefreshKeepsStaleGraphVersionForCommit()
    {
        var stateProvider = new InMemoryResourceStateProvider(
            [CreateState("api", "./api"), CreateState("worker", "./worker")]);
        var model = new ResourceGraphModel(stateProvider);
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);
        var original = await model.GetSnapshotAsync();
        var storeTracker = new ResourceGraphChangeTracker(await stateProvider.GetSnapshotAsync());

        storeTracker.Track(await StageExecutablePathChangeAsync(
            original,
            "application.executable:api",
            "./api-store",
            applyDispatcher));
        Assert.True((await stateProvider.CommitAsync(
            storeTracker.GetChanges(),
            new ResourceGraphCommitContext())).IsCommitted);

        var partiallyRefreshed = await model.RefreshAsync(
            new ResourceGraphRefreshContext(["application.executable:api"]));
        var staleTracker = await model.CreateChangeTrackerAsync();
        staleTracker.Track(await StageExecutablePathChangeAsync(
            partiallyRefreshed,
            "application.executable:worker",
            "./worker-model",
            applyDispatcher));

        var staleCommit = await model.CommitAsync(
            staleTracker.GetChanges(),
            new ResourceGraphCommitContext());

        Assert.Equal(ResourceGraphVersion.Initial, partiallyRefreshed.Version);
        Assert.Equal("./api-store", FindState(partiallyRefreshed, "application.executable:api")
            .ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.False(staleCommit.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.VersionConflict, staleCommit.Summary.Status);
    }

    private static async ValueTask<ResourceChangeApplyResult> StageExecutablePathChangeAsync(
        ResourceGraphSnapshot snapshot,
        string resourceId,
        string executablePath,
        ResourceChangeApplyDispatcher applyDispatcher)
    {
        var resource = Resolve(FindState(snapshot, resourceId));
        resource.SetAttribute(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, executablePath);
        return await applyDispatcher.ApplyChangesAsync(
            resource.ApplyChanges(),
            new ResourceChangeApplyContext(Commit: true));
    }

    private static ResourceState FindState(
        ResourceGraphSnapshot snapshot,
        string resourceId) =>
        snapshot.Resources.Single(resource =>
            string.Equals(resource.EffectiveResourceId, resourceId, StringComparison.OrdinalIgnoreCase));

    private static Resource Resolve(ResourceState state)
    {
        var typeProvider = new ExecutableApplicationResourceTypeProvider();
        var resolver = new ResourceResolver(
            [new ResourceClassDefinition(ExecutableApplicationResourceTypeProvider.ClassId)],
            [typeProvider.TypeDefinition]);

        return resolver.Resolve(state);
    }

    private static ResourceState CreateState(
        string name,
        string executablePath,
        string? version = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? lastModifiedAt = null) =>
        new(
            name,
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Version: version,
            CreatedAt: createdAt,
            LastModifiedAt: lastModifiedAt,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = executablePath
            });
}
