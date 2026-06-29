using System.Text.Json;
using CloudShell.ResourceModel.ReferenceProviders;

namespace CloudShell.ResourceModel.Tests;

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
    public async Task RecordStateProvider_RehydratesRecordsAndCommitsBackToRecords()
    {
        var createdAt = new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);
        var committedAt = new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero);
        var stateProvider = new InMemoryResourceRecordStateProvider(
            [ResourceRecord.FromState(CreateState("api", "./api", version: "3", createdAt: createdAt))]);
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

        var record = Assert.Single(stateProvider.GetRecords());
        var committed = record.ToState();
        Assert.True(commit.IsCommitted);
        Assert.Equal(new ResourceGraphVersion(1), commit.Version);
        Assert.Equal("4", record.Version);
        Assert.Equal("4", committed.Version);
        Assert.Equal(createdAt, record.CreatedAt);
        Assert.Equal(committedAt, record.LastModifiedAt);
        Assert.Equal("./api-v2", committed.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ProjectedStateProvider_CommitsGraphPayloadWithoutDroppingStoreOwnedFields()
    {
        var committedAt = new DateTimeOffset(2026, 6, 24, 13, 30, 0, TimeSpan.Zero);
        var stateProvider = new InMemoryProjectedResourceStateProvider<ResourceManagerResourceRow>(
            new ResourceManagerResourceRowProjector(),
            [
                new(
                    "application.executable:api",
                    ResourceDefinitionJson.FromValue(
                        ResourceRecord.FromState(CreateState("api", "./api", version: "3"))),
                    OperationalState: "Running")
            ]);
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

        var row = Assert.Single(stateProvider.GetRecords());
        var committed = row.GraphData.Deserialize<ResourceRecord>()!.ToState();
        Assert.True(commit.IsCommitted);
        Assert.Equal("Running", row.OperationalState);
        Assert.Equal(new ResourceRevision(4), committed.Revision);
        Assert.Equal(committedAt, committed.LastModifiedAt);
        Assert.Equal("./api-v2", committed.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
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
    public async Task CommitAsync_CanPersistAcceptedResourceDefinitionOverlay()
    {
        var committedAt = new DateTimeOffset(2026, 6, 24, 14, 0, 0, TimeSpan.Zero);
        var stateProvider = new InMemoryResourceStateProvider(
        [
            CreateState("api", "./api", version: "2") with
            {
                Attributes = new Dictionary<ResourceAttributeId, string>
                {
                    [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "./api",
                    ["container.replicas"] = "1"
                }
            }
        ]);
        var snapshot = await stateProvider.GetSnapshotAsync();
        var tracker = new ResourceGraphChangeTracker(snapshot);
        var resource = Resolve(FindState(snapshot, "application.executable:api"));
        var incoming = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "./api-v2"
            });
        var applyDispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);

        tracker.Track(await applyDispatcher.ApplyChangesAsync(
            resource.ApplyDefinition(incoming),
            new ResourceChangeApplyContext(Commit: true)));
        var commit = await stateProvider.CommitAsync(
            tracker.GetChanges(),
            new ResourceGraphCommitContext(Timestamp: committedAt));

        var committed = FindState(commit.Snapshot!, "application.executable:api");
        Assert.True(commit.IsCommitted);
        Assert.Equal(new ResourceGraphVersion(1), commit.Version);
        Assert.Equal(new ResourceRevision(3), committed.Revision);
        Assert.Equal("./api-v2", committed.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Equal("1", committed.ResourceAttributes["container.replicas"]);
        Assert.Equal(committedAt, committed.LastModifiedAt);
    }

    [Fact]
    public async Task DefinitionGraphChangeApplier_StagesDefinitionOverlaysForCommit()
    {
        var stateProvider = new InMemoryResourceStateProvider(
        [
            CreateState("api", "./api", version: "2") with
            {
                Attributes = new Dictionary<ResourceAttributeId, string>
                {
                    [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "./api",
                    ["container.replicas"] = "1"
                }
            }
        ]);
        var applier = CreateDefinitionGraphChangeApplier();
        var snapshot = await stateProvider.GetSnapshotAsync();
        var changes = await applier.ApplyDefinitionsAsync(
            snapshot,
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "./api-v2"
                    })
            ],
            new ResourceChangeApplyContext(Commit: true));

        var commit = await stateProvider.CommitAsync(
            changes,
            new ResourceGraphCommitContext(Timestamp: new DateTimeOffset(2026, 6, 24, 15, 0, 0, TimeSpan.Zero)));

        var committed = FindState(commit.Snapshot!, "application.executable:api");
        Assert.False(changes.HasErrors);
        Assert.Single(changes.AcceptedResources);
        Assert.True(commit.IsCommitted);
        Assert.Equal(new ResourceRevision(3), committed.Revision);
        Assert.Equal("./api-v2", committed.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Equal("1", committed.ResourceAttributes["container.replicas"]);
    }

    [Fact]
    public async Task DefinitionGraphChangeApplier_ReturnsDiagnosticForMissingResource()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var applier = CreateDefinitionGraphChangeApplier();
        var snapshot = await stateProvider.GetSnapshotAsync();

        var changes = await applier.ApplyDefinitionsAsync(
            snapshot,
            [
                new(
                    "worker",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "./worker"
                    })
            ],
            new ResourceChangeApplyContext(Commit: true));

        var diagnostic = Assert.Single(changes.Diagnostics);
        Assert.True(changes.HasErrors);
        Assert.Empty(changes.Resources);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing, diagnostic.Code);
        Assert.Equal("application.executable:worker", diagnostic.Target);
    }

    [Fact]
    public async Task DefinitionGraphChangeApplier_StagesNewResourceWhenAllowed()
    {
        var stateProvider = new InMemoryResourceStateProvider();
        var applier = CreateDefinitionGraphChangeApplier();
        var snapshot = await stateProvider.GetSnapshotAsync();

        var changes = await applier.ApplyDefinitionsAsync(
            snapshot,
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "./api"
                    })
            ],
            new ResourceChangeApplyContext(Commit: true),
            ResourceDefinitionGraphChangeApplierOptions.CreateMissing);

        var commit = await stateProvider.CommitAsync(
            changes,
            new ResourceGraphCommitContext(Timestamp: new DateTimeOffset(2026, 6, 24, 15, 30, 0, TimeSpan.Zero)));

        var created = Assert.Single(commit.Snapshot!.Resources);
        Assert.False(changes.HasErrors);
        Assert.True(changes.HasChanges);
        Assert.True(Assert.Single(changes.AcceptedResources).ChangeSet.IsNewResource);
        Assert.True(commit.IsCommitted);
        Assert.Equal(new ResourceRevision(1), created.Revision);
        Assert.Equal("./api", created.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task DefinitionGraphChangeApplier_PassesApplyContextToResourceResolution()
    {
        var stateProvider = new InMemoryResourceStateProvider();
        var applier = CreateDefinitionGraphChangeApplier(
            new PrincipalAttributeValidator("developer"));
        var snapshot = await stateProvider.GetSnapshotAsync();

        var changes = await applier.ApplyDefinitionsAsync(
            snapshot,
            [CreateDefinition("api", "./api")],
            new ResourceChangeApplyContext("local", "developer", Commit: true),
            ResourceDefinitionGraphChangeApplierOptions.CreateMissing);

        Assert.False(changes.HasErrors);
        Assert.True(Assert.Single(changes.AcceptedResources).IsAccepted);
    }

    [Fact]
    public async Task DefinitionGraphChangeApplier_RejectsDuplicateIncomingDefinitions()
    {
        var stateProvider = new InMemoryResourceStateProvider();
        var applier = CreateDefinitionGraphChangeApplier();
        var snapshot = await stateProvider.GetSnapshotAsync();

        var changes = await applier.ApplyDefinitionsAsync(
            snapshot,
            [
                CreateDefinition("api", "./api"),
                CreateDefinition("api", "./api-duplicate")
            ],
            new ResourceChangeApplyContext(Commit: true),
            ResourceDefinitionGraphChangeApplierOptions.CreateMissing);

        var diagnostic = Assert.Single(changes.Diagnostics);
        Assert.True(changes.HasErrors);
        Assert.False(changes.HasChanges);
        Assert.Empty(changes.Resources);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.DuplicateResourceDefinition, diagnostic.Code);
        Assert.Equal("application.executable:api", diagnostic.Target);
    }

    [Fact]
    public async Task DefinitionGraphChangeApplier_RejectsMissingDependencies()
    {
        var stateProvider = new InMemoryResourceStateProvider([CreateState("api", "./api")]);
        var applier = CreateDefinitionGraphChangeApplier();
        var snapshot = await stateProvider.GetSnapshotAsync();

        var changes = await applier.ApplyDefinitionsAsync(
            snapshot,
            [
                CreateDefinition(
                    "worker",
                    "./worker",
                    dependsOn: ["application.executable:api", "storage:missing"])
            ],
            new ResourceChangeApplyContext(Commit: true),
            ResourceDefinitionGraphChangeApplierOptions.CreateMissing);

        var diagnostic = Assert.Single(changes.Diagnostics);
        Assert.True(changes.HasErrors);
        Assert.False(changes.HasChanges);
        Assert.Empty(changes.Resources);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceDependencyMissing, diagnostic.Code);
        Assert.Equal("application.executable:worker", diagnostic.Target);
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

    private static ResourceDefinitionGraphChangeApplier CreateDefinitionGraphChangeApplier(
        params IResourceAttributeValidator[] attributeValidators)
    {
        var typeProvider = new ExecutableApplicationResourceTypeProvider();
        return new(
            new ResourceResolver(
                [new ResourceClassDefinition(ExecutableApplicationResourceTypeProvider.ClassId)],
                [typeProvider.TypeDefinition],
                attributeValidators),
            new ResourceChangeApplyDispatcher([typeProvider]));
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

    private static ResourceDefinition CreateDefinition(
        string name,
        string executablePath,
        IReadOnlyList<string>? dependsOn = null) =>
        new(
            name,
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            DependsOn: ToReferences(dependsOn),
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = executablePath
            });

    private static IReadOnlyList<ResourceReference>? ToReferences(
        IReadOnlyList<string>? resourceIds) =>
        resourceIds?.Select(resourceId => ResourceReference.DependsOnResourceId(resourceId)).ToArray();

    private sealed record ResourceManagerResourceRow(
        string ResourceId,
        JsonElement GraphData,
        string OperationalState);

    private sealed class ResourceManagerResourceRowProjector :
        IResourceGraphStoreProjector<ResourceManagerResourceRow>
    {
        public string GetResourceId(ResourceManagerResourceRow record) =>
            record.ResourceId;

        public ResourceState ToState(ResourceManagerResourceRow record) =>
            record.GraphData.Deserialize<ResourceRecord>()?.ToState() ??
            throw new InvalidOperationException("Resource graph payload could not be read.");

        public ResourceManagerResourceRow FromState(
            ResourceState state,
            ResourceManagerResourceRow? currentRecord = null) =>
            (currentRecord ?? new(
                state.EffectiveResourceId,
                ResourceDefinitionJson.EmptyObject,
                OperationalState: "Unknown")) with
            {
                ResourceId = state.EffectiveResourceId,
                GraphData = ResourceDefinitionJson.FromValue(ResourceRecord.FromState(state))
            };
    }

    private sealed class PrincipalAttributeValidator(
        string expectedPrincipalId) : IResourceAttributeValidator
    {
        public bool CanValidate(
            ResourceAttributeResolution attribute,
            ResourceAttributeValidationContext context) =>
            attribute.Name == ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath;

        public ResourceDefinitionValidationResult Validate(
            ResourceAttributeResolution attribute,
            ResourceAttributeValidationContext context) =>
            string.Equals(
                context.ResolutionContext.PrincipalId,
                expectedPrincipalId,
                StringComparison.Ordinal)
                    ? ResourceDefinitionValidationResult.Success
                    : ResourceDefinitionValidationResult.FromDiagnostics(
                        [
                            ResourceDefinitionDiagnostic.Error(
                                "test.principalMissing",
                                "The expected principal was not passed to resource resolution.",
                                attribute.Name)
                        ]);
    }
}
