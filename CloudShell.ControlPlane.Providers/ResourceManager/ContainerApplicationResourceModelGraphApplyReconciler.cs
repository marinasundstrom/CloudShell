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
    IResourceOrchestratorDeploymentCleanupCoordinator? deploymentCleanup = null,
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
    private readonly IResourceOrchestratorDeploymentCleanupCoordinator? _deploymentCleanup = deploymentCleanup;
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
            var applyResult = await coordinator.ApplyDeploymentAsync(
                resource,
                deployment,
                cancellationToken,
                context.CommitContext.PrincipalId,
                cause);
            return await RunPostApplyDeploymentTearDownAsync(
                resource,
                resourceManager,
                applyResult,
                context.CommitContext.PrincipalId,
                cause,
                cancellationToken);
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

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> RunPostApplyDeploymentTearDownAsync(
        ResourceManagerResource resource,
        IResourceManagerStore resourceManager,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        string? triggeredBy,
        string cause,
        CancellationToken cancellationToken)
    {
        if (applyResult.ReplicaGroupsToTearDown.Count == 0)
        {
            return [];
        }

        var context = new ResourceOrchestrationContext(
            resource,
            _registrations.GetRegistration(resource.Id),
            resourceManager.GetGroupForResource(resource.Id),
            resourceManager,
            _registrations,
            PreferredContainerHostId: null,
            triggeredBy,
            cause,
            _resourceEvents);
        var cleanup = ResolveDeploymentCleanupCoordinator();
        if (cleanup is null)
        {
            return
            [
                ResourceDefinitionDiagnostic.Warning(
                    "application.container.deploymentCleanupUnavailable",
                    "Container application deployment applied, but post-apply cleanup requires a Resource Manager deployment cleanup coordinator.",
                    resource.Id)
            ];
        }

        var result = await cleanup.RunPostApplyCleanupAsync(
            resource,
            applyResult,
            ResourceProcedureResult.Completed(applyResult.ProcedureResult.Message),
            triggeredBy,
            describeTearDownsAsync: null,
            async (tearDown, replicaGroup, reason, token) =>
            {
                var tearDownHandler = SelectReplicaGroupTearDown(
                    context,
                    applyResult.Deployment.OrchestratorId,
                    tearDown.Service,
                    replicaGroup);
                if (tearDownHandler is null)
                {
                    throw new InvalidOperationException("no orchestrator can tear down the replica group.");
                }

                return await tearDownHandler.TearDownReplicaGroupAsync(
                    context with { Cause = reason },
                    tearDown.Service,
                    replicaGroup,
                    token);
            },
            cancellationToken: cancellationToken);

        return result.Signals
            .Where(signal => signal.Severity == ResourceSignalSeverity.Warning)
            .Select(signal => ResourceDefinitionDiagnostic.Warning(
                "application.container.deploymentCleanupWarning",
                signal.Message,
                resource.Id))
            .ToArray();
    }

    private IResourceOrchestratorReplicaGroupTearDown? SelectReplicaGroupTearDown(
        ResourceOrchestrationContext context,
        string orchestratorId,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup)
    {
        var orchestrators = _serviceProvider?
            .GetServices<IResourceOrchestrator>()
            .ToArray() ?? [];
        var explicitOrchestrator = orchestrators.FirstOrDefault(orchestrator =>
            string.Equals(
                orchestrator.Id,
                orchestratorId,
                StringComparison.OrdinalIgnoreCase) &&
            orchestrator is IResourceOrchestratorReplicaGroupTearDown tearDown &&
            tearDown.CanTearDownReplicaGroup(context, service, replicaGroup));
        if (explicitOrchestrator is IResourceOrchestratorReplicaGroupTearDown explicitTearDown)
        {
            return explicitTearDown;
        }

        return orchestrators.FirstOrDefault(orchestrator =>
            orchestrator is IResourceOrchestratorReplicaGroupTearDown tearDown &&
            tearDown.CanTearDownReplicaGroup(context, service, replicaGroup))
            as IResourceOrchestratorReplicaGroupTearDown;
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

    private IResourceOrchestratorDeploymentCleanupCoordinator? ResolveDeploymentCleanupCoordinator() =>
        _deploymentCleanup ??
        _serviceProvider?.GetService<IResourceOrchestratorDeploymentCleanupCoordinator>();

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
