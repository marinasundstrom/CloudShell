using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;

public sealed class ContainerApplicationResourceModelGraphApplyReconciler(
    ResourceModelGraphResourceResolver resourceResolver) : IResourceModelGraphApplyReconciler
{
    private static readonly ResourceAttributeId ContainerImageAttributeId =
        ContainerApplicationResourceTypeProvider.Attributes.ContainerImage;
    private static readonly ResourceAttributeId ContainerReplicasAttributeId =
        ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas;

    private readonly ResourceModelGraphResourceResolver _resourceResolver =
        resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
        ResourceModelGraphDefinitionApplyReconciliationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        foreach (var accepted in context.Changes.AcceptedResources)
        {
            var changeSet = accepted.ChangeSet;
            if (changeSet.IsNewResource ||
                changeSet.Resource.Type.TypeId != ContainerApplicationResourceTypeProvider.ResourceTypeId)
            {
                continue;
            }

            var operationId = GetRuntimeReconciliationOperation(changeSet);
            if (operationId is null)
            {
                continue;
            }

            diagnostics.AddRange(await ExecuteOperationAsync(
                changeSet.Resource.EffectiveResourceId,
                operationId.Value,
                context,
                cancellationToken));
        }

        return diagnostics;
    }

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteOperationAsync(
        string resourceId,
        ResourceOperationId operationId,
        ResourceModelGraphDefinitionApplyReconciliationContext context,
        CancellationToken cancellationToken)
    {
        var resolution = await _resourceResolver.ResolveOperationAsync(
            resourceId,
            operationId,
            new ResourceDefinitionResolutionContext(
                context.CommitContext.EnvironmentId,
                context.CommitContext.PrincipalId),
            cancellationToken);
        if (resolution.Diagnostics.Count > 0)
        {
            return resolution.Diagnostics;
        }

        if (resolution.Operation is not IResourceOperationExecutorProjection executableOperation)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceOperationProjectionMissing,
                    $"Operation '{operationId}' does not support runtime reconciliation.",
                    resourceId)
            ];
        }

        if (!await executableOperation.CanExecuteAsync(cancellationToken))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceOperationProjectionMissing,
                    $"Operation '{operationId}' cannot execute for runtime reconciliation.",
                    resourceId)
            ];
        }

        var execution = await executableOperation.ExecuteAsync(cancellationToken);
        return execution.Diagnostics;
    }

    private static ResourceOperationId? GetRuntimeReconciliationOperation(
        ResourceChangeSet changeSet)
    {
        if (HasChangedAttribute(changeSet, ContainerImageAttributeId))
        {
            return ContainerApplicationResourceTypeProvider.Operations.UpdateImage;
        }

        if (HasChangedAttribute(changeSet, ContainerReplicasAttributeId))
        {
            return ContainerApplicationResourceTypeProvider.Operations.UpdateReplicas;
        }

        return null;
    }

    private static bool HasChangedAttribute(
        ResourceChangeSet changeSet,
        ResourceAttributeId attributeId) =>
        changeSet.AttributeChanges.Any(change => change.AttributeId == attributeId);
}
