using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;

public sealed class ContainerApplicationResourceModelGraphApplyReconciler(
    ResourceModelGraphResourceResolver resourceResolver,
    IEnumerable<IResourceModelGraphDeploymentDescriptor>? deploymentDescriptors = null,
    IEnumerable<IResourceOrchestratorDeploymentApplier>? deploymentAppliers = null,
    IResourceManagerStore? resourceManager = null,
    IResourceRegistrationStore? registrations = null,
    IResourceEventSink? resourceEvents = null) : IResourceModelGraphApplyReconciler
{
    private static readonly ResourceAttributeId ContainerImageAttributeId =
        ContainerApplicationResourceTypeProvider.Attributes.ContainerImage;
    private static readonly ResourceAttributeId ContainerReplicasAttributeId =
        ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas;

    private readonly ResourceModelGraphResourceResolver _resourceResolver =
        resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
    private readonly IReadOnlyList<IResourceModelGraphDeploymentDescriptor> _deploymentDescriptors =
        (deploymentDescriptors ?? []).ToArray();
    private readonly IReadOnlyList<IResourceOrchestratorDeploymentApplier> _deploymentAppliers =
        (deploymentAppliers ?? []).ToArray();
    private readonly IResourceManagerStore? _resourceManager = resourceManager;
    private readonly IResourceRegistrationStore _registrations =
        registrations ?? EmptyResourceRegistrationStore.Instance;
    private readonly IResourceEventSink? _resourceEvents = resourceEvents;

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

            if (!RequiresRuntimeReconciliation(changeSet))
            {
                continue;
            }

            diagnostics.AddRange(await ApplyDeploymentAsync(
                changeSet.Resource.EffectiveResourceId,
                context,
                cancellationToken));
        }

        return diagnostics;
    }

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyDeploymentAsync(
        string resourceId,
        ResourceModelGraphDefinitionApplyReconciliationContext context,
        CancellationToken cancellationToken)
    {
        if (_resourceManager is null ||
            _deploymentAppliers.Count == 0)
        {
            return
            [
                ResourceDefinitionDiagnostic.Warning(
                    "application.container.deploymentReconciliationUnavailable",
                    "Container application runtime reconciliation requires a Resource Manager deployment applier.",
                    resourceId)
            ];
        }

        var resource = _resourceManager.GetResource(resourceId);
        if (resource is null)
        {
            return
            [
                ResourceDefinitionDiagnostic.Warning(
                    "application.container.deploymentResourceMissing",
                    $"Container application resource '{resourceId}' is not available through Resource Manager projection.",
                    resourceId)
            ];
        }

        var resolution = await _resourceResolver.ResolveAsync(
            resourceId,
            new ResourceDefinitionResolutionContext(
                context.CommitContext.EnvironmentId,
                context.CommitContext.PrincipalId),
            cancellationToken);
        if (resolution.HasErrors)
        {
            return resolution.Diagnostics;
        }

        if (resolution.Target is null)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing,
                    $"Container application resource '{resourceId}' could not be resolved for deployment reconciliation.",
                    resourceId)
            ];
        }

        var descriptor = _deploymentDescriptors.FirstOrDefault(descriptor =>
            descriptor.CanDescribeDeployment(resource, resolution.Target));
        if (descriptor is null)
        {
            return
            [
                ResourceDefinitionDiagnostic.Warning(
                    "application.container.deploymentDescriptorMissing",
                    $"Container application resource '{resourceId}' does not have a deployment descriptor.",
                    resourceId)
            ];
        }

        var cause = CreateCause(context);
        var deployment = await descriptor.DescribeDeploymentAsync(
            new ResourceModelGraphDeploymentDescriptionContext(
                resource,
                resolution.Target,
                new ResourceProcedureContext(
                    resource,
                    _registrations.GetRegistration(resource.Id),
                    _resourceManager.GetGroupForResource(resource.Id)?.Id,
                    _registrations,
                    _resourceManager,
                    PreferredContainerHostId: null,
                    context.CommitContext.PrincipalId,
                    cause,
                    _resourceEvents)),
            cancellationToken);
        if (deployment is null)
        {
            return [];
        }

        var orchestrationContext = new ResourceOrchestrationContext(
            resource,
            _registrations.GetRegistration(resource.Id),
            _resourceManager.GetGroupForResource(resource.Id),
            _resourceManager,
            _registrations,
            PreferredContainerHostId: null,
            context.CommitContext.PrincipalId,
            cause,
            _resourceEvents);
        var applier = _deploymentAppliers.FirstOrDefault(applier =>
            applier.CanApplyDeployment(orchestrationContext, deployment));
        if (applier is null)
        {
            return
            [
                ResourceDefinitionDiagnostic.Warning(
                    "application.container.deploymentApplierMissing",
                    $"Container application resource '{resourceId}' does not have a deployment applier for orchestrator '{deployment.OrchestratorId}'.",
                    resourceId)
            ];
        }

        try
        {
            await applier.ApplyDeploymentAsync(
                orchestrationContext,
                deployment,
                cancellationToken);
            return [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.container.deploymentReconciliationFailed",
                    $"Container application deployment reconciliation failed. Reason: {exception.Message}",
                    resourceId)
            ];
        }
    }

    private static bool RequiresRuntimeReconciliation(
        ResourceChangeSet changeSet)
    {
        return HasChangedAttribute(changeSet, ContainerImageAttributeId) ||
            HasChangedAttribute(changeSet, ContainerReplicasAttributeId);
    }

    private static bool HasChangedAttribute(
        ResourceChangeSet changeSet,
        ResourceAttributeId attributeId) =>
        changeSet.AttributeChanges.Any(change => change.AttributeId == attributeId);

    private static string CreateCause(
        ResourceModelGraphDefinitionApplyReconciliationContext context) =>
        string.IsNullOrWhiteSpace(context.CommitContext.PrincipalId)
            ? "ResourceDefinition apply requested runtime reconciliation."
            : $"ResourceDefinition apply requested runtime reconciliation for principal '{context.CommitContext.PrincipalId.Trim()}'.";

    private sealed class EmptyResourceRegistrationStore : IResourceRegistrationStore
    {
        public static EmptyResourceRegistrationStore Instance { get; } = new();

        public IReadOnlyList<ResourceRegistration> GetRegistrations() => [];

        public ResourceRegistration? GetRegistration(string resourceId) => null;

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
