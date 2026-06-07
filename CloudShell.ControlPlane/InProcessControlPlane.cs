using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;

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

    public Task<IReadOnlyList<CloudResource>> ListAvailableResourcesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(resourceManager.GetAvailableResources());
    }

    public Task<IReadOnlyList<CloudResource>> ListResourcesAsync(
        ResourceQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyQuery(resourceManager.GetResources(), query));
    }

    public Task<CloudResource?> GetResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(resourceManager.GetResource(resourceId));
    }

    public Task<IReadOnlyList<CloudResource>> ListResourceChildrenAsync(
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
        var provider = resourceManager.Providers
            .OfType<IResourceCreationProvider>()
            .FirstOrDefault(provider =>
                string.Equals(provider.Id, command.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                provider.CanCreate(new ResourceCreationRequest(
                    command.ResourceType,
                    command.ResourceId,
                    command.Name,
                    command.Configuration,
                    command.ResourceGroupId)))
            ?? throw new InvalidOperationException(
                $"Resource provider '{command.ProviderId}' cannot create resource type '{command.ResourceType}'.");

        await provider.CreateAsync(
            new ResourceCreationRequest(
                command.ResourceType,
                command.ResourceId,
                command.Name,
                command.Configuration,
                command.ResourceGroupId),
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
            .OfType<CloudResource>()
            .Select(CreateCapabilities)
            .ToDictionary(
                capability => capability.ResourceId,
                capability => capability,
                StringComparer.OrdinalIgnoreCase);

        return Task.FromResult<IReadOnlyDictionary<string, ResourceOperationCapabilities>>(capabilities);
    }

    public Task RegisterResourceAsync(
        RegisterResourceCommand command,
        CancellationToken cancellationToken = default) =>
        registrations.RegisterAsync(
            command.ProviderId,
            command.ResourceId,
            command.ResourceGroupId,
            command.DependsOn,
            cancellationToken);

    public Task RemoveResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        registrations.RemoveAsync(resourceId, cancellationToken);

    public Task AssignResourceGroupAsync(
        AssignResourceGroupCommand command,
        CancellationToken cancellationToken = default) =>
        registrations.AssignToGroupAsync(
            command.ResourceId,
            command.ResourceGroupId,
            command.DependsOn,
            cancellationToken);

    public Task SetResourceDependenciesAsync(
        SetResourceDependenciesCommand command,
        CancellationToken cancellationToken = default) =>
        registrations.SetDependenciesAsync(command.ResourceId, command.DependsOn, cancellationToken);

    public async Task<ResourceProcedureResult> DeleteResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new InvalidOperationException($"Resource '{resourceId}' could not be found.");

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw new UnauthorizedAccessException(
                $"The '{CloudShellPermissions.Resources.Manage}' permission is required for resource '{resource.Id}'.");
        }

        return await orchestration.DeleteAsync(resource, cancellationToken);
    }

    public async Task<ResourceProcedureResult> ExecuteResourceActionAsync(
        ExecuteResourceActionCommand command,
        CancellationToken cancellationToken = default)
    {
        var resource = resourceManager.GetResource(command.ResourceId)
            ?? throw new InvalidOperationException($"Resource '{command.ResourceId}' could not be found.");
        var action = resource.ResourceActions.FirstOrDefault(item =>
            string.Equals(item.Id, command.ActionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Resource '{resource.Name}' does not expose action '{command.ActionId}'.");

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw new UnauthorizedAccessException(
                $"The '{CloudShellPermissions.Resources.Manage}' permission is required for resource '{resource.Id}'.");
        }

        if (!command.IgnoreDependentWarning && ShouldWarnDependents(action))
        {
            var activeDependents = GetActiveDependents(resource);
            if (activeDependents.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The following running resources depend on this resource: {string.Join(", ", activeDependents.Select(dependent => dependent.Name))}. Stopping it may disrupt them.");
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

    private IReadOnlyList<CloudResource> GetActiveDependents(CloudResource resource) =>
        resourceManager.GetResources()
            .Where(candidate => candidate.State == ResourceState.Running)
            .Where(candidate => candidate.DependsOn.Contains(resource.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();

    private static bool ShouldWarnDependents(ResourceAction action) =>
        action.Kind is ResourceActionKind.Stop or ResourceActionKind.Restart or ResourceActionKind.Pause;

    private ResourceOperationCapabilities CreateCapabilities(CloudResource resource)
    {
        var group = resourceManager.GetGroupForResource(resource.Id);
        var canManage = authorization.CanAccessResource(
            resource.Id,
            group?.Id,
            CloudShellPermissions.Resources.Manage);
        var procedureProvider = GetProcedureProvider(resource);
        var executableActionIds = canManage && procedureProvider is not null
            ? resource.ResourceActions
                .Select(action => action.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new ResourceOperationCapabilities(
            resource.Id,
            canManage,
            canManage && GetRegistrationForResourceOrAncestor(resource) is not null && procedureProvider is not null,
            executableActionIds);
    }

    private IResourceProcedureProvider? GetProcedureProvider(CloudResource resource)
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

    private ResourceRegistration? GetRegistrationForResourceOrAncestor(CloudResource resource)
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

    private IReadOnlyList<CloudResource> ApplyQuery(
        IReadOnlyList<CloudResource> resources,
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
