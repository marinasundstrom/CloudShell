using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
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

        api.MapPost("/resources", CreateResource)
            .WithName("CloudShellControlPlane_CreateResource");

        api.MapPost("/resources/capabilities", GetResourceOperationCapabilities)
            .WithName("CloudShellControlPlane_GetResourceOperationCapabilities");

        api.MapGet("/resources/{resourceId}", GetResource)
            .WithName("CloudShellControlPlane_GetResource");

        api.MapGet("/resources/{resourceId}/children", ListResourceChildren)
            .WithName("CloudShellControlPlane_ListResourceChildren");

        api.MapGet("/resources/{resourceId}/resource-group", GetResourceGroupForResource)
            .WithName("CloudShellControlPlane_GetResourceGroupForResource");

        api.MapDelete("/resources/{resourceId}", DeleteResource)
            .WithName("CloudShellControlPlane_DeleteResource");

        api.MapPost("/resources/{resourceId}/actions/{actionId}", ExecuteResourceAction)
            .WithName("CloudShellControlPlane_ExecuteResourceAction");

        api.MapGet("/resource-groups", ListResourceGroups)
            .WithName("CloudShellControlPlane_ListResourceGroups");

        api.MapPost("/resource-groups", CreateResourceGroup)
            .WithName("CloudShellControlPlane_CreateResourceGroup");

        api.MapGet("/resource-groups/{resourceGroupId}/template", ExportResourceGroupTemplate)
            .WithName("CloudShellControlPlane_ExportResourceGroupTemplate");

        api.MapPost("/resource-group-templates/import", ImportResourceGroupTemplate)
            .WithName("CloudShellControlPlane_ImportResourceGroupTemplate");

        api.MapGet("/registrations", ListRegistrations)
            .WithName("CloudShellControlPlane_ListRegistrations");

        api.MapGet("/registrations/{resourceId}", GetRegistration)
            .WithName("CloudShellControlPlane_GetRegistration");

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

        api.MapGet("/logs/{logId}", GetLog)
            .WithName("CloudShellControlPlane_GetLog");

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

    private static async Task<IResult> ListResources(
        string? resourceGroupId,
        string? parentResourceId,
        string? resourceType,
        bool? isRegistered,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        var resources = await resourceManager.ListResourcesAsync(
            new ResourceQuery(resourceGroupId, parentResourceId, resourceType, isRegistered),
            cancellationToken);

        return Results.Ok(await CreateResourceResponses(resourceManager, resources, cancellationToken));
    }

    private static async Task<IResult> ListAvailableResources(
        IResourceManager resourceManager,
        ICloudShellAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!authorization.HasPermission(CloudShellPermissions.Resources.Create))
        {
            return Results.Forbid();
        }

        var resources = await resourceManager.ListAvailableResourcesAsync(cancellationToken);
        return Results.Ok(await CreateResourceResponses(resourceManager, resources, cancellationToken));
    }

    private static async Task<IResult> GetResource(
        string resourceId,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        var resource = await resourceManager.GetResourceAsync(resourceId, cancellationToken);
        return resource is null
            ? Results.NotFound()
            : Results.Ok(await CreateResourceResponse(resourceManager, resource, cancellationToken));
    }

    private static async Task<IResult> ListResourceChildren(
        string resourceId,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        if (await resourceManager.GetResourceAsync(resourceId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var resources = await resourceManager.ListResourceChildrenAsync(resourceId, cancellationToken);
        return Results.Ok(await CreateResourceResponses(resourceManager, resources, cancellationToken));
    }

    private static async Task<IResult> GetResourceGroupForResource(
        string resourceId,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        if (await resourceManager.GetResourceAsync(resourceId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var group = await resourceManager.GetResourceGroupForResourceAsync(resourceId, cancellationToken);
        return group is null
            ? Results.NoContent()
            : Results.Ok(group.ToResponse());
    }

    private static async Task<IResult> GetResourceOperationCapabilities(
        ResourceOperationCapabilitiesRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        var capabilities = await resourceManager.GetResourceOperationCapabilitiesAsync(
            request.ResourceIds,
            cancellationToken);

        return Results.Ok(capabilities.Values.Select(capability => capability.ToResponse()).ToArray());
    }

    private static async Task<IResult> CreateResource(
        CreateResourceRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            await resourceManager.CreateResourceAsync(
                new CreateResourceCommand(
                    request.ProviderId,
                    request.ResourceType,
                    request.ResourceId,
                    request.Name,
                    request.Configuration,
                    request.ResourceGroupId),
                cancellationToken);

            var resource = await resourceManager.GetResourceAsync(request.ResourceId, cancellationToken);
            return resource is null
                ? Results.NoContent()
                : Results.Created(
                    $"{CloudShellControlPlaneApiDefaults.RoutePrefix}/resources/{Uri.EscapeDataString(resource.Id)}",
                    await CreateResourceResponse(resourceManager, resource, cancellationToken));
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> DeleteResource(
        string resourceId,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        if (await resourceManager.GetResourceAsync(resourceId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        try
        {
            var result = await resourceManager.DeleteResourceAsync(resourceId, cancellationToken);
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
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        var resource = await resourceManager.GetResourceAsync(resourceId, cancellationToken);
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

        try
        {
            var result = await resourceManager.ExecuteResourceActionAsync(
                new ExecuteResourceActionCommand(
                    resourceId,
                    actionId,
                    startDependencies,
                    ignoreDependentWarning),
                cancellationToken);

            return Results.Ok(new ResourceProcedureResponse(result.Message));
        }
        catch (InvalidOperationException exception) when (ShouldWarnDependents(action) && IsDependentWarning(exception))
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "Dependent resources are running",
                $"{exception.Message} Do you want to stop the resource?");
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ListResourceGroups(
        IResourceManager resourceManager,
        CancellationToken cancellationToken) =>
        Results.Ok((await resourceManager.ListResourceGroupsAsync(cancellationToken))
            .Select(group => group.ToResponse())
            .ToArray());

    private static async Task<IResult> CreateResourceGroup(
        CreateResourceGroupRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            var group = await resourceManager.CreateResourceGroupAsync(
                new CreateResourceGroupCommand(request.Name, request.Description ?? string.Empty),
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

    private static async Task<IResult> ExportResourceGroupTemplate(
        string resourceGroupId,
        IResourceTemplateManager templates,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await templates.ExportResourceGroupTemplateAsync(resourceGroupId, cancellationToken));
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ImportResourceGroupTemplate(
        ResourceGroupTemplate template,
        IResourceTemplateManager templates,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await templates.ImportResourceGroupTemplateAsync(template, cancellationToken));
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ListRegistrations(
        IResourceManager resourceManager,
        CancellationToken cancellationToken) =>
        Results.Ok((await resourceManager.ListResourceRegistrationsAsync(cancellationToken))
            .Select(registration => registration.ToResponse())
            .ToArray());

    private static async Task<IResult> GetRegistration(
        string resourceId,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        var registration = await resourceManager.GetResourceRegistrationAsync(resourceId, cancellationToken);
        return registration is null
            ? Results.NotFound()
            : Results.Ok(registration.ToResponse());
    }

    private static async Task<IResult> RegisterResource(
        RegisterResourceRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            await resourceManager.RegisterResourceAsync(
                new RegisterResourceCommand(
                    request.ProviderId,
                    request.ResourceId,
                    request.ResourceGroupId,
                    request.DependsOn),
                cancellationToken);

            var registration = await resourceManager.GetResourceRegistrationAsync(
                request.ResourceId,
                cancellationToken);
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
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            await resourceManager.RemoveResourceRegistrationAsync(resourceId, cancellationToken);
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
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            await resourceManager.AssignResourceGroupAsync(
                new AssignResourceGroupCommand(resourceId, request.ResourceGroupId, request.DependsOn),
                cancellationToken);

            var registration = await resourceManager.GetResourceRegistrationAsync(resourceId, cancellationToken);
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
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            await resourceManager.SetResourceDependenciesAsync(
                new SetResourceDependenciesCommand(resourceId, request.DependsOn),
                cancellationToken);

            var registration = await resourceManager.GetResourceRegistrationAsync(resourceId, cancellationToken);
            return registration is null
                ? Results.NoContent()
                : Results.Ok(registration.ToResponse());
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ListLogs(
        string? resourceId,
        string? artifactId,
        LogSourceKind? sourceKind,
        ILogManager logs,
        CancellationToken cancellationToken) =>
        Results.Ok((await logs.ListLogsAsync(
                new LogQuery(resourceId, artifactId, sourceKind),
                cancellationToken))
            .Select(log => log.ToResponse())
            .ToArray());

    private static async Task<IResult> GetLog(
        string logId,
        ILogManager logs,
        CancellationToken cancellationToken)
    {
        var log = await logs.GetLogAsync(logId, cancellationToken);
        return log is null
            ? Results.NotFound()
            : Results.Ok(log.ToResponse());
    }

    private static async Task<IResult> ReadLogEntries(
        string logId,
        int? maxEntries,
        DateTimeOffset? before,
        ILogManager logs,
        CancellationToken cancellationToken)
    {
        if (await logs.GetLogAsync(logId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var entries = await logs.ReadLogAsync(
            logId,
            new ReadLogOptions(Math.Clamp(maxEntries ?? 200, 1, 1000), before),
            cancellationToken);

        return Results.Ok(entries.Select(entry => entry.ToResponse()).ToArray());
    }

    private static async Task<IResult> StreamLogEntries(
        string logId,
        int? initialEntries,
        ILogManager logs,
        CancellationToken cancellationToken)
    {
        var log = await logs.GetLogAsync(logId, cancellationToken);
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
                    new StreamLogOptions(Math.Clamp(initialEntries ?? 50, 0, 1000)),
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

    private static async Task<IResult> ListTraceSpans(
        string? resourceId,
        string? traceId,
        int? maxSpans,
        ITraceManager traces,
        CancellationToken cancellationToken) =>
        Results.Ok(await traces.ListTraceSpansAsync(
                new TraceQuery(resourceId, traceId, Math.Clamp(maxSpans ?? 200, 1, 1000)),
                cancellationToken));

    private static async Task<IResult> IngestTraceSpans(
        TraceIngestRequest request,
        ITraceManager traces,
        CancellationToken cancellationToken)
    {
        await traces.IngestTraceSpansAsync(request.Spans, cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IReadOnlyList<ResourceResponse>> CreateResourceResponses(
        IResourceManager resourceManager,
        IReadOnlyList<CloudResource> resources,
        CancellationToken cancellationToken)
    {
        var responses = new List<ResourceResponse>(resources.Count);
        foreach (var resource in resources)
        {
            responses.Add(await CreateResourceResponse(resourceManager, resource, cancellationToken));
        }

        return responses;
    }

    private static async Task<ResourceResponse> CreateResourceResponse(
        IResourceManager resourceManager,
        CloudResource resource,
        CancellationToken cancellationToken)
    {
        var group = await resourceManager.GetResourceGroupForResourceAsync(resource.Id, cancellationToken);
        var registration = await resourceManager.GetResourceRegistrationAsync(resource.Id, cancellationToken);
        return resource.ToResponse(group, registration is not null);
    }

    private static bool ShouldWarnDependents(ResourceAction action) =>
        action.Kind is ResourceActionKind.Stop or ResourceActionKind.Restart or ResourceActionKind.Pause;

    private static bool IsDependentWarning(InvalidOperationException exception) =>
        exception.Message.Contains("depend on this resource", StringComparison.OrdinalIgnoreCase);

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
