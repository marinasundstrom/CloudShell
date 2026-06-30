namespace CloudShell.ControlPlane.ResourceModel;

public sealed class ResourceModelGraphDefinitionApplyService(
    ResourceGraphModel graphModel,
    ResourceDefinitionGraphChangeApplier changeApplier,
    IEnumerable<IResourceModelGraphApplyReconciler>? reconcilers = null)
{
    private readonly IReadOnlyList<IResourceModelGraphApplyReconciler> _reconcilers =
        (reconcilers ?? []).ToArray();

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
            new ResourceDefinitionGraphChangeApplierOptions(options.Mode),
            cancellationToken);
        var commit = await graphModel.CommitAsync(
            changes,
            commitContext,
            cancellationToken);

        var reconciliation = await ReconcileAsync(
            snapshot,
            changes,
            commit,
            commitContext,
            options,
            cancellationToken);

        return new(snapshot, changes, commit, reconciliation);
    }

    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyTemplateAsync(
        ResourceTemplate template,
        ResourceGraphCommitContext commitContext,
        CancellationToken cancellationToken = default) =>
        await ApplyTemplateAsync(
            template,
            commitContext,
            ResourceModelGraphDefinitionApplyOptions.CreateMissing,
            cancellationToken);

    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyTemplateAsync(
        ResourceTemplate template,
        ResourceGraphCommitContext commitContext,
        ResourceModelGraphDefinitionApplyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(commitContext);
        ArgumentNullException.ThrowIfNull(options);

        var effectiveCommitContext = string.IsNullOrWhiteSpace(commitContext.EnvironmentId) &&
            !string.IsNullOrWhiteSpace(template.EnvironmentId)
                ? commitContext with { EnvironmentId = template.EnvironmentId }
                : commitContext;

        return await ApplyDefinitionsAsync(
            template.Resources,
            effectiveCommitContext,
            options,
            cancellationToken);
    }

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
        ResourceGraphSnapshot snapshot,
        ResourceGraphChangeSet changes,
        ResourceGraphCommitResult commit,
        ResourceGraphCommitContext commitContext,
        ResourceModelGraphDefinitionApplyOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.ReconcileRuntime ||
            !commit.IsCommitted ||
            commit.Summary.Status != ResourceGraphCommitStatus.Committed ||
            _reconcilers.Count == 0)
        {
            return [];
        }

        var context = new ResourceModelGraphDefinitionApplyReconciliationContext(
            snapshot,
            changes,
            commit,
            commitContext);
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var reconciler in _reconcilers)
        {
            diagnostics.AddRange(await reconciler.ReconcileAsync(
                context,
                cancellationToken));
        }

        return diagnostics;
    }
}

public sealed record ResourceModelGraphDefinitionApplyOptions(
    ResourceDefinitionApplyMode Mode = ResourceDefinitionApplyMode.UpdateExisting,
    bool ReconcileRuntime = true)
{
    public static ResourceModelGraphDefinitionApplyOptions Default { get; } = new();

    public static ResourceModelGraphDefinitionApplyOptions CreateMissing { get; } =
        new(ResourceDefinitionApplyMode.CreateOrUpdate);

    public ResourceModelGraphDefinitionApplyOptions WithoutRuntimeReconciliation() =>
        this with { ReconcileRuntime = false };
}

public sealed record ResourceModelGraphDefinitionApplyResult(
    ResourceGraphSnapshot BaseSnapshot,
    ResourceGraphChangeSet Changes,
    ResourceGraphCommitResult Commit,
    IReadOnlyList<ResourceDefinitionDiagnostic>? ReconciliationDiagnostics = null)
{
    public ResourceGraphVersion BaseVersion => BaseSnapshot.Version;

    public bool HasErrors =>
        Changes.HasErrors ||
        Commit.HasErrors ||
        ReconciliationDiagnosticsOrEmpty.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public bool IsCommitted => Commit.IsCommitted;

    public IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics =>
        ReconciliationDiagnosticsOrEmpty.Count > 0
            ? [.. Commit.Diagnostics, .. Changes.Diagnostics, .. ReconciliationDiagnosticsOrEmpty]
            : Commit.Diagnostics.Count > 0
                ? Commit.Diagnostics
                : Changes.Diagnostics;

    private IReadOnlyList<ResourceDefinitionDiagnostic> ReconciliationDiagnosticsOrEmpty =>
        ReconciliationDiagnostics ?? [];
}

public interface IResourceModelGraphApplyReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
        ResourceModelGraphDefinitionApplyReconciliationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceModelGraphDefinitionApplyReconciliationContext(
    ResourceGraphSnapshot BaseSnapshot,
    ResourceGraphChangeSet Changes,
    ResourceGraphCommitResult Commit,
    ResourceGraphCommitContext CommitContext);
