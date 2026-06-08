using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceOrchestrationService(
    IEnumerable<IResourceOrchestrator> orchestrators,
    IEnumerable<IResourceOrchestrationDescriptorProvider> descriptorProviders,
    IEnumerable<IContainerEngineProvider> containerEngineProviders,
    IResourceManagerStore resourceManager,
    IResourceRegistrationStore registrations,
    ResourceDeclarationStore declarations,
    ResourceOrchestratorSelectionStore selectionStore)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyList<IResourceOrchestrator> orchestrators = orchestrators.ToArray();
    private readonly IReadOnlyList<IResourceOrchestrationDescriptorProvider> descriptorProviders =
        descriptorProviders.ToArray();
    private readonly IReadOnlyList<IContainerEngineProvider> containerEngineProviders =
        containerEngineProviders.ToArray();

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
        CancellationToken cancellationToken = default)
    {
        if (startDependencies && ShouldStartDependencies(action))
        {
            await StartResourceDependenciesAsync(
                resource,
                authorization,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                cancellationToken);
        }

        return await ExecuteActionCoreAsync(resource, action, cancellationToken);
    }

    private async Task<ResourceProcedureResult> ExecuteActionCoreAsync(
        Resource resource,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        var context = CreateContext(resource);
        await ValidateContainerEngineAsync(context, action, cancellationToken);
        var orchestrator = SelectActionOrchestrator(context, action);
        return await orchestrator.ExecuteActionAsync(context, action, cancellationToken);
    }

    private async Task StartResourceDependenciesAsync(
        Resource resource,
        ICloudShellAuthorizationService authorization,
        HashSet<string> visiting,
        HashSet<string> completed,
        CancellationToken cancellationToken)
    {
        if (!visiting.Add(resource.Id))
        {
            throw new InvalidOperationException(
                $"Resource dependency cycle detected at '{resource.Id}'.");
        }

        foreach (var dependencyId in resource.DependsOn.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (completed.Contains(dependencyId))
            {
                continue;
            }

            var dependency = resourceManager.GetResource(dependencyId)
                ?? throw new InvalidOperationException(
                    $"Dependency resource '{dependencyId}' could not be found.");

            if (dependency.State == ResourceState.Running)
            {
                completed.Add(dependency.Id);
                continue;
            }

            if (!declarations.ShouldAutoStart(dependency.Id))
            {
                throw new InvalidOperationException(
                    $"Dependency resource '{dependency.Name}' is not running and has auto-start disabled.");
            }

            await StartResourceDependenciesAsync(
                dependency,
                authorization,
                visiting,
                completed,
                cancellationToken);

            var runAction = dependency.ResourceActions.FirstOrDefault(action =>
                action.Kind == ResourceActionKind.Run);
            if (runAction is null)
            {
                throw new InvalidOperationException(
                    $"Dependency resource '{dependency.Name}' is not running and does not expose a Run action.");
            }

            var group = resourceManager.GetGroupForResource(dependency.Id);
            if (!authorization.CanAccessResource(
                    dependency.Id,
                    group?.Id,
                    CloudShellPermissions.Resources.Manage))
            {
                throw new UnauthorizedAccessException(
                    $"The '{CloudShellPermissions.Resources.Manage}' permission is required for dependency resource '{dependency.Id}'.");
            }

            await ExecuteActionCoreAsync(dependency, runAction, cancellationToken);
            completed.Add(dependency.Id);
        }

        visiting.Remove(resource.Id);
    }

    private ResourceOrchestrationContext CreateContext(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        return new ResourceOrchestrationContext(
            resource,
            registration,
            resourceManager.GetGroupForResource(resource.Id),
            resourceManager,
            registrations,
            selectionStore.Get().PreferredContainerEngineId);
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

    private async Task ValidateContainerEngineAsync(
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        if (action.Kind is not (ResourceActionKind.Run or ResourceActionKind.Restart))
        {
            return;
        }

        var workload = await ResolveExecutionWorkloadAsync(context, cancellationToken);
        if (workload?.Kind is not (ResourceWorkloadKind.ContainerImage or ResourceWorkloadKind.ContainerBuild))
        {
            return;
        }

        var selectedEngineId = FirstNonEmpty(
            workload.ContainerEngineId,
            context.PreferredContainerEngineId);
        if (!string.IsNullOrWhiteSpace(selectedEngineId))
        {
            if (await ResolveContainerEngineAsync(selectedEngineId, context, cancellationToken) is null)
            {
                throw new InvalidOperationException(
                    $"Container engine '{selectedEngineId}' is not registered.");
            }

            return;
        }

        if (await ResolveDefaultContainerEngineAsync(context, cancellationToken) is null)
        {
            throw new InvalidOperationException(
                $"Resource '{context.Resource.Name}' is container-backed but no default container engine is registered. Use UseDocker(), UseContainerEngine(...), or set WithContainerEngine(...).");
        }
    }

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
        if (!descriptor.ResourceType.Equals("cloudshell.service", StringComparison.OrdinalIgnoreCase))
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

    private async Task<ContainerEngineResourceDefinition?> ResolveContainerEngineAsync(
        string engineId,
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        var engine = GetContainerEngines()
            .FirstOrDefault(engine => string.Equals(engine.Id, engineId, StringComparison.OrdinalIgnoreCase));
        if (engine is not null)
        {
            return engine;
        }

        var resource = context.ResourceManager.GetResource(engineId);
        if (resource is null)
        {
            return null;
        }

        var descriptor = await TryDescribeAsync(resource, context, cancellationToken);
        return descriptor is null ? null : TryReadContainerEngine(descriptor);
    }

    private async Task<ContainerEngineResourceDefinition?> ResolveDefaultContainerEngineAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        var engine = GetContainerEngines()
            .Where(engine => engine.IsDefault)
            .OrderBy(engine => engine.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (engine is not null)
        {
            return engine;
        }

        foreach (var resource in context.ResourceManager.GetResources())
        {
            var descriptor = await TryDescribeAsync(resource, context, cancellationToken);
            if (descriptor is null)
            {
                continue;
            }

            engine = TryReadContainerEngine(descriptor);
            if (engine?.IsDefault == true)
            {
                return engine;
            }
        }

        return null;
    }

    private static ContainerEngineResourceDefinition? TryReadContainerEngine(
        ResourceOrchestrationDescriptor descriptor)
    {
        if (!descriptor.ResourceType.Equals(ContainerEngineResourceTypes.ContainerEngine, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return descriptor.Configuration.Deserialize<ContainerEngineResourceDefinition>(SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IReadOnlyList<ContainerEngineResourceDefinition> GetContainerEngines() =>
        containerEngineProviders
            .Select(provider => provider.GetContainerEngine())
            .Where(engine => !string.IsNullOrWhiteSpace(engine.Id))
            .GroupBy(engine => engine.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

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
        action.Kind is ResourceActionKind.Run or ResourceActionKind.Restart;
}
