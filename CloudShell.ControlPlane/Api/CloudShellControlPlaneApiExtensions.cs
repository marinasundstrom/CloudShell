using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
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

        api.MapGet("/traces", ListTraceSpans)
            .WithName("CloudShellControlPlane_ListTraceSpans");

        api.MapPost("/traces/ingest", IngestTraceSpans)
            .WithName("CloudShellControlPlane_IngestTraceSpans")
            .AllowAnonymous()
            .ExcludeFromDescription();

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
        ResourceOrchestrationService orchestration,
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

        try
        {
            var result = await orchestration.DeleteAsync(resource, cancellationToken);
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
        ResourceOrchestrationService orchestration,
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

        try
        {
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

            var result = await orchestration.ExecuteActionAsync(
                resource,
                action,
                startDependencies,
                authorization,
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

    private static IResult ListTraceSpans(
        string? resourceId,
        string? traceId,
        int? maxSpans,
        ITraceStore traces) =>
        Results.Ok(traces.GetSpans(
            resourceId,
            traceId,
            Math.Clamp(maxSpans ?? 200, 1, 1000)));

    private static IResult IngestTraceSpans(
        TraceIngestRequest request,
        ITraceStore traces)
    {
        traces.AddSpans(request.Spans);
        return Results.Accepted();
    }

    private static ResourceResponse CreateResourceResponse(
        IResourceManagerStore resourceManager,
        CloudResource resource) =>
        resource.ToResponse(
            resourceManager.GetGroupForResource(resource.Id),
            resourceManager.IsRegistered(resource.Id));

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

    private sealed record TraceIngestRequest(IReadOnlyList<TraceSpan> Spans);
}
