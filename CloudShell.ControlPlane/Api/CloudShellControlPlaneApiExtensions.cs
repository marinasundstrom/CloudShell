using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace CloudShell.ControlPlane.Api;

public static class CloudShellControlPlaneApiExtensions
{
    public static IServiceCollection AddCloudShellControlPlaneOpenApi(
        this IServiceCollection services)
    {
        services.AddOpenApi(CloudShellControlPlaneApiDefaults.DocumentName);
        return services;
    }

    public static IEndpointRouteBuilder MapCloudShellControlPlaneOpenApi(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapOpenApi(CloudShellControlPlaneApiDefaults.OpenApiRoutePattern)
            .AllowAnonymous();

        return endpoints;
    }

    public static RouteGroupBuilder MapCloudShellControlPlaneApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints
            .MapGroup(CloudShellControlPlaneApiDefaults.RoutePrefix)
            .WithTags("Control Plane")
            .WithGroupName(CloudShellControlPlaneApiDefaults.DocumentName);

        api.MapGet("/resources", ListResources)
            .WithName("CloudShellControlPlane_ListResources");

        api.MapGet("/resources/available", ListAvailableResources)
            .WithName("CloudShellControlPlane_ListAvailableResources");

        api.MapGet("/resources/{resourceId}", GetResource)
            .WithName("CloudShellControlPlane_GetResource");

        api.MapGet("/resources/{resourceId}/children", ListResourceChildren)
            .WithName("CloudShellControlPlane_ListResourceChildren");

        api.MapDelete("/resources/{resourceId}", DeleteResource)
            .WithName("CloudShellControlPlane_DeleteResource");

        api.MapPost("/resources/{resourceId}/actions/{actionId}", ExecuteResourceAction)
            .WithName("CloudShellControlPlane_ExecuteResourceAction");

        api.MapGet("/resource-groups", ListResourceGroups)
            .WithName("CloudShellControlPlane_ListResourceGroups");

        api.MapPost("/resource-groups", CreateResourceGroup)
            .WithName("CloudShellControlPlane_CreateResourceGroup");

        api.MapGet("/registrations", ListRegistrations)
            .WithName("CloudShellControlPlane_ListRegistrations");

        api.MapPost("/registrations", RegisterResource)
            .WithName("CloudShellControlPlane_RegisterResource");

        api.MapDelete("/registrations/{resourceId}", RemoveRegistration)
            .WithName("CloudShellControlPlane_RemoveRegistration");

        api.MapPut("/registrations/{resourceId}/group", AssignResourceGroup)
            .WithName("CloudShellControlPlane_AssignResourceGroup");

        api.MapPut("/registrations/{resourceId}/dependencies", SetResourceDependencies)
            .WithName("CloudShellControlPlane_SetResourceDependencies");

        api.MapGet("/logs", ListLogs)
            .WithName("CloudShellControlPlane_ListLogs");

        api.MapGet("/logs/{logId}/entries", ReadLogEntries)
            .WithName("CloudShellControlPlane_ReadLogEntries");

        api.MapGet("/logs/{logId}/stream", StreamLogEntries)
            .WithName("CloudShellControlPlane_StreamLogEntries");

