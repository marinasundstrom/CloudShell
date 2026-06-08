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
    ResourceOrchestrationService orchestration,
    ResourceTemplateService templates,
    ILogStore logs,
    ITraceStore traces,
    ICloudShellAuthorizationService authorization) : IControlPlane
{
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
    }

    public Task<IReadOnlyDictionary<string, ResourceOperationCapabilities>> GetResourceOperationCapabilitiesAsync(
        IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var capabilities = resourceIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(resourceId => resourceManager.GetResource(resourceId))
            .OfType<Resource>()
            .Select(CreateCapabilities)
            .ToDictionary(
                capability => capability.ResourceId,
                capability => capability,
                StringComparer.OrdinalIgnoreCase);

        return Task.FromResult<IReadOnlyDictionary<string, ResourceOperationCapabilities>>(capabilities);
    }

    public Task RegisterResourceAsync(
        RegisterResourceCommand command,
        CancellationToken cancellationToken = default)
    {
        var providerId = RequireValue(command.ProviderId, nameof(command.ProviderId));
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var resourceGroupId = NormalizeOptional(command.ResourceGroupId);
        EnsureProviderExists(providerId);
        EnsureAvailableResourceExists(resourceId);
        EnsureResourceGroupExists(resourceGroupId);

        return registrations.RegisterAsync(
            providerId,
            resourceId,
            resourceGroupId,
            NormalizeOptionalDependencies(resourceId, command.DependsOn),
            cancellationToken);
    }

    public Task RemoveResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        registrations.RemoveAsync(resourceId, cancellationToken);

    public Task AssignResourceGroupAsync(
        AssignResourceGroupCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var resourceGroupId = NormalizeOptional(command.ResourceGroupId);
        EnsureRegisteredResourceExists(resourceId);
        EnsureResourceGroupExists(resourceGroupId);

        return registrations.AssignToGroupAsync(
            resourceId,
            resourceGroupId,
            NormalizeOptionalDependencies(resourceId, command.DependsOn),
            cancellationToken);
    }

    public Task SetResourceDependenciesAsync(
        SetResourceDependenciesCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        EnsureRegisteredResourceExists(resourceId);

        return registrations.SetDependenciesAsync(
            resourceId,
            NormalizeRequiredDependencies(resourceId, command.DependsOn),
            cancellationToken);
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

        return await orchestration.DeleteAsync(resource, cancellationToken);
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
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                CloudShellPermissions.Resources.Manage);
        }

        if (GetProcedureProvider(resource) is null)
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceActionUnsupported(resource.Name));
        }

        var unavailableReason = GetActionUnavailableReason(resource, action);
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

        return await orchestration.ExecuteActionAsync(
            resource,
            action,
            command.StartDependencies,
            authorization,
            cancellationToken);
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

    private IReadOnlyList<Resource> GetActiveDependents(Resource resource) =>
        resourceManager.GetResources()
            .Where(candidate => candidate.State == ResourceState.Running)
            .Where(candidate => candidate.DependsOn.Contains(resource.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();

    private static bool ShouldWarnDependents(ResourceAction action) =>
        action.Kind is ResourceActionKind.Stop or ResourceActionKind.Restart or ResourceActionKind.Pause;

    private ResourceOperationCapabilities CreateCapabilities(Resource resource)
    {
        var group = resourceManager.GetGroupForResource(resource.Id);
        var canManage = authorization.CanAccessResource(
            resource.Id,
            group?.Id,
            CloudShellPermissions.Resources.Manage);
        var procedureProvider = GetProcedureProvider(resource);
        var actionCapabilities = resource.ResourceActions
            .Select(action => CreateActionCapability(resource, action, canManage, procedureProvider is not null))
            .ToArray();
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

    private static ResourceActionCapability CreateActionCapability(
        Resource resource,
        ResourceAction action,
        bool canManage,
        bool hasProcedureProvider)
    {
        if (!canManage)
        {
            return new ResourceActionCapability(
                action.Id,
                false,
                $"The '{CloudShellPermissions.Resources.Manage}' permission is required.");
        }

        if (!hasProcedureProvider)
        {
            return new ResourceActionCapability(
                action.Id,
                false,
                "The resource provider does not support procedures.");
        }

        var unavailableReason = GetActionUnavailableReason(resource, action);
        return new ResourceActionCapability(
            action.Id,
            unavailableReason is null,
            unavailableReason);
    }

    private static string? GetActionUnavailableReason(
        Resource resource,
        ResourceAction action) =>
        action.Kind switch
        {
            ResourceActionKind.Run when resource.State is not (
                ResourceState.Stopped or
                ResourceState.Paused or
                ResourceState.Unknown) =>
                $"Resource '{resource.Name}' cannot run while it is {FormatState(resource.State)}.",
            ResourceActionKind.Stop when resource.State is not (
                ResourceState.Running or
                ResourceState.Starting or
                ResourceState.Paused or
                ResourceState.Degraded) =>
                $"Resource '{resource.Name}' cannot stop while it is {FormatState(resource.State)}.",
            ResourceActionKind.Pause when resource.State is not (ResourceState.Running or ResourceState.Degraded) =>
                $"Resource '{resource.Name}' cannot pause while it is {FormatState(resource.State)}.",
            ResourceActionKind.Restart when resource.State is ResourceState.Stopped or ResourceState.Paused or ResourceState.Unknown =>
                $"Resource '{resource.Name}' cannot restart while it is {FormatState(resource.State)}.",
            _ => null
        };

    private static string FormatState(ResourceState state) =>
        state.ToString().ToLowerInvariant();

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
}
