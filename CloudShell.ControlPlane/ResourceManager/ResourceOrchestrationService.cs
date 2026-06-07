using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceOrchestrationService(
    IEnumerable<IResourceOrchestrator> orchestrators,
    IResourceManagerStore resourceManager,
    IResourceRegistrationStore registrations,
    ResourceOrchestratorSelectionStore selectionStore)
{
    private readonly IReadOnlyList<IResourceOrchestrator> orchestrators = orchestrators.ToArray();

    public async Task<ResourceProcedureResult> DeleteAsync(
        CloudResource resource,
        CancellationToken cancellationToken = default)
    {
        var context = CreateContext(resource);
        var orchestrator = SelectDeleteOrchestrator(context);
        return await orchestrator.DeleteAsync(context, cancellationToken);
    }

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        CloudResource resource,
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
        CloudResource resource,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        var context = CreateContext(resource);
        var orchestrator = SelectActionOrchestrator(context, action);
        return await orchestrator.ExecuteActionAsync(context, action, cancellationToken);
    }

    private async Task StartResourceDependenciesAsync(
        CloudResource resource,
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

            await StartResourceDependenciesAsync(
                dependency,
                authorization,
                visiting,
                completed,
                cancellationToken);

            if (dependency.State == ResourceState.Running)
            {
                completed.Add(dependency.Id);
                continue;
            }

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

    private ResourceOrchestrationContext CreateContext(CloudResource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        return new ResourceOrchestrationContext(
            resource,
            registration,
            resourceManager.GetGroupForResource(resource.Id),
            resourceManager,
            registrations);
    }

    private IResourceOrchestrator SelectActionOrchestrator(
        ResourceOrchestrationContext context,
        ResourceAction action) =>
        SelectPreferredOrchestrator(orchestrator => orchestrator.CanExecute(context, action))
        ?? throw new InvalidOperationException(
            $"Resource '{context.Resource.Name}' does not support action '{action.DisplayName}'.");

    private IResourceOrchestrator SelectDeleteOrchestrator(
        ResourceOrchestrationContext context) =>
        SelectPreferredOrchestrator(orchestrator => orchestrator.CanDelete(context))
        ?? throw new InvalidOperationException(
            $"Resource '{context.Resource.Name}' does not support delete.");

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

    private static bool ShouldStartDependencies(ResourceAction action) =>
        action.Kind is ResourceActionKind.Run or ResourceActionKind.Restart;
}
