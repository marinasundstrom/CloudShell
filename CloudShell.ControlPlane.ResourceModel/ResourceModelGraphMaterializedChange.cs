namespace CloudShell.ControlPlane.ResourceModel;

public interface IResourceModelGraphMaterializedChangeApplier
{
    bool CanApplyMaterializedChange(
        ResourceModelGraphMaterializedChangeContext context);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyMaterializedChangeAsync(
        ResourceModelGraphMaterializedChangeContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceModelGraphMaterializedChangeContext(
    ResourceChangeSet ChangeSet,
    ResourceModelGraphDefinitionApplyReconciliationContext Reconciliation);

public sealed class ResourceModelGraphMaterializedChangeReconciler(
    IEnumerable<IResourceModelGraphMaterializedChangeApplier>? appliers = null)
    : IResourceModelGraphApplyReconciler
{
    private readonly IReadOnlyList<IResourceModelGraphMaterializedChangeApplier> _appliers =
        (appliers ?? []).ToArray();

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
        ResourceModelGraphDefinitionApplyReconciliationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_appliers.Count == 0 || context.Changes.AcceptedResources.Count == 0)
        {
            return [];
        }

        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        foreach (var accepted in context.Changes.AcceptedResources)
        {
            var changeContext = new ResourceModelGraphMaterializedChangeContext(
                accepted.ChangeSet,
                context);
            foreach (var applier in _appliers.Where(applier =>
                applier.CanApplyMaterializedChange(changeContext)))
            {
                diagnostics.AddRange(await ApplyChangeAsync(
                    applier,
                    changeContext,
                    cancellationToken));
            }
        }

        return diagnostics;
    }

    private static async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyChangeAsync(
        IResourceModelGraphMaterializedChangeApplier applier,
        ResourceModelGraphMaterializedChangeContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await applier.ApplyMaterializedChangeAsync(
                context,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "resource.materializedChangeApplicationFailed",
                    $"Materialized resource change application failed. Reason: {exception.Message}",
                    context.ChangeSet.Resource.EffectiveResourceId)
            ];
        }
    }
}
