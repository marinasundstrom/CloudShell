using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.ControlPlane.ResourceManager;

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
    IResourceEventSink? resourceEvents = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyList<IResourceOrchestrator> orchestrators = orchestrators.ToArray();
    private readonly IReadOnlyList<IResourceOrchestrationDescriptorProvider> descriptorProviders =
        descriptorProviders.ToArray();
    private readonly IReadOnlyList<IResourceActionAvailabilityProvider> actionAvailabilityProviders =
        actionAvailabilityProviders?.ToArray() ?? [];
    private readonly IContainerHostResolver containerHostResolver =
        containerHostResolver ??
        new ContainerHostResolver(
            resourceManager,
            registrations,
            descriptorProviders,
            containerHostProviders ?? []);

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
                    "Error");
                AppendLifecycleEvent(
                    resource,
                    GetLifecycleEventTypes(action)?.Failed,
                    $"{GetLifecycleFailedMessage(action)} Reason: A dependency could not start. {exception.Message}",
                    cause,
                    triggeredBy,
                    "Error");
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
                "Error");
            AppendLifecycleEvent(
                resource,
                GetLifecycleEventTypes(action)?.Failed,
                $"{GetLifecycleFailedMessage(action)} Reason: {exception.Message}",
                cause,
                triggeredBy,
                "Error");
            NotifyResourceChange(
                notifyResourceChange,
                ResourceChangeKind.ResourceActionFailed,
                resource,
                action);

            throw;
        }
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
                            $"Dependency auto-start for '{rootResource.Name}' ({rootResource.Id}).",
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
        finally
        {
            path.RemoveAt(path.Count - 1);
            visiting.Remove(resource.Id);
        }
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
            "Warning");
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
            Message = $"{result.Message} {string.Join(" ", dependencyWarnings)}"
        };
    }

    private void AppendResourceActionEvent(
        Resource resource,
        string eventType,
        string message,
        string? triggeredBy,
        string level = "Information") =>
        resourceEvents?.Append(new ResourceEvent(
            resource.Id,
            eventType,
            message,
            DateTimeOffset.UtcNow,
            triggeredBy,
            level));

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
        string level = "Information")
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
            level);
    }

    private static string GetActionEventType(ResourceAction action) =>
        ResourceEventTypes.Actions.ForAction(action.Id);

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
            dependency.Name,
            path.Select(FormatResource),
            reason,
            innerException);

    private static ControlPlaneException CreateDependencyAutoStartException(
        Resource rootResource,
        string dependencyName,
        IEnumerable<string> path,
        string reason,
        Exception? innerException = null)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "no failure reason was provided"
            : reason.Trim().TrimEnd('.');
        var message =
            $"Could not auto-start dependency '{dependencyName}' for resource '{rootResource.Name}'. " +
            $"Dependency path: {string.Join(" -> ", path)}. " +
            $"Reason: {normalizedReason}.";
        var error = ControlPlaneError.DependencyAutoStartFailed(message);
        return innerException is null
            ? new ControlPlaneException(error)
            : new ControlPlaneException(error, innerException);
    }

    private static string FormatResource(Resource resource) =>
        $"{resource.Name} ({resource.Id})";

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
