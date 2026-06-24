using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceGraphChangeTrackingTests
{
    [Fact]
    public async Task CommitAsync_PersistsAcceptedChangesWithSingleGraphVersion()
    {
        var stateProvider = new InMemoryResourceStateProvider(
            [CreateState("api", "./api"), CreateState("worker", "./worker")]);
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
            new ResourceGraphCommitContext("local", "developer"));

        Assert.True(commit.IsCommitted);
        Assert.Equal(new ResourceGraphVersion(1), commit.Version);
        Assert.NotNull(commit.Snapshot);
        Assert.All(commit.Snapshot.Resources, resource =>
            Assert.Equal("1", resource.Version));
        Assert.Equal("./api-v2", FindState(commit.Snapshot, "application.executable:api")
            .ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Equal("./worker-v2", FindState(commit.Snapshot, "application.executable:worker")
            .ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
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
        string executablePath) =>
        new(
            name,
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = executablePath
            });
}