        return api;
    }

    private static IResult ListResources(IResourceManagerStore resourceManager) =>
        Results.Ok(resourceManager
            .GetResources()
            .Select(resource => CreateResourceResponse(resourceManager, resource))
            .ToArray());

    private static IResult ListAvailableResources(
        IResourceManagerStore resourceManager,
        ICloudShellAuthorizationService authorization)
    {
        if (!authorization.HasPermission(CloudShellPermissions.Resources.Create))
        {
            return Results.Forbid();
        }

        return Results.Ok(resourceManager
            .GetAvailableResources()
            .Select(resource => CreateResourceResponse(resourceManager, resource))
            .ToArray());
    }

    private static IResult GetResource(
        string resourceId,
        IResourceManagerStore resourceManager)
    {
        var resource = resourceManager.GetResource(resourceId);
        return resource is null
            ? Results.NotFound()
            : Results.Ok(CreateResourceResponse(resourceManager, resource));
    }

    private static IResult ListResourceChildren(
        string resourceId,
        IResourceManagerStore resourceManager)
    {
        if (resourceManager.GetResource(resourceId) is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(resourceManager
            .GetChildren(resourceId)
            .Select(resource => CreateResourceResponse(resourceManager, resource))
            .ToArray());
    }

    private static async Task<IResult> DeleteResource(
        string resourceId,
        IResourceManagerStore resourceManager,
        IResourceRegistrationStore registrations,
        ICloudShellAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var resource = resourceManager.GetResource(resourceId);
        if (resource is null)
        {
            return Results.NotFound();
        }

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            return Results.Forbid();
        }

        var registration = registrations.GetRegistration(resource.Id);
        var provider = registration is null
            ? null
            : resourceManager.Providers.FirstOrDefault(item =>
                string.Equals(item.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase));

        if (provider is not IResourceProcedureProvider procedureProvider)
        {
            return Problem(
                StatusCodes.Status405MethodNotAllowed,
                "Unsupported resource procedure",
                "The selected provider does not support deleting this resource.");
        }

        try
        {
            var result = await procedureProvider.DeleteAsync(
                new ResourceProcedureContext(resource, registration, group?.Id, registrations),
                cancellationToken);

            return Results.Ok(new ResourceProcedureResponse(result.Message));
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ExecuteResourceAction(
        string resourceId,
        string actionId,
        bool startDependencies,
        bool ignoreDependentWarning,
        IResourceManagerStore resourceManager,
        IResourceRegistrationStore registrations,
        ICloudShellAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var resource = resourceManager.GetResource(resourceId);
        if (resource is null)
        {
            return Results.NotFound();
        }

        var action = resource.ResourceActions.FirstOrDefault(item =>
            string.Equals(item.Id, actionId, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            return Results.NotFound();
        }

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            return Results.Forbid();
        }

        var provider = GetProcedureProvider(resourceManager, registrations, resource);
        if (provider is null)
        {
            return Problem(
                StatusCodes.Status405MethodNotAllowed,
                "Unsupported resource action",
                "The selected provider does not support actions for this resource.");
        }

        try
        {
            var registration = GetRegistrationForResourceOrAncestor(
                resourceManager,
                registrations,
                resource);
            if (!ignoreDependentWarning && ShouldWarnDependents(action))
            {
                var activeDependents = GetActiveDependents(resource, resourceManager);
                if (activeDependents.Count > 0)
                {
                    return Problem(
                        StatusCodes.Status409Conflict,
                        "Dependent resources are running",
                        $"The following running resources depend on this resource: {string.Join(", ", activeDependents.Select(dependent => dependent.Name))}. Stopping it may disrupt them. Do you want to stop the resource?");
                }
            }

            if (startDependencies && ShouldStartDependencies(action))
            {
                await StartResourceDependenciesAsync(
                    resource,
                    resourceManager,
                    registrations,
                    authorization,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    cancellationToken);
            }

            var result = await provider.ExecuteActionAsync(
                new ResourceProcedureContext(resource, registration, group?.Id, registrations),
                action,
                cancellationToken);

            return Results.Ok(new ResourceProcedureResponse(result.Message));
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static IResult ListResourceGroups(IResourceManagerStore resourceManager) =>
        Results.Ok(resourceManager
            .GetResourceGroups()
            .Select(group => group.ToResponse())
            .ToArray());

    private static async Task<IResult> CreateResourceGroup(
        CreateResourceGroupRequest request,
        IResourceGroupStore groups,
        CancellationToken cancellationToken)
    {
        try
        {
            var group = await groups.CreateAsync(
                request.Name,
                request.Description ?? string.Empty,
                cancellationToken);

            return Results.Created(
                $"{CloudShellControlPlaneApiDefaults.RoutePrefix}/resource-groups/{Uri.EscapeDataString(group.Id)}",
                group.ToResponse());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static IResult ListRegistrations(IResourceRegistrationStore registrations) =>
        Results.Ok(registrations
            .GetRegistrations()
            .Select(registration => registration.ToResponse())
            .ToArray());

    private static async Task<IResult> RegisterResource(
        RegisterResourceRequest request,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken)
    {
        try
        {
            await registrations.RegisterAsync(
                request.ProviderId,
                request.ResourceId,
                request.ResourceGroupId,
                request.DependsOn,
                cancellationToken);

            var registration = registrations.GetRegistration(request.ResourceId);
            return registration is null
                ? Results.NoContent()
                : Results.Created(
                    $"{CloudShellControlPlaneApiDefaults.RoutePrefix}/registrations/{Uri.EscapeDataString(registration.ResourceId)}",
                    registration.ToResponse());
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> RemoveRegistration(
        string resourceId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken)
    {
        try
        {
            await registrations.RemoveAsync(resourceId, cancellationToken);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> AssignResourceGroup(
        string resourceId,
        AssignResourceGroupRequest request,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken)
    {
        try
        {
            await registrations.AssignToGroupAsync(
                resourceId,
                request.ResourceGroupId,
                cancellationToken: cancellationToken);

            var registration = registrations.GetRegistration(resourceId);
            return registration is null
                ? Results.NoContent()
                : Results.Ok(registration.ToResponse());
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> SetResourceDependencies(
        string resourceId,
        SetResourceDependenciesRequest request,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken)
    {
        try
        {
            await registrations.SetDependenciesAsync(
                resourceId,
                request.DependsOn,
                cancellationToken);

            var registration = registrations.GetRegistration(resourceId);
            return registration is null
                ? Results.NoContent()
                : Results.Ok(registration.ToResponse());
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static IResult ListLogs(ILogStore logs) =>
        Results.Ok(logs.GetLogs().Select(log => log.ToResponse()).ToArray());

    private static async Task<IResult> ReadLogEntries(
        string logId,
        int? maxEntries,
        DateTimeOffset? before,
        ILogStore logs,
        CancellationToken cancellationToken)
    {
        if (logs.GetLog(logId) is null)
        {
            return Results.NotFound();
        }

        var entries = await logs.ReadLogAsync(
            logId,
            Math.Clamp(maxEntries ?? 200, 1, 1000),
            before,
            cancellationToken);

        return Results.Ok(entries.Select(entry => entry.ToResponse()).ToArray());
    }

    private static IResult StreamLogEntries(
        string logId,
        int? initialEntries,
        ILogStore logs,
        CancellationToken cancellationToken)
    {
        var log = logs.GetLog(logId);
        if (log is null)
        {
            return Results.NotFound();
        }

        if (!log.SupportsStreaming)
        {
            return Problem(
                StatusCodes.Status405MethodNotAllowed,
                "Log streaming is unavailable",
                "The selected log source does not support streaming.");
        }

        return Results.Stream(
            async stream =>
            {
                await foreach (var entry in logs.StreamLogAsync(
                    logId,
                    Math.Clamp(initialEntries ?? 50, 0, 1000),
                    cancellationToken))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        entry.ToResponse(),
                        cancellationToken: cancellationToken);
                    await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
            },
            "application/x-ndjson");
    }

    private static ResourceResponse CreateResourceResponse(
        IResourceManagerStore resourceManager,
        CloudResource resource) =>
        resource.ToResponse(
            resourceManager.GetGroupForResource(resource.Id),
            resourceManager.IsRegistered(resource.Id));

    private static async Task StartResourceDependenciesAsync(
        CloudResource resource,
        IResourceManagerStore resourceManager,
        IResourceRegistrationStore registrations,
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
                resourceManager,
                registrations,
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

            var provider = GetProcedureProvider(resourceManager, registrations, dependency);
            if (provider is null)
            {
                throw new InvalidOperationException(
                    $"Dependency resource '{dependency.Name}' does not support actions.");
            }

            var registration = GetRegistrationForResourceOrAncestor(
                resourceManager,
                registrations,
                dependency);
            await provider.ExecuteActionAsync(
                new ResourceProcedureContext(dependency, registration, group?.Id, registrations),
                runAction,
                cancellationToken);

            completed.Add(dependency.Id);
        }

        visiting.Remove(resource.Id);
    }

    private static bool ShouldStartDependencies(ResourceAction action) =>
        action.Kind is ResourceActionKind.Run or ResourceActionKind.Restart;

    private static bool ShouldWarnDependents(ResourceAction action) =>
        action.Kind is ResourceActionKind.Stop or ResourceActionKind.Restart;

    private static IReadOnlyList<CloudResource> GetActiveDependents(
        CloudResource resource,
        IResourceManagerStore resourceManager) =>
        resourceManager.GetResources()
            .Where(candidate => candidate.DependsOn.Contains(resource.Id, StringComparer.OrdinalIgnoreCase))
            .Where(candidate => IsActiveDependencyState(candidate.State))
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsActiveDependencyState(ResourceState state) =>
        state is not ResourceState.Stopped and not ResourceState.Unknown;

    private static IResourceProcedureProvider? GetProcedureProvider(
        IResourceManagerStore resourceManager,
        IResourceRegistrationStore registrations,
        CloudResource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(
            resourceManager,
            registrations,
            resource);
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

    private static ResourceRegistration? GetRegistrationForResourceOrAncestor(
        IResourceManagerStore resourceManager,
        IResourceRegistrationStore registrations,
        CloudResource resource)
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

    private static IResult ToProblem(Exception exception) =>
        exception is UnauthorizedAccessException
            ? Results.Forbid()
            : Problem(
                StatusCodes.Status400BadRequest,
                "Control plane request failed",
                exception.Message);

    private static IResult Problem(int statusCode, string title, string detail) =>
        Results.Problem(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        });
}
