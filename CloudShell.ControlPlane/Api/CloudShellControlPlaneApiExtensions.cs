using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
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
            .WithName("CloudShellControlPlane_ListResources")
            .Produces<ResourceResponse[]>(StatusCodes.Status200OK);

        api.MapGet("/resources/available", ListAvailableResources)
            .WithName("CloudShellControlPlane_ListAvailableResources")
            .Produces<ResourceResponse[]>(StatusCodes.Status200OK);

        api.MapPost("/resources", CreateResource)
            .WithName("CloudShellControlPlane_CreateResource")
            .Accepts<CreateResourceRequest>("application/json")
            .Produces<ResourceResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        api.MapPost("/resources/capabilities", GetResourceOperationCapabilities)
            .WithName("CloudShellControlPlane_GetResourceOperationCapabilities");

        api.MapGet("/resources/{resourceId}", GetResource)
            .WithName("CloudShellControlPlane_GetResource")
            .Produces<ResourceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        api.MapGet("/resources/{resourceId}/children", ListResourceChildren)
            .WithName("CloudShellControlPlane_ListResourceChildren")
            .Produces<ResourceResponse[]>(StatusCodes.Status200OK);

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

        api.MapGet("/environment-settings", ListUserSettings)
            .WithName("CloudShellControlPlane_ListEnvironmentSettings");

        api.MapGet("/environment-settings/{key}", GetUserSetting)
            .WithName("CloudShellControlPlane_GetEnvironmentSetting");

        api.MapPut("/environment-settings/{key}", SetUserSetting)
            .WithName("CloudShellControlPlane_SetEnvironmentSetting");

        api.MapDelete("/environment-settings/{key}", RemoveUserSetting)
            .WithName("CloudShellControlPlane_RemoveEnvironmentSetting");

        return api;
    }

    private static async Task<IResult> ListResources(
        string? resourceGroupId,
        string? parentResourceId,
        string? resourceType,
        bool? isRegistered,
        ResourceClass? resourceClass,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        var resources = await resourceManager.ListResourcesAsync(
            new ResourceQuery(resourceGroupId, parentResourceId, resourceType, isRegistered, resourceClass),
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
            var error = ControlPlaneError.ResourceNotRegistered(resourceId);
            return Problem(
                StatusCodes.Status404NotFound,
                "Resource not found",
                error.Message,
                error.Code);
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
            var error = ControlPlaneError.ResourceNotRegistered(resourceId);
            return Problem(
                StatusCodes.Status404NotFound,
                "Resource not found",
                error.Message,
                error.Code);
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
        try
        {
            var capabilities = await resourceManager.GetResourceOperationCapabilitiesAsync(
                NormalizeRequiredIds(request.ResourceIds, nameof(request.ResourceIds)),
                cancellationToken);

            return Results.Ok(capabilities.Values.Select(capability => capability.ToResponse()).ToArray());
        }
        catch (Exception exception) when (exception is ControlPlaneException or ArgumentException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> CreateResource(
        CreateResourceRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            var providerId = RequireValue(request.ProviderId, nameof(request.ProviderId));
            var resourceType = RequireValue(request.ResourceType, nameof(request.ResourceType));
            var resourceId = RequireValue(request.ResourceId, nameof(request.ResourceId));
            await resourceManager.CreateResourceAsync(
                new CreateResourceCommand(
                    providerId,
                    resourceType,
                    resourceId,
                    RequireValue(request.Name, nameof(request.Name)),
                    RequireConfiguration(request.Configuration),
                    NormalizeOptional(request.ResourceGroupId),
                    request.ResourceClass,
                    request.Attributes,
                    request.StartAfterCreate),
                cancellationToken);

            var resource = await resourceManager.GetResourceAsync(resourceId, cancellationToken);
            return resource is null
                ? Results.NoContent()
                : Results.Created(
                    $"{CloudShellControlPlaneApiDefaults.RoutePrefix}/resources/{Uri.EscapeDataString(resource.Id)}",
                    await CreateResourceResponse(resourceManager, resource, cancellationToken));
        }
        catch (Exception exception) when (exception is ControlPlaneException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> DeleteResource(
        string resourceId,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await resourceManager.DeleteResourceAsync(resourceId, cancellationToken);
            return Results.Ok(ToResponse(result));
        }
        catch (ControlPlaneException exception)
            when (exception.Error.Code == ControlPlaneErrorCodes.ResourceNotRegistered)
        {
            return Problem(
                StatusCodes.Status404NotFound,
                "Resource not found",
                exception.Error.Message,
                exception.Error.Code);
        }
        catch (Exception exception) when (exception is ControlPlaneException or InvalidOperationException or UnauthorizedAccessException)
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
            var error = ControlPlaneError.ResourceNotRegistered(resourceId);
            return Problem(
                StatusCodes.Status404NotFound,
                "Resource not found",
                error.Message,
                error.Code);
        }

        var action = resource.ResourceActions.FirstOrDefault(item =>
            string.Equals(item.Id, actionId, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            var error = ControlPlaneError.ResourceActionNotFound(resourceId, actionId);
            return Problem(
                StatusCodes.Status404NotFound,
                "Resource action not found",
                error.Message,
                error.Code);
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

            return Results.Ok(ToResponse(result));
        }
        catch (ControlPlaneException exception)
            when (ShouldWarnDependents(action) &&
                exception.Error.Code == ControlPlaneErrorCodes.DependentResourcesRunning)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "Dependent resources are running",
                $"{exception.Message} Do you want to stop the resource?",
                exception.Error.Code);
        }
        catch (InvalidOperationException exception) when (ShouldWarnDependents(action) && IsDependentWarning(exception))
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "Dependent resources are running",
                $"{exception.Message} Do you want to stop the resource?",
                ControlPlaneErrorCodes.DependentResourcesRunning);
        }
        catch (Exception exception) when (exception is ControlPlaneException or InvalidOperationException or UnauthorizedAccessException)
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
                new CreateResourceGroupCommand(
                    RequireValue(request.Name, nameof(request.Name)),
                    request.Description?.Trim() ?? string.Empty),
                cancellationToken);

            return Results.Created(
                $"{CloudShellControlPlaneApiDefaults.RoutePrefix}/resource-groups/{Uri.EscapeDataString(group.Id)}",
                group.ToResponse());
        }
        catch (Exception exception) when (exception is ControlPlaneException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
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
        catch (Exception exception) when (exception is ControlPlaneException or InvalidOperationException or UnauthorizedAccessException)
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
        catch (Exception exception) when (exception is ControlPlaneException or InvalidOperationException or UnauthorizedAccessException)
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
            var resourceId = RequireValue(request.ResourceId, nameof(request.ResourceId));
            await resourceManager.RegisterResourceAsync(
                new RegisterResourceCommand(
                    RequireValue(request.ProviderId, nameof(request.ProviderId)),
                    resourceId,
                    NormalizeOptional(request.ResourceGroupId),
                    NormalizeOptionalIds(request.DependsOn, nameof(request.DependsOn))),
                cancellationToken);

            var registration = await resourceManager.GetResourceRegistrationAsync(
                resourceId,
                cancellationToken);
            return registration is null
                ? Results.NoContent()
                : Results.Created(
                    $"{CloudShellControlPlaneApiDefaults.RoutePrefix}/registrations/{Uri.EscapeDataString(registration.ResourceId)}",
                    registration.ToResponse());
        }
        catch (Exception exception) when (exception is ControlPlaneException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
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
                new AssignResourceGroupCommand(
                    RequireValue(resourceId, nameof(resourceId)),
                    NormalizeOptional(request.ResourceGroupId),
                    NormalizeOptionalIds(request.DependsOn, nameof(request.DependsOn))),
                cancellationToken);

            var registration = await resourceManager.GetResourceRegistrationAsync(resourceId, cancellationToken);
            return registration is null
                ? Results.NoContent()
                : Results.Ok(registration.ToResponse());
        }
        catch (Exception exception) when (exception is ControlPlaneException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
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
                new SetResourceDependenciesCommand(
                    RequireValue(resourceId, nameof(resourceId)),
                    NormalizeRequiredIds(request.DependsOn, nameof(request.DependsOn))),
                cancellationToken);

            var registration = await resourceManager.GetResourceRegistrationAsync(resourceId, cancellationToken);
            return registration is null
                ? Results.NoContent()
                : Results.Ok(registration.ToResponse());
        }
        catch (Exception exception) when (exception is ControlPlaneException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
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

    private static async Task<IResult> ListUserSettings(
        ICloudShellControlPlaneUserSettingsProvider settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok((await settings.GetSettingsAsync(cancellationToken))
                .Values
                .Select(setting => setting.ToResponse())
                .ToArray());
        }
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> GetUserSetting(
        string key,
        ICloudShellControlPlaneUserSettingsProvider settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var setting = await settings.GetSettingAsync(key, cancellationToken);
            return setting is null
                ? Results.NotFound()
                : Results.Ok(setting.ToResponse());
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> SetUserSetting(
        string key,
        SetCloudShellUserSettingRequest request,
        ICloudShellControlPlaneUserSettingsProvider settings,
        CancellationToken cancellationToken)
    {
        try
        {
            await settings.SetSettingAsync(
                key,
                request.Value ?? string.Empty,
                cancellationToken);
            var setting = await settings.GetSettingAsync(key, cancellationToken);
            return Results.Ok(setting!.ToResponse());
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> RemoveUserSetting(
        string key,
        ICloudShellControlPlaneUserSettingsProvider settings,
        CancellationToken cancellationToken)
    {
        try
        {
            await settings.RemoveSettingAsync(key, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IReadOnlyList<ResourceResponse>> CreateResourceResponses(
        IResourceManager resourceManager,
        IReadOnlyList<Resource> resources,
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
        Resource resource,
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

    private static JsonElement RequireConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest("Configuration is required."));
        }

        return configuration;
    }

    private static IReadOnlyList<string> NormalizeRequiredIds(
        IReadOnlyList<string>? values,
        string name) =>
        NormalizeOptionalIds(values, name) ??
        throw new ControlPlaneException(ControlPlaneError.InvalidRequest($"{name} is required."));

    private static IReadOnlyList<string>? NormalizeOptionalIds(
        IReadOnlyList<string>? values,
        string name)
    {
        if (values is null)
        {
            return null;
        }

        var normalized = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ControlPlaneException(ControlPlaneError.InvalidRequest($"{name} cannot contain empty values."));
            }

            normalized.Add(value.Trim());
        }

        return normalized
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ResourceProcedureResponse ToResponse(ResourceProcedureResult result) =>
        new(
            result.Message,
            result.RestartRequired,
            result.RestartResourceId,
            result.RestartMessage);

    private static IResult ToProblem(Exception exception)
    {
        if (exception is ControlPlaneAccessDeniedException accessDeniedException)
        {
            return Problem(
                StatusCodes.Status403Forbidden,
                "Control plane request forbidden",
                accessDeniedException.Error.Message,
                accessDeniedException.Error.Code);
        }

        if (exception is UnauthorizedAccessException)
        {
            return Results.Forbid();
        }

        var error = ToControlPlaneError(exception);
        return Problem(
            StatusCodes.Status400BadRequest,
            "Control plane request failed",
            error.Message,
            error.Code);
    }

    private static ControlPlaneError ToControlPlaneError(Exception exception) =>
        exception switch
        {
            ControlPlaneException controlPlaneException => controlPlaneException.Error,
            ArgumentException argumentException => ControlPlaneError.InvalidRequest(argumentException.Message),
            _ => new(
                ControlPlaneErrorCodes.OperationFailed,
                "The requested control-plane operation could not be completed.")
        };

    private static IResult Problem(
        int statusCode,
        string title,
        string detail,
        string? code = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        };
        if (!string.IsNullOrWhiteSpace(code))
        {
            problem.Extensions["code"] = code;
        }

        return Results.Json(
            problem,
            statusCode: statusCode,
            contentType: "application/problem+json");
    }

    private sealed record TraceIngestRequest(IReadOnlyList<TraceSpan> Spans);
}
