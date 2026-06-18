using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using System.Text.Json;

namespace CloudShell.ControlPlane;

public sealed class InProcessControlPlane(
    IResourceManagerStore resourceManager,
    IResourceGroupStore resourceGroups,
    IResourceRegistrationStore registrations,
    ResourceDeclarationStore declarations,
    ResourceOrchestrationService orchestration,
    ResourceIdentityProvisioningService resourceIdentityProvisioning,
    ResourceIdentityProviderSetupService resourceIdentityProviderSetup,
    ResourceTemplateService templates,
    ILogStore logs,
    ITraceStore traces,
    IMetricStore metrics,
    IEnumerable<IResourceMonitoringProvider> monitoringProviders,
    ICloudShellAuthorizationService authorization,
    IResourceEventStore? resourceEvents = null) : IControlPlane
{
    public event EventHandler<ResourceChangeNotification>? ResourcesChanged;

    public Task<IReadOnlyList<ResourceGroup>> ListResourceGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(resourceManager.GetResourceGroups());
    }

    public Task<ResourceGroup?> GetResourceGroupForResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(resourceManager.GetGroupForResource(resourceId));
    }

    public Task<ResourceGroup> CreateResourceGroupAsync(
        CreateResourceGroupCommand command,
        CancellationToken cancellationToken = default) =>
        resourceGroups.CreateAsync(command.Name, command.Description, cancellationToken);

    public Task<IReadOnlyList<Resource>> ListAvailableResourcesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(resourceManager.GetAvailableResources());
    }

    public Task<IReadOnlyList<Resource>> ListResourcesAsync(
        ResourceQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyQuery(resourceManager.GetResources(), query));
    }

    public Task<Resource?> GetResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(resourceManager.GetResource(resourceId));
    }

    public Task<IReadOnlyList<Resource>> ListResourceChildrenAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(resourceManager.GetChildren(resourceId));
    }

    public Task<IReadOnlyList<ResourceRegistration>> ListResourceRegistrationsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(registrations.GetRegistrations());
    }

    public Task<ResourceRegistration?> GetResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(registrations.GetRegistration(resourceId));
    }

    public async Task CreateResourceAsync(
        CreateResourceCommand command,
        CancellationToken cancellationToken = default)
    {
        var providerId = RequireValue(command.ProviderId, nameof(command.ProviderId));
        var resourceType = RequireValue(command.ResourceType, nameof(command.ResourceType));
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var name = RequireValue(command.Name, nameof(command.Name));
        var configuration = RequireConfiguration(command.Configuration);
        var resourceGroupId = NormalizeOptional(command.ResourceGroupId);
        var resourceClass = ResolveCreationResourceClass(resourceId, resourceType, command.ResourceClass);
        var request = new ResourceCreationRequest(
            resourceType,
            resourceId,
            name,
            configuration,
            resourceGroupId,
            resourceClass,
            NormalizeAttributes(command.ResourceAttributes));
        EnsureResourceGroupExists(resourceGroupId);
        AuthorizeCreateResource(request);

        var provider = resourceManager.Providers
            .OfType<IResourceCreationProvider>()
            .FirstOrDefault(provider =>
                string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase) &&
                provider.CanCreate(request))
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceProviderCannotCreate(providerId, resourceType));

        await provider.CreateAsync(
            request,
            new ResourceCreationContext(registrations),
            cancellationToken);

        if (command.StartAfterCreate)
        {
            await ExecuteResourceActionAsync(
                new ExecuteResourceActionCommand(
                    resourceId,
                    ResourceActionIds.Start,
                    StartDependencies: true),
                cancellationToken);
        }

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceCreated,
            resourceId,
            AffectedResourceIds: [resourceId]));
    }

    public async Task<IReadOnlyDictionary<string, ResourceOperationCapabilities>> GetResourceOperationCapabilitiesAsync(
        IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resources = resourceIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(resourceId => resourceManager.GetResource(resourceId))
            .OfType<Resource>()
            .ToArray();
        var capabilities = await Task.WhenAll(resources.Select(resource =>
            CreateCapabilitiesAsync(resource, cancellationToken)));

        return capabilities
            .ToDictionary(
                capability => capability.ResourceId,
                capability => capability,
                StringComparer.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<ResourcePermissionGrant>> ListResourcePermissionGrantsAsync(
        ResourcePermissionGrantQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyQuery(declarations.GetPermissionGrants(), query));
    }

    public Task<ResourcePermissionEvaluation> EvaluateResourcePermissionGrantAsync(
        ResourceIdentityReference identity,
        string targetResourceId,
        string permission,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(declarations
            .CreatePermissionGrantEvaluator()
            .Evaluate(identity, targetResourceId, permission));
    }

    public async Task<ResourceIdentityProvisioningResult> ProvisionResourceIdentityAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        resourceId = RequireValue(resourceId, nameof(resourceId));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));
        var group = resourceManager.GetGroupForResource(resourceId);
        if (!authorization.CanAccessResource(
                resourceId,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resourceId,
                CloudShellPermissions.Resources.Manage);
        }

        AuthorizeResourceIdentityProvisioning(resourceIdentityProvisioning.CreatePlan(resource.Id));

        return await resourceIdentityProvisioning.ProvisionResourceAsync(
            resource.Id,
            cancellationToken);
    }

    private void AuthorizeCreateResource(ResourceCreationRequest request)
    {
        if (!string.Equals(
                request.ResourceType,
                PlatformResourceProvider.VolumeResourceType,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        VolumeResourceDefinition? definition;
        try
        {
            definition = request.Configuration.Deserialize<VolumeResourceDefinition>();
        }
        catch (JsonException)
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest(
                "Volume configuration is invalid."));
        }

        var storageResourceId = NormalizeOptional(definition?.StorageResourceId);
        if (storageResourceId is null)
        {
            return;
        }

        var storageResource = resourceManager.GetResource(storageResourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(storageResourceId));
        var storageGroup = resourceManager.GetGroupForResource(storageResource.Id);
        if (!authorization.CanAccessResource(
                storageResource.Id,
                storageGroup?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                storageResource.Id,
                CloudShellPermissions.Resources.Manage);
        }
    }

    public async Task<ResourceIdentityProvisioningStatusResult> GetResourceIdentityProvisioningStatusAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        resourceId = RequireValue(resourceId, nameof(resourceId));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));
        var group = resourceManager.GetGroupForResource(resourceId);
        if (!authorization.CanAccessResource(
                resourceId,
                group?.Id,
                CloudShellPermissions.Resources.Read) &&
            !authorization.CanAccessResource(
                resourceId,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resourceId,
                FormatPermissionRequirement(CloudShellPermissions.Resources.Read));
        }

        var plan = resourceIdentityProvisioning.CreatePlan(resource.Id);
        AuthorizeResourceIdentityProvisioningStatus(plan);

        return await resourceIdentityProvisioning.GetResourceStatusAsync(
            resource.Id,
            cancellationToken);
    }

    public async Task<ResourceIdentityProviderSetupResult> SetupResourceIdentityProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        providerId = RequireValue(providerId, nameof(providerId));
        var provider = resourceIdentityProviderSetup.ResolveProvider(providerId);
        AuthorizeResourceIdentityProviderSetup(provider);

        return await resourceIdentityProviderSetup.SetupAsync(
            provider.Id,
            cancellationToken);
    }

    public async Task RegisterResourceAsync(
        RegisterResourceCommand command,
        CancellationToken cancellationToken = default)
    {
        var providerId = RequireValue(command.ProviderId, nameof(command.ProviderId));
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var resourceGroupId = NormalizeOptional(command.ResourceGroupId);
        EnsureProviderExists(providerId);
        EnsureAvailableResourceExists(resourceId);
        EnsureResourceGroupExists(resourceGroupId);

        await registrations.RegisterAsync(
            providerId,
            resourceId,
            resourceGroupId,
            NormalizeOptionalDependencies(resourceId, command.DependsOn),
            cancellationToken);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceRegistered,
            resourceId,
            AffectedResourceIds: [resourceId]));
    }

    public async Task RemoveResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        resourceId = RequireValue(resourceId, nameof(resourceId));
        await registrations.RemoveAsync(resourceId, cancellationToken);
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceRegistrationRemoved,
            resourceId,
            AffectedResourceIds: [resourceId]));
    }

    public async Task AssignResourceGroupAsync(
        AssignResourceGroupCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var resourceGroupId = NormalizeOptional(command.ResourceGroupId);
        EnsureRegisteredResourceExists(resourceId);
        EnsureResourceGroupExists(resourceGroupId);

        await registrations.AssignToGroupAsync(
            resourceId,
            resourceGroupId,
            NormalizeOptionalDependencies(resourceId, command.DependsOn),
            cancellationToken);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceGroupAssigned,
            resourceId,
            AffectedResourceIds: [resourceId]));
    }

    public async Task SetResourceDependenciesAsync(
        SetResourceDependenciesCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        EnsureRegisteredResourceExists(resourceId);

        await registrations.SetDependenciesAsync(
            resourceId,
            NormalizeRequiredDependencies(resourceId, command.DependsOn),
            cancellationToken);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceDependenciesChanged,
            resourceId,
            AffectedResourceIds: [resourceId]));
    }

    public async Task<ResourceProcedureResult> DeleteResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        resourceId = RequireValue(resourceId, nameof(resourceId));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                CloudShellPermissions.Resources.Manage);
        }

        if (GetDirectProcedureProvider(resource) is null)
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceDeleteUnsupported(resource.Name));
        }

        var result = await orchestration.DeleteAsync(resource, cancellationToken);
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceDeleted,
            resource.Id,
            AffectedResourceIds: [resource.Id]));
        return result;
    }

    public async Task<ResourceProcedureResult> ExecuteResourceActionAsync(
        ExecuteResourceActionCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var actionId = RequireValue(command.ActionId, nameof(command.ActionId));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));
        var action = resource.ResourceActions.FirstOrDefault(item =>
            string.Equals(item.Id, actionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceActionNotFound(resource.Id, actionId));

        var group = resourceManager.GetGroupForResource(resource.Id);
        var actionPermission = ResourceActionPermissions.GetRequiredPermission(action);
        if (!CanAccessResource(resource.Id, group?.Id, actionPermission, command.ActingIdentity))
        {
            RecordDeniedResourceAction(resource, action, actionPermission, command.TriggeredBy);
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                FormatPermissionRequirement(actionPermission));
        }

        if (GetProcedureProvider(resource) is null)
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceActionUnsupported(resource.Name));
        }

        var unavailableReason = GetActionUnavailableReason(resource, action);
        unavailableReason ??= await orchestration.GetActionUnavailableReasonAsync(
            resource,
            action,
            cancellationToken);
        if (unavailableReason is not null)
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceActionUnavailable(unavailableReason));
        }

        if (!command.IgnoreDependentWarning && ShouldWarnDependents(action))
        {
            var activeDependents = GetActiveDependents(resource);
            if (activeDependents.Count > 0)
            {
                throw new ControlPlaneException(ControlPlaneError.DependentResourcesRunning(
                    $"The following running resources depend on this resource: {string.Join(", ", activeDependents.Select(dependent => dependent.Name))}. Stopping it may disrupt them."));
            }
        }

        var result = await orchestration.ExecuteActionAsync(
            resource,
            action,
            command.StartDependencies,
            CreateAuthorizationService(command.ActingIdentity),
            cancellationToken,
            command.TriggeredBy,
            notifyResourceChange: NotifyResourcesChanged);

        return result;
    }

    private void RecordDeniedResourceAction(
        Resource resource,
        ResourceAction action,
        string permission,
        string? triggeredBy)
    {
        resourceEvents?.Append(new ResourceEvent(
            resource.Id,
            ResourceEventTypes.Actions.ForFailedAction(action.Id),
            $"{action.DisplayName} action was denied. The '{FormatPermissionRequirement(permission)}' permission is required for resource '{resource.Id}'.",
            DateTimeOffset.UtcNow,
            triggeredBy,
            Level: "Warning"));
    }

    public async Task<ResourceProcedureResult> UpdateResourceImageAsync(
        UpdateResourceImageCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var image = RequireValue(command.Image, nameof(command.Image));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                CloudShellPermissions.Resources.Manage);
        }

        var provider = GetImageUpdateProvider(resource);
        if (provider is null || !provider.CanUpdateImage(resource))
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceImageUpdateUnsupported(resource.Name));
        }

        var result = await provider.UpdateImageAsync(
            CreateProcedureContext(resource),
            image,
            command.RestartIfRunning,
            command.TriggeredBy,
            cancellationToken);

        var updatedResource = resourceManager.GetResource(resource.Id);
        var revision = updatedResource?.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.ContainerRevision);
        resourceEvents?.Append(new ResourceEvent(
            resource.Id,
            ResourceEventTypes.Events.Deployment.ImageUpdated,
            string.IsNullOrWhiteSpace(revision)
                ? $"Updated image to '{image}'. Restart if running: {command.RestartIfRunning.ToString().ToLowerInvariant()}."
                : $"Updated image to '{image}' and created revision '{revision}'. Restart if running: {command.RestartIfRunning.ToString().ToLowerInvariant()}.",
            DateTimeOffset.UtcNow,
            command.TriggeredBy));

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceImageUpdated,
            resource.Id,
            AffectedResourceIds: [resource.Id]));

        return result;
    }

    public async Task<ResourceProcedureResult> UpdateResourceReplicasAsync(
        UpdateResourceReplicasCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        if (command.Replicas < 1)
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest("Replicas must be greater than or equal to 1."));
        }

        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                CloudShellPermissions.Resources.Manage);
        }

        var provider = GetReplicaUpdateProvider(resource);
        if (provider is null || !provider.CanUpdateReplicas(resource))
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceReplicasUpdateUnsupported(resource.Name));
        }

        var result = await provider.UpdateReplicasAsync(
            CreateProcedureContext(resource),
            command.Replicas,
            command.RestartIfRunning,
            command.TriggeredBy,
            cancellationToken);

        resourceEvents?.Append(new ResourceEvent(
            resource.Id,
            ResourceEventTypes.Events.Deployment.ReplicasUpdated,
            $"Updated replicas to '{command.Replicas}'. Restart if running: {command.RestartIfRunning.ToString().ToLowerInvariant()}.",
            DateTimeOffset.UtcNow,
            command.TriggeredBy));

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceReplicasUpdated,
            resource.Id,
            AffectedResourceIds: [resource.Id]));

        return result;
    }

    public Task<ResourceGroupTemplateExportResult> ExportResourceGroupTemplateAsync(
        string resourceGroupId,
        CancellationToken cancellationToken = default) =>
        templates.ExportGroupAsync(resourceGroupId, cancellationToken);

    public Task<ResourceGroupTemplateImportResult> ImportResourceGroupTemplateAsync(
        ResourceGroupTemplate template,
        CancellationToken cancellationToken = default) =>
        templates.ImportGroupAsync(template, cancellationToken);

    public Task<IReadOnlyList<LogDescriptor>> ListLogsAsync(
        LogQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyQuery(logs.GetLogs(), query));
    }

    public Task<IReadOnlyList<ResourceEvent>> ListResourceEventsAsync(
        ResourceEventQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (resourceEvents is null)
        {
            return Task.FromResult<IReadOnlyList<ResourceEvent>>([]);
        }

        var visibleResourceIds = resourceManager.GetResources()
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (query?.ResourceId is not null &&
            !visibleResourceIds.Contains(query.ResourceId))
        {
            return Task.FromResult<IReadOnlyList<ResourceEvent>>([]);
        }

        var events = resourceEvents
            .GetEvents(query)
            .Where(resourceEvent => visibleResourceIds.Contains(resourceEvent.ResourceId))
            .ToArray();

        return Task.FromResult<IReadOnlyList<ResourceEvent>>(events);
    }

    public Task<LogDescriptor?> GetLogAsync(
        string logId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(logs.GetLog(logId));
    }

    public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        ReadLogOptions? options = null,
        CancellationToken cancellationToken = default) =>
        logs.ReadLogAsync(
            logId,
            options?.MaxEntries ?? 200,
            options?.Before,
            cancellationToken);

    public IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        StreamLogOptions? options = null,
        CancellationToken cancellationToken = default) =>
        logs.StreamLogAsync(logId, options?.InitialEntries ?? 50, cancellationToken);

    public Task<IReadOnlyList<TraceSpan>> ListTraceSpansAsync(
        TraceQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(traces.GetSpans(
            query?.ResourceId,
            query?.TraceId,
            query?.MaxSpans ?? 200));
    }

    public Task IngestTraceSpansAsync(
        IEnumerable<TraceSpan> spans,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        traces.AddSpans(spans);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MetricPoint>> ListMetricPointsAsync(
        MetricQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(metrics.GetPoints(
            query?.ResourceId,
            query?.MetricName,
            query?.MaxPoints ?? 200));
    }

    public Task IngestMetricPointsAsync(
        IEnumerable<MetricPoint> points,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        metrics.AddPoints(points);
        return Task.CompletedTask;
    }

    public Task<bool> HasResourceMonitoringAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = resourceManager.GetResource(resourceId);
        return Task.FromResult(resource is not null &&
            monitoringProviders.Any(provider => provider.CanMonitor(resource)));
    }

    public async Task<ResourceMonitoringSnapshot?> GetResourceMonitoringAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = resourceManager.GetResource(resourceId);
        if (resource is null)
        {
            return null;
        }

        foreach (var provider in monitoringProviders)
        {
            if (!provider.CanMonitor(resource))
            {
                continue;
            }

            var snapshot = await provider.GetMonitoringSnapshotAsync(resource, cancellationToken);
            if (snapshot is not null)
            {
                return snapshot;
            }
        }

        return null;
    }

    private IReadOnlyList<Resource> GetActiveDependents(Resource resource) =>
        resourceManager.GetResources()
            .Where(candidate => candidate.State == ResourceState.Running)
            .Where(candidate => candidate.DependsOn.Contains(resource.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();

    private void NotifyResourcesChanged(ResourceChangeNotification notification) =>
        ResourcesChanged?.Invoke(this, notification);

    private static bool ShouldWarnDependents(ResourceAction action) =>
        action.Kind is ResourceActionKind.Stop or ResourceActionKind.Restart or ResourceActionKind.Pause;

    private async Task<ResourceOperationCapabilities> CreateCapabilitiesAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        var group = resourceManager.GetGroupForResource(resource.Id);
        var canManage = authorization.CanAccessResource(
            resource.Id,
            group?.Id,
            CloudShellPermissions.Resources.Manage);
        var procedureProvider = GetProcedureProvider(resource);
        var actionCapabilities = await Task.WhenAll(resource.ResourceActions
            .Select(action => CreateActionCapabilityAsync(
                resource,
                action,
                CanAccessResource(
                    resource.Id,
                    group?.Id,
                    ResourceActionPermissions.GetRequiredPermission(action)),
                procedureProvider is not null,
                cancellationToken)));
        var executableActionIds = actionCapabilities
            .Where(capability => capability.CanExecute)
            .Select(capability => capability.ActionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ResourceOperationCapabilities(
            resource.Id,
            canManage,
            canManage && GetDirectProcedureProvider(resource) is not null,
            executableActionIds,
            actionCapabilities);
    }

    private async Task<ResourceActionCapability> CreateActionCapabilityAsync(
        Resource resource,
        ResourceAction action,
        bool canExecute,
        bool hasProcedureProvider,
        CancellationToken cancellationToken)
    {
        if (!canExecute)
        {
            var permission = ResourceActionPermissions.GetRequiredPermission(action);
            return new ResourceActionCapability(
                action.Id,
                false,
                $"The '{FormatPermissionRequirement(permission)}' permission is required for resource '{resource.Id}'.");
        }

        if (!hasProcedureProvider)
        {
            return new ResourceActionCapability(
                action.Id,
                false,
                "The resource provider does not support procedures.");
        }

        var unavailableReason = GetActionUnavailableReason(resource, action);
        unavailableReason ??= await orchestration.GetActionUnavailableReasonAsync(
            resource,
            action,
            cancellationToken);
        return new ResourceActionCapability(
            action.Id,
            unavailableReason is null,
            unavailableReason);
    }

    private bool CanAccessResource(
        string resourceId,
        string? resourceGroupId,
        string permission,
        ResourceIdentityReference? actingIdentity = null)
    {
        if (actingIdentity is null)
        {
            return authorization.CanAccessResource(resourceId, resourceGroupId, permission) ||
                !string.Equals(permission, CloudShellPermissions.Resources.Manage, StringComparison.OrdinalIgnoreCase) &&
                authorization.CanAccessResource(resourceId, resourceGroupId, CloudShellPermissions.Resources.Manage);
        }

        return ResourceIdentityCanAccessResource(actingIdentity, resourceId, permission);
    }

    private void AuthorizeResourceIdentityProvisioning(ResourceIdentityProvisioningPlan plan)
    {
        foreach (var provisioningResourceId in plan.Requests
            .Select(request => request.Provider.ProvisioningResourceId)
            .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var provisioningResource = resourceManager.GetResource(provisioningResourceId!)
                ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(provisioningResourceId!));
            var group = resourceManager.GetGroupForResource(provisioningResource.Id);
            if (!authorization.CanAccessResource(
                    provisioningResource.Id,
                    group?.Id,
                    ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities) &&
                !authorization.CanAccessResource(
                    provisioningResource.Id,
                    group?.Id,
                    CloudShellPermissions.Resources.Manage))
            {
                throw ControlPlaneAccessDeniedException.ForResource(
                    provisioningResource.Id,
                    FormatPermissionRequirement(ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities));
            }
        }
    }

    private void AuthorizeResourceIdentityProvisioningStatus(ResourceIdentityProvisioningPlan plan)
    {
        foreach (var provisioningResourceId in plan.Requests
            .Select(request => request.Provider.ProvisioningResourceId)
            .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var provisioningResource = resourceManager.GetResource(provisioningResourceId!)
                ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(provisioningResourceId!));
            var group = resourceManager.GetGroupForResource(provisioningResource.Id);
            if (!authorization.CanAccessResource(
                    provisioningResource.Id,
                    group?.Id,
                    CloudShellPermissions.Resources.Read) &&
                !authorization.CanAccessResource(
                    provisioningResource.Id,
                    group?.Id,
                    CloudShellPermissions.Resources.Manage))
            {
                throw ControlPlaneAccessDeniedException.ForResource(
                    provisioningResource.Id,
                    FormatPermissionRequirement(CloudShellPermissions.Resources.Read));
            }
        }
    }

    private void AuthorizeResourceIdentityProviderSetup(ResourceIdentityProviderDefinition provider)
    {
        if (string.IsNullOrWhiteSpace(provider.ProvisioningResourceId))
        {
            if (!authorization.HasPermission(CloudShellPermissions.Resources.Manage))
            {
                throw new ControlPlaneAccessDeniedException(new ControlPlaneError(
                    ControlPlaneErrorCodes.InsufficientPermission,
                    $"The '{CloudShellPermissions.Resources.Manage}' permission is required to set up resource identity provider '{provider.Id}'."));
            }

            return;
        }

        var provisioningResource = resourceManager.GetResource(provider.ProvisioningResourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(provider.ProvisioningResourceId));
        var group = resourceManager.GetGroupForResource(provisioningResource.Id);
        if (!authorization.CanAccessResource(
                provisioningResource.Id,
                group?.Id,
                ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities) &&
            !authorization.CanAccessResource(
                provisioningResource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                provisioningResource.Id,
                FormatPermissionRequirement(ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities));
        }
    }

    private bool ResourceIdentityCanAccessResource(
        ResourceIdentityReference identity,
        string resourceId,
        string permission)
    {
        var evaluator = declarations.CreatePermissionGrantEvaluator();
        return evaluator.Evaluate(identity, resourceId, permission).IsAllowed ||
            !string.Equals(permission, CloudShellPermissions.Resources.Manage, StringComparison.OrdinalIgnoreCase) &&
            evaluator.Evaluate(identity, resourceId, CloudShellPermissions.Resources.Manage).IsAllowed;
    }

    private ICloudShellAuthorizationService CreateAuthorizationService(ResourceIdentityReference? actingIdentity) =>
        actingIdentity is null
            ? authorization
            : new ResourcePermissionGrantAuthorizationService(this, actingIdentity);

    private static string FormatPermissionRequirement(string permission) =>
        string.Equals(permission, CloudShellPermissions.Resources.Manage, StringComparison.OrdinalIgnoreCase)
            ? permission
            : $"{permission}' or '{CloudShellPermissions.Resources.Manage}";

    private static string? GetActionUnavailableReason(
        Resource resource,
        ResourceAction action) =>
        action.Kind switch
        {
            ResourceActionKind.Start when resource.State is not (
                ResourceState.Stopped or
                ResourceState.Paused or
                ResourceState.Unknown) =>
                $"Resource '{resource.Name}' cannot start while it is {FormatState(resource.State)}.",
            ResourceActionKind.Stop when resource.State is not (
                ResourceState.Running or
                ResourceState.Starting or
                ResourceState.Stopping or
                ResourceState.Paused or
                ResourceState.Degraded) =>
                $"Resource '{resource.Name}' cannot stop while it is {FormatState(resource.State)}.",
            ResourceActionKind.Pause when resource.State is not (ResourceState.Running or ResourceState.Degraded) =>
                $"Resource '{resource.Name}' cannot pause while it is {FormatState(resource.State)}.",
            ResourceActionKind.Restart when resource.State is ResourceState.Stopping or ResourceState.Stopped or ResourceState.Paused or ResourceState.Unknown =>
                $"Resource '{resource.Name}' cannot restart while it is {FormatState(resource.State)}.",
            _ => null
        };

    private static string FormatState(ResourceState? state) =>
        state?.ToString().ToLowerInvariant() ?? "no lifecycle status";

    private IResourceProcedureProvider? GetProcedureProvider(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        if (registration is not null)
        {
            return resourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceProcedureProvider;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceProcedureProvider;
    }

    private IResourceProcedureProvider? GetDirectProcedureProvider(Resource resource)
    {
        var registration = registrations.GetRegistration(resource.Id);
        if (registration is null)
        {
            return null;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
            as IResourceProcedureProvider;
    }

    private IResourceImageUpdateProvider? GetImageUpdateProvider(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        if (registration is not null)
        {
            return resourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceImageUpdateProvider;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceImageUpdateProvider;
    }

    private IResourceReplicaUpdateProvider? GetReplicaUpdateProvider(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        if (registration is not null)
        {
            return resourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceReplicaUpdateProvider;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceReplicaUpdateProvider;
    }

    private ResourceProcedureContext CreateProcedureContext(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        var group = resourceManager.GetGroupForResource(resource.Id);
        return new ResourceProcedureContext(
            resource,
            registration,
            group?.Id,
            registrations,
            resourceManager,
            orchestration.PreferredContainerHostId);
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

    private void EnsureProviderExists(string providerId)
    {
        if (!resourceManager.Providers.Any(provider =>
                string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceProviderNotFound(providerId));
        }
    }

    private void EnsureAvailableResourceExists(string resourceId)
    {
        if (!resourceManager.GetAvailableResources().Any(resource =>
                string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceNotAvailable(resourceId));
        }
    }

    private void EnsureRegisteredResourceExists(string resourceId)
    {
        if (resourceManager.GetResource(resourceId) is null)
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));
        }
    }

    private void EnsureResourceGroupExists(string? resourceGroupId)
    {
        if (resourceGroupId is null)
        {
            return;
        }

        if (!resourceManager.GetResourceGroups().Any(group =>
                string.Equals(group.Id, resourceGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceGroupNotFound(resourceGroupId));
        }
    }

    private ResourceClass? ResolveCreationResourceClass(
        string resourceId,
        string resourceType,
        ResourceClass? resourceClass)
    {
        var expectedResourceClass = resourceManager.GetResourceTypeClass(resourceType);
        if (expectedResourceClass is null)
        {
            return resourceClass;
        }

        var result = ResourceModelValidation.ResolveResourceClass(
            resourceId,
            resourceType,
            expectedResourceClass.Value,
            resourceClass,
            "creation request");

        return result.Succeeded
            ? result.ResourceClass
            : throw new ControlPlaneException(
                ControlPlaneError.ResourceClassMismatch(result.Diagnostic!.Message));
    }

    private IReadOnlyList<string>? NormalizeOptionalDependencies(
        string resourceId,
        IReadOnlyList<string>? dependsOn) =>
        dependsOn is null
            ? null
            : NormalizeDependencies(resourceId, dependsOn);

    private IReadOnlyList<string> NormalizeRequiredDependencies(
        string resourceId,
        IReadOnlyList<string> dependsOn) =>
        NormalizeDependencies(resourceId, dependsOn);

    private IReadOnlyList<string> NormalizeDependencies(
        string resourceId,
        IReadOnlyList<string> dependsOn)
    {
        var dependencies = new List<string>(dependsOn.Count);
        foreach (var dependency in dependsOn)
        {
            var dependencyId = RequireValue(dependency, nameof(dependsOn));
            if (string.Equals(dependencyId, resourceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ControlPlaneException(ControlPlaneError.ResourceSelfDependency(resourceId));
            }

            EnsureAvailableResourceExists(dependencyId);
            dependencies.Add(dependencyId);
        }

        return dependencies
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string RequireValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest($"{name} is required."));
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyDictionary<string, string>? NormalizeAttributes(
        IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized[name.Trim()] = value.Trim();
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static JsonElement RequireConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest("Configuration is required."));
        }

        return configuration;
    }

    private IReadOnlyList<Resource> ApplyQuery(
        IReadOnlyList<Resource> resources,
        ResourceQuery? query)
    {
        if (query is null)
        {
            return resources;
        }

        var filtered = resources.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.ResourceGroupId))
        {
            filtered = filtered.Where(resource =>
                string.Equals(
                    resourceManager.GetGroupForResource(resource.Id)?.Id,
                    query.ResourceGroupId,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.ParentResourceId))
        {
            filtered = filtered.Where(resource =>
                string.Equals(
                    resource.ParentResourceId,
                    query.ParentResourceId,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceType))
        {
            filtered = filtered.Where(resource =>
                string.Equals(resource.EffectiveTypeId, query.ResourceType, StringComparison.OrdinalIgnoreCase));
        }

        if (query.IsRegistered is not null)
        {
            filtered = filtered.Where(resource =>
                resourceManager.IsRegistered(resource.Id) == query.IsRegistered);
        }

        if (query.ResourceClass is not null)
        {
            filtered = filtered.Where(resource => resource.ResourceClass == query.ResourceClass);
        }

        return filtered.ToArray();
    }

    private static IReadOnlyList<ResourcePermissionGrant> ApplyQuery(
        IReadOnlyList<ResourcePermissionGrant> grants,
        ResourcePermissionGrantQuery? query)
    {
        if (query is null)
        {
            return grants;
        }

        var filtered = grants.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.IdentityResourceId))
        {
            filtered = filtered.Where(grant =>
                string.Equals(grant.Identity.ResourceId, query.IdentityResourceId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.IdentityName))
        {
            filtered = filtered.Where(grant =>
                string.Equals(grant.Identity.Name, query.IdentityName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.TargetResourceId))
        {
            filtered = filtered.Where(grant =>
                string.Equals(grant.TargetResourceId, query.TargetResourceId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Permission))
        {
            filtered = filtered.Where(grant =>
                string.Equals(grant.Permission, query.Permission, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToArray();
    }

    private static IReadOnlyList<LogDescriptor> ApplyQuery(
        IReadOnlyList<LogDescriptor> descriptors,
        LogQuery? query)
    {
        if (query is null)
        {
            return descriptors;
        }

        var filtered = descriptors.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.ResourceId))
        {
            filtered = filtered.Where(log =>
                string.Equals(log.ResourceId, query.ResourceId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.ArtifactId))
        {
            filtered = filtered.Where(log =>
                string.Equals(log.ArtifactId, query.ArtifactId, StringComparison.OrdinalIgnoreCase));
        }

        if (query.SourceKind is not null)
        {
            filtered = filtered.Where(log => log.SourceKind == query.SourceKind);
        }

        return filtered.ToArray();
    }

    private sealed class ResourcePermissionGrantAuthorizationService(
        InProcessControlPlane controlPlane,
        ResourceIdentityReference identity) : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => false;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => false;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) =>
            controlPlane.ResourceIdentityCanAccessResource(identity, resourceId, permission);
    }
}
