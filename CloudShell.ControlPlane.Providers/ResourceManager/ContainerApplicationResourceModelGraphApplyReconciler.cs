using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationResourceModelGraphApplyReconciler(
    ResourceModelGraphResourceResolver resourceResolver,
    IEnumerable<IResourceModelGraphDeploymentDescriptor>? deploymentDescriptors = null,
    IResourceRegistrationStore? registrations = null,
    IResourceEventSink? resourceEvents = null,
    IServiceProvider? serviceProvider = null) : IResourceModelGraphApplyReconciler
{
    private static readonly ResourceAttributeId ContainerImageAttributeId =
        ContainerApplicationResourceTypeProvider.Attributes.ContainerImage;
    private static readonly ResourceAttributeId ContainerReplicasAttributeId =
        ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas;

    private readonly ResourceModelGraphResourceResolver _resourceResolver =
        resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
    private readonly IReadOnlyList<IResourceModelGraphDeploymentDescriptor> _deploymentDescriptors =
        (deploymentDescriptors ?? []).ToArray();
    private readonly IResourceRegistrationStore _registrations =
        registrations ?? EmptyResourceRegistrationStore.Instance;
    private readonly IResourceEventSink? _resourceEvents = resourceEvents;
    private readonly IServiceProvider? _serviceProvider = serviceProvider;

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
        var resourceManager = ResolveResourceManager();
        if (resourceManager is null)
        {
            return
            [
                ResourceDefinitionDiagnostic.Warning(
                    "application.container.deploymentReconciliationUnavailable",
                    "Container application runtime reconciliation requires a Resource Manager deployment coordinator.",
                    resourceId)
            ];
        }

        var resource = resourceManager.GetResource(resourceId);
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
                    resourceManager.GetGroupForResource(resource.Id)?.Id,
                    _registrations,
                    resourceManager,
                    PreferredContainerHostId: null,
                    context.CommitContext.PrincipalId,
                    cause,
                    _resourceEvents)),
            cancellationToken);
        if (deployment is null)
        {
            return [];
        }

        var coordinators = ResolveDeploymentCoordinators();
        if (coordinators.Count == 0)
        {
            return
            [
                ResourceDefinitionDiagnostic.Warning(
                    "application.container.deploymentReconciliationUnavailable",
                    "Container application runtime reconciliation requires a Resource Manager deployment coordinator.",
                    resourceId)
            ];
        }

        var coordinator = coordinators.FirstOrDefault(coordinator =>
            coordinator.CanApplyDeployment(resource, deployment));
        if (coordinator is null)
        {
            return
            [
                ResourceDefinitionDiagnostic.Warning(
                    "application.container.deploymentCoordinatorMissing",
                    $"Container application resource '{resourceId}' does not have a deployment coordinator for orchestrator '{deployment.OrchestratorId}'.",
                    resourceId)
            ];
        }

        try
        {
            await coordinator.ApplyDeploymentAsync(
                resource,
                deployment,
                cancellationToken,
                context.CommitContext.PrincipalId,
                cause);
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

    private IReadOnlyList<IResourceOrchestratorDeploymentCoordinator> ResolveDeploymentCoordinators() =>
        _serviceProvider?.GetServices<IResourceOrchestratorDeploymentCoordinator>().ToArray() ?? [];

    private IResourceManagerStore? ResolveResourceManager() =>
        _serviceProvider?.GetService<IResourceManagerStore>();

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
