namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed class ResourceModelGraphDefinitionApplyService(
    ResourceGraphModel graphModel,
    ResourceDefinitionGraphChangeApplier changeApplier)
{
    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyDefinitionsAsync(
        IEnumerable<ResourceDefinition> definitions,
        ResourceGraphCommitContext commitContext,
        CancellationToken cancellationToken = default) =>
        await ApplyDefinitionsAsync(
            definitions,
            commitContext,
            ResourceModelGraphDefinitionApplyOptions.Default,
            cancellationToken);

    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyDefinitionsAsync(
        IEnumerable<ResourceDefinition> definitions,
        ResourceGraphCommitContext commitContext,
        ResourceModelGraphDefinitionApplyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(commitContext);
        ArgumentNullException.ThrowIfNull(options);

        var snapshot = await graphModel.GetSnapshotAsync(cancellationToken);
        var changes = await changeApplier.ApplyDefinitionsAsync(
            snapshot,
            definitions,
            new ResourceChangeApplyContext(
                commitContext.EnvironmentId,
                commitContext.PrincipalId,
                Commit: true),
            new ResourceDefinitionGraphChangeApplierOptions(options.CreateMissingResources),
            cancellationToken);
        var commit = await graphModel.CommitAsync(
            changes,
            commitContext,
            cancellationToken);

        return new(snapshot, changes, commit);
    }

    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyDeploymentAsync(
        ResourceDeploymentDefinition deployment,
        ResourceGraphCommitContext commitContext,
        CancellationToken cancellationToken = default) =>
        await ApplyDeploymentAsync(
            deployment,
            commitContext,
            ResourceModelGraphDefinitionApplyOptions.CreateMissing,
            cancellationToken);

    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyDeploymentAsync(
        ResourceDeploymentDefinition deployment,
        ResourceGraphCommitContext commitContext,
        ResourceModelGraphDefinitionApplyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(commitContext);
        ArgumentNullException.ThrowIfNull(options);

        var effectiveCommitContext = string.IsNullOrWhiteSpace(commitContext.EnvironmentId) &&
            !string.IsNullOrWhiteSpace(deployment.EnvironmentId)
                ? commitContext with { EnvironmentId = deployment.EnvironmentId }
                : commitContext;

        return await ApplyDefinitionsAsync(
            deployment.Resources,
            effectiveCommitContext,
            options,
            cancellationToken);
    }
}

public sealed record ResourceModelGraphDefinitionApplyOptions(
    bool CreateMissingResources = false)
{
    public static ResourceModelGraphDefinitionApplyOptions Default { get; } = new();

    public static ResourceModelGraphDefinitionApplyOptions CreateMissing { get; } =
        new(CreateMissingResources: true);
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
