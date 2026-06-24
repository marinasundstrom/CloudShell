namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed class ResourceModelGraphDefinitionApplyService(
    ResourceGraphModel graphModel,
    ResourceDefinitionGraphChangeApplier changeApplier)
{
    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyDefinitionsAsync(
        IEnumerable<ResourceDefinition> definitions,
        ResourceGraphCommitContext commitContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(commitContext);

        var snapshot = await graphModel.GetSnapshotAsync(cancellationToken);
        var changes = await changeApplier.ApplyDefinitionsAsync(
            snapshot,
            definitions,
            new ResourceChangeApplyContext(
                commitContext.EnvironmentId,
                commitContext.PrincipalId,
                Commit: true),
            cancellationToken);
        var commit = await graphModel.CommitAsync(
            changes,
            commitContext,
            cancellationToken);

        return new(snapshot, changes, commit);
    }
}

public sealed record ResourceModelGraphDefinitionApplyResult(
    ResourceGraphSnapshot BaseSnapshot,
    ResourceGraphChangeSet Changes,
    ResourceGraphCommitResult Commit)
{
    public ResourceGraphVersion BaseVersion => BaseSnapshot.Version;

    public bool HasErrors => Changes.HasErrors || Commit.HasErrors;

    public bool IsCommitted => Commit.IsCommitted;

    public IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics =>
        Commit.Diagnostics.Count > 0 ? Commit.Diagnostics : Changes.Diagnostics;
}
