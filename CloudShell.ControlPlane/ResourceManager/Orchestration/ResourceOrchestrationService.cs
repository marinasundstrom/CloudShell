using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Deployment;
using CloudShell.ControlPlane.ResourceManager.Observability;
using CloudShell.ControlPlane.ResourceManager.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

public sealed class ResourceOrchestrationService(
    IEnumerable<IResourceOrchestrator> orchestrators,
    IEnumerable<IResourceOrchestrationDescriptorProvider> descriptorProviders,
    IResourceManagerStore resourceManager,
    IResourceRegistrationStore registrations,
    ResourceDeclarationStore declarations,
    ResourceOrchestratorSelectionStore selectionStore,
    IEnumerable<IContainerHostProvider>? containerHostProviders = null,
    IContainerHostResolver? containerHostResolver = null,
    IEnumerable<IResourceActionAvailabilityProvider>? actionAvailabilityProviders = null,
    IResourceEventSink? resourceEvents = null,
    IResourceOrchestratorDeploymentStore? deploymentStore = null,
    ILoggerFactory? loggerFactory = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyList<IResourceOrchestrator> orchestrators = orchestrators.ToArray();
    private readonly IReadOnlyList<IResourceOrchestrationDescriptorProvider> descriptorProviders =
        descriptorProviders.ToArray();
    private readonly IReadOnlyList<IResourceActionAvailabilityProvider> actionAvailabilityProviders =
        actionAvailabilityProviders?.ToArray() ?? [];
    private readonly ConcurrentDictionary<string, byte> activeReplicaSlotReconciliations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IContainerHostResolver containerHostResolver =
        containerHostResolver ??
        new ContainerHostResolver(
            resourceManager,
            registrations,
            descriptorProviders,
            containerHostProviders ?? []);
    private readonly ILogger lifecycleLogger =
        loggerFactory?.CreateLogger(CloudShellLogCategories.ResourceLifecycle) ??
        NullLogger.Instance;

    public string? PreferredContainerHostId => selectionStore.Get().PreferredContainerHostId;

    public DependencyStartFailureBehavior DependencyStartFailureBehavior =>
        selectionStore.GetDependencyStartFailureSettings().Behavior;

    public async Task<ResourceProcedureResult> DeleteAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        var context = CreateContext(resource);
        var orchestrator = SelectDeleteOrchestrator(context);
        return await orchestrator.DeleteAsync(context, cancellationToken);
    }

    public async Task<ResourceProcedureResult> TearDownServiceAsync(
        Resource resource,
        ResourceOrchestratorService service,
        CancellationToken cancellationToken = default,
        string? triggeredBy = null,
        string? cause = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(service);

        if (!string.Equals(resource.Id, service.ResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Service '{service.Name}' belongs to resource '{service.ResourceId}', not '{resource.Id}'."));
        }

        var context = CreateContext(resource, triggeredBy, cause);
        var tearDown = SelectServiceTearDown(context, service);
        return await tearDown.TearDownServiceAsync(context, service, cancellationToken);
    }

    public async Task<ResourceProcedureResult> TearDownReplicaGroupAsync(
        Resource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        CancellationToken cancellationToken = default,
        string? triggeredBy = null,
        string? cause = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(replicaGroup);

        if (!string.Equals(resource.Id, service.ResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Service '{service.Name}' belongs to resource '{service.ResourceId}', not '{resource.Id}'."));
        }

        var context = CreateContext(resource, triggeredBy, cause);
        var tearDown = SelectReplicaGroupTearDown(context, service, replicaGroup);
        return await tearDown.TearDownReplicaGroupAsync(context, service, replicaGroup, cancellationToken);
    }

    public async Task<ResourceProcedureResult> ReconcileReplicaSlotAsync(
        Resource resource,
        int slotOrdinal,
        string? detail = null,
        CancellationToken cancellationToken = default,
        string? triggeredBy = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (slotOrdinal < 1)
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest("Replica slot ordinal must be greater than or equal to 1."));
        }

        var context = CreateContext(resource, triggeredBy, detail);
        var provider = ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, ResourceAction.Start)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));
        var resourceContext = ResourceOrchestratorProviderResolver.CreateProcedureContext(context);
        var activeRuntime = ResolveActiveReplicaGroup(resource);
        var service = activeRuntime?.Service ??
            await provider.CreateOrchestratorServiceAsync(resourceContext, cancellationToken);
        var replicaGroup = activeRuntime?.ReplicaGroup ??
            ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);

        return await ReconcileReplicaSlotAsync(
            resource,
            service,
            replicaGroup,
            slotOrdinal,
            detail,
            cancellationToken,
            triggeredBy);
    }

    public async Task<ResourceProcedureResult> ReconcileReplicaSlotAsync(
        Resource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        int slotOrdinal,
        string? detail = null,
        CancellationToken cancellationToken = default,
        string? triggeredBy = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(replicaGroup);
        if (slotOrdinal < 1)
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest("Replica slot ordinal must be greater than or equal to 1."));
        }

        if (!string.Equals(resource.Id, service.ResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Service '{service.Name}' belongs to resource '{service.ResourceId}', not '{resource.Id}'."));
        }

        if (!string.Equals(service.Name, replicaGroup.ServiceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Replica group '{replicaGroup.Id}' belongs to service '{replicaGroup.ServiceId}', not '{service.Name}'."));
        }

        var context = CreateContext(resource, triggeredBy, detail);
        var provider = ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, ResourceAction.Start)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));
        var resourceContext = ResourceOrchestratorProviderResolver.CreateProcedureContext(context);
        var slot = replicaGroup.Slots.FirstOrDefault(candidate => candidate.Ordinal == slotOrdinal)
            ?? throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Replica group '{replicaGroup.Id}' does not define slot '{slotOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}'."));
        var key = $"{resource.Id}:{replicaGroup.Id}:{slot.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        if (!activeReplicaSlotReconciliations.TryAdd(key, 0))
        {
            return ResourceProcedureResult.Completed(
                $"Replica slot '{slot.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}' reconciliation is already running.");
        }

        try
        {
            return await ReconcileReplicaSlotCoreAsync(
                provider,
                resourceContext,
                service,
                replicaGroup,
                slot,
                detail,
                triggeredBy,
                cancellationToken);
        }
        finally
        {
            activeReplicaSlotReconciliations.TryRemove(key, out _);
        }
    }

    private ActiveReplicaGroup? ResolveActiveReplicaGroup(Resource resource)
    {
        if (deploymentStore is null)
        {
            return null;
        }

        var record = deploymentStore
            .List(new ResourceOrchestratorDeploymentQuery(
                SourceResourceId: resource.Id,
                MaxRecords: 1_000))
            .Where(record =>
                record.Status == ResourceOrchestratorDeploymentStatus.Active &&
                record.ReplicaGroup is not null)
            .OrderByDescending(record => record.CompletedAt ?? record.StartedAt)
            .FirstOrDefault();
        if (record is null)
        {
            return null;
        }

        var replicaGroup = record.ReplicaGroup!;
        var service = record.Deployment.Spec.Service with
        {
            RuntimeRevisionId = replicaGroup.RuntimeRevisionId
        };
        return new ActiveReplicaGroup(service, replicaGroup);
    }

    private sealed record ActiveReplicaGroup(
        ResourceOrchestratorService Service,
        ResourceOrchestratorReplicaGroup ReplicaGroup);

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        Resource resource,
        ResourceAction action,
        bool startDependencies,
        ICloudShellAuthorizationService authorization,
        CancellationToken cancellationToken = default,
        string? triggeredBy = null,
        string? cause = null,
        Action<ResourceChangeNotification>? notifyResourceChange = null,
        DependencyStartFailureBehavior dependencyStartFailureBehavior = DependencyStartFailureBehavior.FailAction)
    {
        var dependencyWarnings = new List<string>();
        if (startDependencies && ShouldStartDependencies(action))
        {
            try
            {
                await StartResourceDependenciesAsync(
                    resource,
                    resource,
                    action,
                    authorization,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    [],
                    triggeredBy,
                    dependencyStartFailureBehavior,
                    dependencyWarnings,
                    notifyResourceChange,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AppendResourceActionEvent(
                    resource,
                    ResourceEventTypes.Actions.ForFailedAction(action.Id),
                    $"{GetActionFailedMessage(action)} Reason: A dependency could not start. {exception.Message}",
                    triggeredBy,
                    ResourceSignalSeverity.Error);
                AppendLifecycleEvent(
                    resource,
                    GetLifecycleEventTypes(action)?.Failed,
                    $"{GetLifecycleFailedMessage(action)} Reason: A dependency could not start. {exception.Message}",
                    cause,
                    triggeredBy,
                    ResourceSignalSeverity.Error);
                NotifyResourceChange(
                    notifyResourceChange,
                    ResourceChangeKind.ResourceActionFailed,
                    resource,
                    action);

                throw;
            }
        }

        var result = await ExecuteActionCoreAsync(
            resource,
            action,
            cancellationToken,
            triggeredBy,
            cause,
            notifyResourceChange);
        return AddDependencyWarnings(result, dependencyWarnings);
    }

    private async Task<ResourceProcedureResult> ReconcileReplicaSlotCoreAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorReplicaSlot slot,
        string? detail,
        string? triggeredBy,
        CancellationToken cancellationToken)
    {
        AppendReplicaManagementEvent(
            resourceContext.Resource,
            ResourceEventTypes.Events.ReplicaManagement.SlotUnhealthy,
            $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} is unhealthy. {detail ?? "No detail was provided."}",
            triggeredBy,
            ResourceSignalSeverity.Warning);

        var occupant = slot.Occupant;
        if (occupant is null)
        {
            AppendReplicaManagementEvent(
                resourceContext.Resource,
                ResourceEventTypes.Events.ReplicaManagement.SlotVacant,
                $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} is vacant.",
                triggeredBy,
                ResourceSignalSeverity.Warning);
        }
        else
        {
            AppendReplicaManagementEvent(
                resourceContext.Resource,
                ResourceEventTypes.Events.ReplicaManagement.OccupantCrashed,
                $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} occupant '{occupant.Name}' is not healthy.",
                triggeredBy,
                ResourceSignalSeverity.Warning);
        }

        var policy = replicaGroup.EffectiveManagementPolicy;
        return policy.RestartMode switch
        {
            ResourceOrchestratorReplicaRestartMode.LeaveVacant =>
                LeaveReplicaSlotVacant(resourceContext.Resource, replicaGroup, slot, triggeredBy),
            ResourceOrchestratorReplicaRestartMode.RestartOccupant =>
                await RestartReplicaSlotOccupantAsync(
                    provider,
                    resourceContext,
                    service,
                    replicaGroup,
                    slot,
                    triggeredBy,
                    cancellationToken),
            _ =>
                await ReplaceReplicaSlotOccupantAsync(
                    provider,
                    resourceContext,
                    service,
                    replicaGroup,
                    slot,
                    triggeredBy,
                    cancellationToken)
        };
    }

    private async Task<ResourceProcedureResult> ExecuteActionCoreAsync(
        Resource resource,
        ResourceAction action,
        CancellationToken cancellationToken,
        string? triggeredBy = null,
        string? cause = null,
        Action<ResourceChangeNotification>? notifyResourceChange = null)
    {
        var context = CreateContext(resource, triggeredBy, cause);
        var unavailableReason = await GetActionUnavailableReasonAsync(context, action, cancellationToken);
        if (unavailableReason is not null)
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceActionUnavailable(unavailableReason));
        }

        var orchestrator = SelectActionOrchestrator(context, action);
        AppendResourceActionEvent(
            resource,
            GetActionEventType(action),
            $"{GetActionRequestedMessage(action)}{FormatCause(cause)}",
            triggeredBy);
        AppendLifecycleEvent(
            resource,
            GetLifecycleEventTypes(action)?.Starting,
            GetLifecycleStartingMessage(action),
            cause,
            triggeredBy);
        LogLifecycle(
            action,
            resource,
            "Requested lifecycle {ActionKind} for resource {ResourceName}.",
            action.Kind,
            ResourceDisplayLabels.GetQualifiedLabel(resource));
        NotifyResourceChange(
            notifyResourceChange,
            ResourceChangeKind.ResourceActionStarted,
            resource,
            action);

        try
        {
            var result = await orchestrator.ExecuteActionAsync(context, action, cancellationToken);
            AppendLifecycleEvent(
                resource,
                GetLifecycleEventTypes(action)?.Completed,
                $"{GetLifecycleCompletedMessage(action)} Result: {result.Message}",
                cause,
                triggeredBy);
            LogLifecycle(
                action,
                resource,
                "Completed lifecycle {ActionKind} for resource {ResourceName}: {Message}",
                action.Kind,
                ResourceDisplayLabels.GetQualifiedLabel(resource),
                result.Message);
            NotifyResourceChange(
                notifyResourceChange,
                ResourceChangeKind.ResourceActionExecuted,
                resource,
                action);

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendResourceActionEvent(
                resource,
                ResourceEventTypes.Actions.ForFailedAction(action.Id),
                $"{GetActionFailedMessage(action)}{FormatCause(cause)} Reason: {exception.Message}",
                triggeredBy,
                ResourceSignalSeverity.Error);
            AppendLifecycleEvent(
                resource,
                GetLifecycleEventTypes(action)?.Failed,
                $"{GetLifecycleFailedMessage(action)} Reason: {exception.Message}",
                cause,
                triggeredBy,
                ResourceSignalSeverity.Error);
            LogLifecycle(
                action,
                resource,
                "Failed lifecycle {ActionKind} for resource {ResourceName}: {Message}",
                action.Kind,
                ResourceDisplayLabels.GetQualifiedLabel(resource),
                exception.Message);
            NotifyResourceChange(
                notifyResourceChange,
                ResourceChangeKind.ResourceActionFailed,
                resource,
                action);

            throw;
        }
    }

    private void LogLifecycle(ResourceAction action, Resource resource, string message, params object?[] args)
    {
        if (GetLifecycleEventTypes(action) is null)
        {
            return;
        }

        using var scope = ResourceLogScope.Begin(lifecycleLogger, resource);
        lifecycleLogger.LogInformation(message, args);
    }

    public async Task<string?> GetActionUnavailableReasonAsync(
        Resource resource,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var context = CreateContext(resource);
        return await GetActionUnavailableReasonAsync(context, action, cancellationToken);
    }

    private async Task StartResourceDependenciesAsync(
        Resource resource,
        Resource rootResource,
        ResourceAction rootAction,
        ICloudShellAuthorizationService authorization,
        HashSet<string> visiting,
        HashSet<string> completed,
        List<Resource> path,
        string? triggeredBy,
        DependencyStartFailureBehavior dependencyStartFailureBehavior,
        List<string> dependencyWarnings,
        Action<ResourceChangeNotification>? notifyResourceChange,
        CancellationToken cancellationToken)
    {
        if (!visiting.Add(resource.Id))
        {
            throw CreateDependencyAutoStartException(
                rootResource,
                resource,
                path.Append(resource),
                $"dependency cycle detected at '{resource.Id}'");
        }

        path.Add(resource);
        try
        {
            foreach (var dependencyId in resource.DependsOn.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (completed.Contains(dependencyId))
                    {
                        continue;
                    }

                    var dependency = resourceManager.GetResource(dependencyId);
                    if (dependency is null)
                    {
                        throw CreateDependencyAutoStartException(
                            rootResource,
                            dependencyId,
                            path.Select(FormatResource).Append(dependencyId),
                            "dependency resource could not be found");
                    }

                    var dependencyPath = path.Append(dependency).ToArray();
                    if (!ShouldAutoStartAsDependency(dependency))
                    {
                        throw CreateDependencyAutoStartException(
                            rootResource,
                            dependency,
                            dependencyPath,
                            "auto-start is disabled");
                    }

                    await StartResourceDependenciesAsync(
                        dependency,
                        rootResource,
                        rootAction,
                        authorization,
                        visiting,
                        completed,
                        path,
                        triggeredBy,
                        dependencyStartFailureBehavior,
                        dependencyWarnings,
                        notifyResourceChange,
                        cancellationToken);

                    if (dependency.State == ResourceState.Running)
                    {
                        completed.Add(dependency.Id);
                        continue;
                    }

                    var runAction = dependency.ResourceActions.FirstOrDefault(action =>
                        action.Kind == ResourceActionKind.Start);
                    if (runAction is null)
                    {
                        throw CreateDependencyAutoStartException(
                            rootResource,
                            dependency,
                            dependencyPath,
                            "dependency does not expose a Start action");
                    }

                    var group = resourceManager.GetGroupForResource(dependency.Id);
                    if (!authorization.CanAccessResource(
                            dependency.Id,
                            group?.Id,
                            CloudShellPermissions.Resources.Manage))
                    {
                        throw CreateDependencyAutoStartException(
                            rootResource,
                            dependency,
                            dependencyPath,
                            $"the '{CloudShellPermissions.Resources.Manage}' permission is required");
                    }

                    try
                    {
                        await ExecuteActionCoreAsync(
                            dependency,
                            runAction,
                            cancellationToken,
                            triggeredBy ?? rootResource.Id,
                            $"Dependency auto-start for {FormatResource(rootResource)}.",
                            notifyResourceChange);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        throw CreateDependencyAutoStartException(
                            rootResource,
                            dependency,
                            dependencyPath,
                            exception.Message,
                            exception);
                    }

                    completed.Add(dependency.Id);
                }
                catch (ControlPlaneException exception)
                    when (dependencyStartFailureBehavior == DependencyStartFailureBehavior.WarnAndContinue &&
                        exception.Error.Code == ControlPlaneErrorCodes.DependencyAutoStartFailed)
                {
                    AddDependencyStartWarning(rootResource, rootAction, triggeredBy, dependencyWarnings, exception);
                }
            }
        }
        catch (ControlPlaneException exception)
            when (!string.Equals(resource.Id, rootResource.Id, StringComparison.OrdinalIgnoreCase) &&
                exception.Error.Code == ControlPlaneErrorCodes.DependencyAutoStartFailed)
        {
            AppendDependencyStartFailureEvent(
                resource,
                rootAction,
                triggeredBy,
                exception);
            NotifyResourceChange(
                notifyResourceChange,
                ResourceChangeKind.ResourceActionFailed,
                resource,
                rootAction);

            throw;
        }
        finally
        {
            path.RemoveAt(path.Count - 1);
            visiting.Remove(resource.Id);
        }
    }

    private void AppendDependencyStartFailureEvent(
        Resource resource,
        ResourceAction action,
        string? triggeredBy,
        ControlPlaneException exception)
    {
        var reason = $"Reason: A dependency could not start. {exception.Message}";
        AppendResourceActionEvent(
            resource,
            ResourceEventTypes.Actions.ForFailedAction(action.Id),
            $"{GetActionFailedMessage(action)} {reason}",
            triggeredBy,
            ResourceSignalSeverity.Error);
        AppendLifecycleEvent(
            resource,
            GetLifecycleEventTypes(action)?.Failed,
            $"{GetLifecycleFailedMessage(action)} {reason}",
            null,
            triggeredBy,
            ResourceSignalSeverity.Error);
    }

    private void AddDependencyStartWarning(
        Resource rootResource,
        ResourceAction rootAction,
        string? triggeredBy,
        List<string> dependencyWarnings,
        ControlPlaneException exception)
    {
        var warning = $"Dependency auto-start warning: {exception.Message}";
        dependencyWarnings.Add(warning);
        AppendResourceActionEvent(
            rootResource,
            ResourceEventTypes.Actions.ForAction(rootAction.Id),
            warning,
            triggeredBy,
            ResourceSignalSeverity.Warning);
    }

    private static ResourceProcedureResult AddDependencyWarnings(
        ResourceProcedureResult result,
        IReadOnlyList<string> dependencyWarnings)
    {
        if (dependencyWarnings.Count == 0)
        {
            return result;
        }

        return result with
        {
            Signals = result.Signals
                .Concat(dependencyWarnings.Select(ResourceProcedureSignal.Warning))
                .ToArray()
        };
    }

    private void AppendResourceActionEvent(
        Resource resource,
        string eventType,
        string message,
        string? triggeredBy,
        ResourceSignalSeverity severity = ResourceSignalSeverity.Info) =>
        resourceEvents?.Append(new ResourceEvent(
            resource.Id,
            eventType,
            message,
            DateTimeOffset.UtcNow,
            triggeredBy,
            severity));

    private static void NotifyResourceChange(
        Action<ResourceChangeNotification>? notifyResourceChange,
        ResourceChangeKind kind,
        Resource resource,
        ResourceAction action) =>
        notifyResourceChange?.Invoke(new ResourceChangeNotification(
            kind,
            resource.Id,
            action.Id,
            [resource.Id]));

    private void AppendLifecycleEvent(
        Resource resource,
        string? eventType,
        string? message,
        string? cause,
        string? triggeredBy,
        ResourceSignalSeverity severity = ResourceSignalSeverity.Info)
    {
        if (string.IsNullOrWhiteSpace(eventType) ||
            string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        AppendResourceActionEvent(
            resource,
            eventType,
            $"{message}{FormatCause(cause)}",
            triggeredBy,
            severity);
    }

    private static string GetActionEventType(ResourceAction action) =>
        ResourceEventTypes.Actions.ForAction(action.Id);

    private ResourceProcedureResult LeaveReplicaSlotVacant(
        Resource resource,
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorReplicaSlot slot,
        string? triggeredBy)
    {
        var message =
            $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} was left vacant by policy.";
        AppendReplicaManagementEvent(
            resource,
            ResourceEventTypes.Events.ReplicaManagement.SlotLeftVacant,
            message,
            triggeredBy,
            ResourceSignalSeverity.Warning);
        return ResourceProcedureResult.Completed(message);
    }

    private async Task<ResourceProcedureResult> RestartReplicaSlotOccupantAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorReplicaSlot slot,
        string? triggeredBy,
        CancellationToken cancellationToken)
    {
        var occupant = RequireReplicaSlotOccupant(replicaGroup, slot);
        AppendReplicaManagementEvent(
            resourceContext.Resource,
            ResourceEventTypes.Events.ReplicaManagement.RestartScheduled,
            $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} occupant '{occupant.Name}' restart was scheduled.",
            triggeredBy,
            ResourceSignalSeverity.Warning);
        AppendReplicaManagementEvent(
            resourceContext.Resource,
            ResourceEventTypes.Events.ReplicaManagement.RestartAttempted,
            $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} occupant '{occupant.Name}' restart started.",
            triggeredBy,
            ResourceSignalSeverity.Warning);

        try
        {
            await provider.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(resourceContext, service, occupant, replicaGroup),
                ResourceAction.Start,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendReplicaManagementEvent(
                resourceContext.Resource,
                ResourceEventTypes.Events.ReplicaManagement.ReconciliationFailed,
                $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} restart failed: {exception.Message}",
                triggeredBy,
                ResourceSignalSeverity.Error);
            throw;
        }

        return ResourceProcedureResult.Completed(
            $"Restarted replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} occupant '{occupant.Name}'.");
    }

    private async Task<ResourceProcedureResult> ReplaceReplicaSlotOccupantAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorReplicaSlot slot,
        string? triggeredBy,
        CancellationToken cancellationToken)
    {
        var occupant = RequireReplicaSlotOccupant(replicaGroup, slot);
        AppendReplicaManagementEvent(
            resourceContext.Resource,
            ResourceEventTypes.Events.ReplicaManagement.ReplacementScheduled,
            $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} occupant '{occupant.Name}' replacement was scheduled.",
            triggeredBy,
            ResourceSignalSeverity.Warning);

        try
        {
            await provider.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(resourceContext, service, occupant, replicaGroup),
                ResourceAction.Stop,
                cancellationToken);
            AppendReplicaManagementEvent(
                resourceContext.Resource,
                ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterializing,
                $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} replacement occupant '{occupant.Name}' is materializing.",
                triggeredBy,
                ResourceSignalSeverity.Warning);
            await provider.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(resourceContext, service, occupant, replicaGroup),
                ResourceAction.Start,
                cancellationToken);
            AppendReplicaManagementEvent(
                resourceContext.Resource,
                ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterialized,
                $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} replacement occupant '{occupant.Name}' materialized.",
                triggeredBy,
                ResourceSignalSeverity.Success);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendReplicaManagementEvent(
                resourceContext.Resource,
                ResourceEventTypes.Events.ReplicaManagement.ReconciliationFailed,
                $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} replacement failed: {exception.Message}",
                triggeredBy,
                ResourceSignalSeverity.Error);
            throw;
        }

        return ResourceProcedureResult.Completed(
            $"Replaced replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} occupant '{occupant.Name}'.");
    }

    private static ResourceOrchestratorServiceInstance RequireReplicaSlotOccupant(
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorReplicaSlot slot) =>
        slot.Occupant ??
        throw new ControlPlaneException(
            ControlPlaneError.InvalidRequest(
                $"Replica group '{replicaGroup.Id}' slot {FormatReplicaSlot(slot)} does not have an occupant to reconcile."));

    private void AppendReplicaManagementEvent(
        Resource resource,
        string eventType,
        string message,
        string? triggeredBy,
        ResourceSignalSeverity severity) =>
        resourceEvents?.Append(new ResourceEvent(
            resource.Id,
            eventType,
            message,
            DateTimeOffset.UtcNow,
            triggeredBy,
            severity));

    private static string FormatReplicaSlot(ResourceOrchestratorReplicaSlot slot) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{slot.Ordinal}/{slot.SlotCount}");

    private static ResourceLifecycleEventTypes? GetLifecycleEventTypes(ResourceAction action) =>
        action.Kind switch
        {
            ResourceActionKind.Start => new(
                ResourceEventTypes.Events.Lifecycle.Starting,
                ResourceEventTypes.Events.Lifecycle.Started,
                ResourceEventTypes.Events.Lifecycle.StartFailed),
            ResourceActionKind.Stop => new(
                ResourceEventTypes.Events.Lifecycle.Stopping,
                ResourceEventTypes.Events.Lifecycle.Stopped,
                ResourceEventTypes.Events.Lifecycle.StopFailed),
            ResourceActionKind.Pause => new(
                ResourceEventTypes.Events.Lifecycle.Pausing,
                ResourceEventTypes.Events.Lifecycle.Paused,
                ResourceEventTypes.Events.Lifecycle.PauseFailed),
            ResourceActionKind.Restart => new(
                ResourceEventTypes.Events.Lifecycle.Restarting,
                ResourceEventTypes.Events.Lifecycle.Restarted,
                ResourceEventTypes.Events.Lifecycle.RestartFailed),
            _ => null
        };

    private static string? GetLifecycleStartingMessage(ResourceAction action) =>
        action.Kind switch
        {
            ResourceActionKind.Start => "Resource is starting.",
            ResourceActionKind.Stop => "Resource is stopping.",
            ResourceActionKind.Pause => "Resource is pausing.",
            ResourceActionKind.Restart => "Resource is restarting.",
            _ => null
        };

    private static string? GetLifecycleCompletedMessage(ResourceAction action) =>
        action.Kind switch
        {
            ResourceActionKind.Start => "Resource started.",
            ResourceActionKind.Stop => "Resource stopped.",
            ResourceActionKind.Pause => "Resource paused.",
            ResourceActionKind.Restart => "Resource restarted.",
            _ => null
        };

    private static string? GetLifecycleFailedMessage(ResourceAction action) =>
        action.Kind switch
        {
            ResourceActionKind.Start => "Resource failed to start.",
            ResourceActionKind.Stop => "Resource failed to stop.",
            ResourceActionKind.Pause => "Resource failed to pause.",
            ResourceActionKind.Restart => "Resource failed to restart.",
            _ => null
        };

    private static string GetActionRequestedMessage(ResourceAction action) =>
        action.Kind switch
        {
            ResourceActionKind.Start => "Requested lifecycle start action.",
            ResourceActionKind.Stop => "Requested lifecycle stop action.",
            ResourceActionKind.Pause => "Requested lifecycle pause action.",
            ResourceActionKind.Restart => "Requested lifecycle restart action.",
            _ => $"Requested resource action '{action.Id}'."
        };

    private static string GetActionFailedMessage(ResourceAction action) =>
        action.Kind switch
        {
            ResourceActionKind.Start => "Failed lifecycle start action.",
            ResourceActionKind.Stop => "Failed lifecycle stop action.",
            ResourceActionKind.Pause => "Failed lifecycle pause action.",
            ResourceActionKind.Restart => "Failed lifecycle restart action.",
            _ => $"Failed action '{action.Id}'."
        };

    private static string FormatCause(string? cause) =>
        string.IsNullOrWhiteSpace(cause)
            ? string.Empty
            : $" Cause: {cause.Trim().TrimEnd('.')}.";

    private sealed record ResourceLifecycleEventTypes(
        string Starting,
        string Completed,
        string Failed);

    private static ControlPlaneException CreateDependencyAutoStartException(
        Resource rootResource,
        Resource dependency,
        IEnumerable<Resource> path,
        string reason,
        Exception? innerException = null) =>
        CreateDependencyAutoStartException(
            rootResource,
            FormatResource(dependency),
            path.Select(FormatResource),
            reason,
            innerException);

    private static ControlPlaneException CreateDependencyAutoStartException(
        Resource rootResource,
        string dependencyLabel,
        IEnumerable<string> path,
        string reason,
        Exception? innerException = null)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "no failure reason was provided"
            : reason.Trim().TrimEnd('.');
        var message =
            $"Could not auto-start dependency '{dependencyLabel}' for resource '{FormatResource(rootResource)}'. " +
            $"Dependency path: {string.Join(" -> ", path)}. " +
            $"Reason: {normalizedReason}.";
        var error = ControlPlaneError.DependencyAutoStartFailed(message);
        return innerException is null
            ? new ControlPlaneException(error)
            : new ControlPlaneException(error, innerException);
    }

    private static string FormatResource(Resource resource)
        => ResourceDisplayLabels.GetQualifiedLabel(resource);

    private bool ShouldAutoStartAsDependency(Resource resource)
    {
        var declaration = declarations.GetDeclaration(resource.Id);
        if (declaration?.DependencyAutoStartOverride is not null)
        {
            return declaration.DependencyAutoStartOverride.Value;
        }

        var providerId = declaration?.ProviderId ??
            registrations.GetRegistration(resource.Id)?.ProviderId;
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            var provider = resourceManager.Providers
                .OfType<IResourceAutoStartPolicyProvider>()
                .FirstOrDefault(provider =>
                    string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase) &&
                    declaration is not null &&
                    provider.CanEvaluateAutoStartPolicy(declaration));
            var providerDefault = declaration is null
                ? null
                : provider?.GetAutoStartPolicy(declaration).StartAsDependency;
            if (providerDefault is not null)
            {
                return providerDefault.Value;
            }
        }

        return declarations.DefaultDependencyAutoStart;
    }

    private ResourceOrchestrationContext CreateContext(
        Resource resource,
        string? triggeredBy = null,
        string? cause = null)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        return new ResourceOrchestrationContext(
            resource,
            registration,
            resourceManager.GetGroupForResource(resource.Id),
            resourceManager,
            registrations,
            selectionStore.Get().PreferredContainerHostId,
            triggeredBy,
            cause,
            resourceEvents);
    }

    private IResourceOrchestrator SelectActionOrchestrator(
        ResourceOrchestrationContext context,
        ResourceAction action) =>
        SelectPreferredOrchestrator(orchestrator => orchestrator.CanExecute(context, action))
        ?? throw new ControlPlaneException(
            ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));

    private IResourceOrchestrator SelectDeleteOrchestrator(
        ResourceOrchestrationContext context) =>
        SelectPreferredOrchestrator(orchestrator => orchestrator.CanDelete(context))
        ?? throw new ControlPlaneException(
            ControlPlaneError.ResourceDeleteUnsupported(context.Resource.Name));

    private IResourceOrchestratorServiceTearDown SelectServiceTearDown(
        ResourceOrchestrationContext context,
        ResourceOrchestratorService service) =>
        SelectPreferredServiceTearDown((_, tearDown) => tearDown.CanTearDownService(context, service))
        ?? throw new ControlPlaneException(
            ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));

    private IResourceOrchestratorReplicaGroupTearDown SelectReplicaGroupTearDown(
        ResourceOrchestrationContext context,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup) =>
        SelectPreferredReplicaGroupTearDown((_, tearDown) =>
            tearDown.CanTearDownReplicaGroup(context, service, replicaGroup))
        ?? throw new ControlPlaneException(
            ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));

    private IResourceOrchestratorServiceTearDown? SelectPreferredServiceTearDown(
        Func<IResourceOrchestrator, IResourceOrchestratorServiceTearDown, bool> predicate)
    {
        var selectedId = selectionStore.Get().OrchestratorId;
        if (!string.Equals(selectedId, "default", StringComparison.OrdinalIgnoreCase))
        {
            var selected = orchestrators.FirstOrDefault(orchestrator =>
                string.Equals(orchestrator.Id, selectedId, StringComparison.OrdinalIgnoreCase) &&
                orchestrator is IResourceOrchestratorServiceTearDown tearDown &&
                predicate(orchestrator, tearDown));
            if (selected is IResourceOrchestratorServiceTearDown selectedTearDown)
            {
                return selectedTearDown;
            }
        }

        var defaultOrchestrator = orchestrators.FirstOrDefault(orchestrator =>
            string.Equals(orchestrator.Id, "default", StringComparison.OrdinalIgnoreCase) &&
            orchestrator is IResourceOrchestratorServiceTearDown tearDown &&
            predicate(orchestrator, tearDown));
        return defaultOrchestrator as IResourceOrchestratorServiceTearDown;
    }

    private IResourceOrchestratorReplicaGroupTearDown? SelectPreferredReplicaGroupTearDown(
        Func<IResourceOrchestrator, IResourceOrchestratorReplicaGroupTearDown, bool> predicate)
    {
        var selectedId = selectionStore.Get().OrchestratorId;
        if (!string.Equals(selectedId, "default", StringComparison.OrdinalIgnoreCase))
        {
            var selected = orchestrators.FirstOrDefault(orchestrator =>
                string.Equals(orchestrator.Id, selectedId, StringComparison.OrdinalIgnoreCase) &&
                orchestrator is IResourceOrchestratorReplicaGroupTearDown tearDown &&
                predicate(orchestrator, tearDown));
            if (selected is IResourceOrchestratorReplicaGroupTearDown selectedTearDown)
            {
                return selectedTearDown;
            }
        }

        var defaultOrchestrator = orchestrators.FirstOrDefault(orchestrator =>
            string.Equals(orchestrator.Id, "default", StringComparison.OrdinalIgnoreCase) &&
            orchestrator is IResourceOrchestratorReplicaGroupTearDown tearDown &&
            predicate(orchestrator, tearDown));
        return defaultOrchestrator as IResourceOrchestratorReplicaGroupTearDown;
    }

    private IResourceOrchestrator? SelectPreferredOrchestrator(
        Func<IResourceOrchestrator, bool> predicate)
    {
        var selectedId = selectionStore.Get().OrchestratorId;
        if (!string.Equals(selectedId, "default", StringComparison.OrdinalIgnoreCase))
        {
            var selected = orchestrators.FirstOrDefault(orchestrator =>
                string.Equals(orchestrator.Id, selectedId, StringComparison.OrdinalIgnoreCase) &&
                predicate(orchestrator));
            if (selected is not null)
            {
                return selected;
            }
        }

        return orchestrators.FirstOrDefault(orchestrator =>
            string.Equals(orchestrator.Id, "default", StringComparison.OrdinalIgnoreCase) &&
            predicate(orchestrator));
    }

    private async Task<string?> GetContainerHostUnavailableReasonAsync(
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        if (action.Kind is not (ResourceActionKind.Start or ResourceActionKind.Restart))
        {
            return null;
        }

        var workload = await ResolveExecutionWorkloadAsync(context, cancellationToken);
        if (workload?.Kind is not (ResourceWorkloadKind.ContainerImage or ResourceWorkloadKind.ContainerBuild))
        {
            return null;
        }

        var result = await containerHostResolver.ResolveAsync(
            new ContainerHostResolutionRequest(
                context.Resource.Id,
                context.ResourceGroup?.Id,
                ExplicitHostResourceId: workload.ContainerHostId,
                PreferredHostId: context.PreferredContainerHostId,
                RequiredCapability: GetRequiredContainerHostCapability(workload)),
            cancellationToken);

        return result.IsResolved
            ? null
            : result.ErrorMessage ??
                $"Resource '{context.Resource.Name}' is container-backed but no matching container host is available.";
    }

    private async Task<string?> GetActionUnavailableReasonAsync(
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        var providerReason = await GetProviderActionUnavailableReasonAsync(
            context,
            action,
            cancellationToken);
        if (providerReason is not null)
        {
            return providerReason;
        }

        return await GetContainerHostUnavailableReasonAsync(context, action, cancellationToken);
    }

    private async Task<string?> GetProviderActionUnavailableReasonAsync(
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        foreach (var provider in actionAvailabilityProviders)
        {
            if (!provider.CanEvaluateAction(context.Resource, action))
            {
                continue;
            }

            var reason = await provider.GetActionUnavailableReasonAsync(
                CreateProcedureContext(context),
                action,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return reason;
            }
        }

        return null;
    }

    private static ResourceProcedureContext CreateProcedureContext(
        ResourceOrchestrationContext context) =>
        new(
            context.Resource,
            context.Registration,
            context.ResourceGroup?.Id,
            context.Registrations,
            context.ResourceManager,
            context.PreferredContainerHostId);

    private async Task<ResourceWorkloadConfiguration?> ResolveExecutionWorkloadAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        var descriptor = await TryDescribeAsync(context.Resource, context, cancellationToken);
        if (descriptor is null)
        {
            return null;
        }

        var workload = TryReadWorkload(descriptor);
        if (workload is not null)
        {
            return workload;
        }

        var service = TryReadService(descriptor);
        var target = service?.Targets.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(target?.ResourceId))
        {
            return null;
        }

        var targetResource = context.ResourceManager.GetResource(target.ResourceId);
        if (targetResource is null)
        {
            return null;
        }

        var targetDescriptor = await TryDescribeAsync(targetResource, context, cancellationToken);
        return targetDescriptor is null ? null : TryReadWorkload(targetDescriptor);
    }

    private static string GetRequiredContainerHostCapability(ResourceWorkloadConfiguration workload) =>
        workload.Kind switch
        {
            ResourceWorkloadKind.ContainerBuild => ContainerHostCapabilityIds.ContainerBuild,
            _ => ContainerHostCapabilityIds.ContainerImage
        };

    private async Task<ResourceOrchestrationDescriptor?> TryDescribeAsync(
        Resource resource,
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        var provider = descriptorProviders.FirstOrDefault(provider => provider.CanDescribe(resource));
        if (provider is null)
        {
            return null;
        }

        return await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(
                registrations.GetRegistration(resource.Id),
                resourceManager.GetGroupForResource(resource.Id),
                resourceManager),
            cancellationToken);
    }

    private static ResourceWorkloadConfiguration? TryReadWorkload(
        ResourceOrchestrationDescriptor descriptor)
    {
        try
        {
            var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(SerializerOptions);
            return workload?.Kind is ResourceWorkloadKind.ContainerImage or ResourceWorkloadKind.ContainerBuild
                ? workload
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ServiceResourceDefinition? TryReadService(ResourceOrchestrationDescriptor descriptor)
    {
        if (!descriptor.ResourceType.Equals(PlatformResourceProvider.ServiceResourceType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return descriptor.Configuration.Deserialize<ServiceResourceDefinition>(SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ResourceRegistration? GetRegistrationForResourceOrAncestor(Resource resource)
    {
        var current = resource;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (visited.Add(current.Id))
        {
            var registration = registrations.GetRegistration(current.Id);
            if (registration is not null)
            {
                return registration;
            }

            if (current.ParentResourceId is null)
            {
                return null;
            }

            var parent = resourceManager.GetResource(current.ParentResourceId);
            if (parent is null)
            {
                return null;
            }

            current = parent;
        }

        return null;
    }

    private static bool ShouldStartDependencies(ResourceAction action) =>
        action.Kind is ResourceActionKind.Start or ResourceActionKind.Restart;
}
