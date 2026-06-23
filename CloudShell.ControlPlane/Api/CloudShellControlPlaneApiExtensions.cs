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

        api.MapGet("/resource-principals", QueryResourcePrincipals)
            .WithName("CloudShellControlPlane_QueryResourcePrincipals")
            .Produces<ResourcePrincipalResponse[]>(StatusCodes.Status200OK);

        api.MapGet("/resource-permission-grants", ListResourcePermissionGrants)
            .WithName("CloudShellControlPlane_ListResourcePermissionGrants")
            .Produces<ResourcePermissionGrantResponse[]>(StatusCodes.Status200OK);

        api.MapGet("/resource-permission-grants/status", ListResourcePermissionGrantStatuses)
            .WithName("CloudShellControlPlane_ListResourcePermissionGrantStatuses")
            .Produces<ResourcePermissionGrantStatusResponse[]>(StatusCodes.Status200OK);

        api.MapPost("/resource-permission-grants", GrantResourcePermission)
            .WithName("CloudShellControlPlane_GrantResourcePermission")
            .Accepts<GrantResourcePermissionRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        api.MapPost("/resource-permission-grants/revoke", RevokeResourcePermission)
            .WithName("CloudShellControlPlane_RevokeResourcePermission")
            .Accepts<RevokeResourcePermissionRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        api.MapPost("/resource-permission-grants/evaluate", EvaluateResourcePermissionGrant)
            .WithName("CloudShellControlPlane_EvaluateResourcePermissionGrant")
            .Accepts<ResourcePermissionEvaluationRequest>("application/json")
            .Produces<ResourcePermissionEvaluationResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        api.MapPost("/identity-providers/{providerId}/setup", SetupResourceIdentityProvider)
            .WithName("CloudShellControlPlane_SetupResourceIdentityProvider")
            .Produces<ResourceIdentityProviderSetupResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

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

        api.MapPost("/resources/{resourceId}/identity/provision", ProvisionResourceIdentity)
            .WithName("CloudShellControlPlane_ProvisionResourceIdentity")
            .Produces<ResourceIdentityProvisioningResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        api.MapGet("/resources/{resourceId}/identity/provisioning-status", GetResourceIdentityProvisioningStatus)
            .WithName("CloudShellControlPlane_GetResourceIdentityProvisioningStatus")
            .Produces<ResourceIdentityProvisioningStatusResultResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

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

        api.MapPut("/registrations/{resourceId}/identity", SetResourceIdentity)
            .WithName("CloudShellControlPlane_SetResourceIdentity")
            .Accepts<SetResourceIdentityRequest>("application/json")
            .Produces<ResourceRegistrationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        api.MapGet("/logs", ListLogs)
            .WithName("CloudShellControlPlane_ListLogs")
            .Produces<LogResponse[]>(StatusCodes.Status200OK);

        api.MapGet("/log-sources", ListLogSources)
            .WithName("CloudShellControlPlane_ListLogSources")
            .Produces<LogSourceResponse[]>(StatusCodes.Status200OK);

        api.MapGet("/log-sources/{logSourceId}", GetLogSource)
            .WithName("CloudShellControlPlane_GetLogSource");

        api.MapGet("/log-sources/{logSourceId}/entries", ReadLogSourceEntries)
            .WithName("CloudShellControlPlane_ReadLogSourceEntries");

        api.MapGet("/log-sources/{logSourceId}/stream", StreamLogSourceEntries)
            .WithName("CloudShellControlPlane_StreamLogSourceEntries");

        api.MapGet("/resource-events", ListResourceEvents)
            .WithName("CloudShellControlPlane_ListResourceEvents")
            .Produces<ResourceEventResponse[]>(StatusCodes.Status200OK);

        api.MapGet("/deployments", ListResourceDeployments)
            .WithName("CloudShellControlPlane_ListResourceDeployments")
            .Produces<ResourceDeploymentRecordResponse[]>(StatusCodes.Status200OK);

        api.MapGet("/replica-slot-states", ListReplicaSlotStates)
            .WithName("CloudShellControlPlane_ListReplicaSlotStates")
            .Produces<ResourceReplicaSlotStateResponse[]>(StatusCodes.Status200OK);

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

        api.MapGet("/metrics", ListMetricPoints)
            .WithName("CloudShellControlPlane_ListMetricPoints");

        api.MapPost("/metrics/ingest", IngestMetricPoints)
            .WithName("CloudShellControlPlane_IngestMetricPoints")
            .AllowAnonymous()
            .ExcludeFromDescription();

        api.MapGet("/resource-health", ListResourceHealth)
            .WithName("CloudShellControlPlane_ListResourceHealth");

        api.MapPost("/resource-health/refresh", RefreshResourceHealth)
            .WithName("CloudShellControlPlane_RefreshResourceHealth");

        api.MapGet("/resources/{resourceId}/health", GetResourceHealth)
            .WithName("CloudShellControlPlane_GetResourceHealth");

        api.MapGet("/resources/{resourceId}/health/snapshots", ListResourceHealthSnapshots)
            .WithName("CloudShellControlPlane_ListResourceHealthSnapshots");

        api.MapPost("/resources/{resourceId}/health/refresh", RefreshResourceHealthForResource)
            .WithName("CloudShellControlPlane_RefreshResourceHealthForResource");

        api.MapGet("/resources/{resourceId}/recovery-policy", GetResourceRecoveryPolicy)
            .WithName("CloudShellControlPlane_GetResourceRecoveryPolicy");

        api.MapPut("/resources/{resourceId}/recovery-policy", SetResourceRecoveryPolicy)
            .WithName("CloudShellControlPlane_SetResourceRecoveryPolicy");

        api.MapDelete("/resources/{resourceId}/recovery-policy", ClearResourceRecoveryPolicy)
            .WithName("CloudShellControlPlane_ClearResourceRecoveryPolicy");

        api.MapGet("/resources/{resourceId}/recovery-status", GetResourceRecoveryStatus)
            .WithName("CloudShellControlPlane_GetResourceRecoveryStatus");

        api.MapPost("/resources/{resourceId}/recovery-status/refresh", RefreshResourceRecovery)
            .WithName("CloudShellControlPlane_RefreshResourceRecovery");

        api.MapGet("/resources/{resourceId}/monitoring/availability", HasResourceMonitoring)
            .WithName("CloudShellControlPlane_HasResourceMonitoring");

        api.MapGet("/resources/{resourceId}/monitoring", GetResourceMonitoring)
            .WithName("CloudShellControlPlane_GetResourceMonitoring");

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

    public static RouteGroupBuilder MapCloudShellContainerAppsApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints
            .MapGroup("/api/container-apps/v1")
            .WithTags("Container Apps")
            .WithGroupName(CloudShellControlPlaneApiDefaults.DocumentName);

        api.MapPost("/{containerAppId}/deployments", DeployContainerApp)
            .WithName("CloudShellContainerApps_Deploy")
            .Accepts<CreateContainerAppDeploymentRequest>("application/json")
            .Produces<ResourceProcedureResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        api.MapPut("/{containerAppId}/replicas", UpdateContainerAppReplicas)
            .WithName("CloudShellContainerApps_UpdateReplicas")
            .Accepts<UpdateResourceReplicasRequest>("application/json")
            .Produces<ResourceProcedureResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

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

    private static async Task<IResult> ListResourcePermissionGrants(
        ResourcePrincipalKind? principalKind,
        string? principalId,
        string? principalProviderId,
        string? targetResourceId,
        string? permission,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        var grants = await resourceManager.ListResourcePermissionGrantsAsync(
            new ResourcePermissionGrantQuery(
                CreatePrincipalFilter(principalKind, principalId, principalProviderId),
                targetResourceId,
                permission),
            cancellationToken);

        return Results.Ok(grants.Select(grant => grant.ToResponse()).ToArray());
    }

    private static async Task<IResult> ListResourcePermissionGrantStatuses(
        ResourcePrincipalKind? principalKind,
        string? principalId,
        string? principalProviderId,
        string? targetResourceId,
        string? permission,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        var statuses = await resourceManager.ListResourcePermissionGrantStatusesAsync(
            new ResourcePermissionGrantQuery(
                CreatePrincipalFilter(principalKind, principalId, principalProviderId),
                targetResourceId,
                permission),
            cancellationToken);

        return Results.Ok(statuses.Select(status => status.ToResponse()).ToArray());
    }

    private static ResourcePrincipalReference? CreatePrincipalFilter(
        ResourcePrincipalKind? kind,
        string? id,
        string? providerId)
    {
        if (kind is null && string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        if (kind is null || string.IsNullOrWhiteSpace(id))
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest(
                "principalKind and principalId are required when filtering grants by principal."));
        }

        return new ResourcePrincipalReference(kind.Value, id, ProviderId: providerId);
    }

    private static async Task<IResult> QueryResourcePrincipals(
        string? searchText,
        string? kinds,
        string? providerId,
        int? limit,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            var principals = await resourceManager.QueryResourcePrincipalsAsync(
                new ResourcePrincipalQuery(
                    searchText,
                    ParsePrincipalKinds(kinds),
                    providerId,
                    limit),
                cancellationToken);

            return Results.Ok(principals.Select(principal => principal.ToResponse()).ToArray());
        }
        catch (Exception exception) when (exception is ControlPlaneException or ArgumentException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> GrantResourcePermission(
        GrantResourcePermissionRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            await resourceManager.GrantResourcePermissionAsync(
                new GrantResourcePermissionCommand(
                    request.Principal.ToDomain(),
                    RequireValue(request.TargetResourceId, nameof(request.TargetResourceId)),
                    RequireValue(request.Permission, nameof(request.Permission))),
                cancellationToken);

            return Results.NoContent();
        }
        catch (Exception exception) when (exception is ControlPlaneException or ControlPlaneAccessDeniedException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> RevokeResourcePermission(
        RevokeResourcePermissionRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            await resourceManager.RevokeResourcePermissionAsync(
                new RevokeResourcePermissionCommand(
                    request.Principal.ToDomain(),
                    RequireValue(request.TargetResourceId, nameof(request.TargetResourceId)),
                    RequireValue(request.Permission, nameof(request.Permission))),
                cancellationToken);

            return Results.NoContent();
        }
        catch (Exception exception) when (exception is ControlPlaneException or ControlPlaneAccessDeniedException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> EvaluateResourcePermissionGrant(
        ResourcePermissionEvaluationRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request.Identity);
            var evaluation = await resourceManager.EvaluateResourcePermissionGrantAsync(
                request.Identity.ToResourceIdentityReference(),
                RequireValue(request.TargetResourceId, nameof(request.TargetResourceId)),
                RequireValue(request.Permission, nameof(request.Permission)),
                cancellationToken);

            return Results.Ok(evaluation.ToResponse());
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentNullException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ProvisionResourceIdentity(
        string resourceId,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await resourceManager.ProvisionResourceIdentityAsync(
                resourceId,
                cancellationToken);
            return Results.Ok(result.ToResponse());
        }
        catch (Exception exception) when (exception is ControlPlaneException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> GetResourceIdentityProvisioningStatus(
        string resourceId,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await resourceManager.GetResourceIdentityProvisioningStatusAsync(
                resourceId,
                cancellationToken);
            return Results.Ok(result.ToResponse());
        }
        catch (Exception exception) when (exception is ControlPlaneException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> SetupResourceIdentityProvider(
        string providerId,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await resourceManager.SetupResourceIdentityProviderAsync(
                providerId,
                cancellationToken);
            return Results.Ok(result.ToResponse());
        }
        catch (Exception exception) when (exception is ControlPlaneException or UnauthorizedAccessException)
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
        bool? startDependencies,
        bool? ignoreDependentWarning,
        DependencyStartFailureBehavior? dependencyStartFailureBehavior,
        string? triggeredBy,
        string? cause,
        string? actingIdentityResourceId,
        string? actingIdentityName,
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
                    startDependencies.GetValueOrDefault(),
                    ignoreDependentWarning.GetValueOrDefault(),
                    NormalizeOptional(triggeredBy),
                    CreateActingIdentity(actingIdentityResourceId, actingIdentityName),
                    dependencyStartFailureBehavior,
                    NormalizeOptional(cause)),
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

    private static async Task<IResult> DeployContainerApp(
        string containerAppId,
        [FromBody] CreateContainerAppDeploymentRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        if (await resourceManager.GetResourceAsync(containerAppId, cancellationToken) is null)
        {
            var error = ControlPlaneError.ResourceNotRegistered(containerAppId);
            return Problem(
                StatusCodes.Status404NotFound,
                "Container app not found",
                error.Message,
                error.Code);
        }

        try
        {
            var result = await resourceManager.UpdateResourceImageAsync(
                new UpdateResourceImageCommand(
                    containerAppId,
                    RequireValue(request.Image, nameof(request.Image)),
                    RestartIfRunning: false,
                    NormalizeOptional(request.TriggeredBy),
                    request.RequestedReplicas),
                cancellationToken);

            return Results.Ok(ToResponse(result));
        }
        catch (Exception exception) when (exception is ControlPlaneException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> UpdateContainerAppReplicas(
        string containerAppId,
        [FromBody] UpdateResourceReplicasRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        if (await resourceManager.GetResourceAsync(containerAppId, cancellationToken) is null)
        {
            var error = ControlPlaneError.ResourceNotRegistered(containerAppId);
            return Problem(
                StatusCodes.Status404NotFound,
                "Container app not found",
                error.Message,
                error.Code);
        }

        try
        {
            var result = await resourceManager.UpdateResourceReplicasAsync(
                new UpdateResourceReplicasCommand(
                    containerAppId,
                    RequireReplicas(request.Replicas),
                    request.RestartIfRunning,
                    NormalizeOptional(request.TriggeredBy)),
                cancellationToken);

            return Results.Ok(ToResponse(result));
        }
        catch (Exception exception) when (exception is ControlPlaneException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
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

    private static async Task<IResult> SetResourceIdentity(
        string resourceId,
        SetResourceIdentityRequest request,
        IResourceManager resourceManager,
        CancellationToken cancellationToken)
    {
        try
        {
            await resourceManager.SetResourceIdentityAsync(
                new SetResourceIdentityCommand(
                    RequireValue(resourceId, nameof(resourceId)),
                    request.Identity?.ToResourceIdentityBinding()),
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
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok((await logs.ListLogsAsync(
                new LogQuery(resourceId, artifactId, sourceKind),
                cancellationToken))
            .Select(log => log.ToResponse())
            .ToArray());
        }
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ListLogSources(
        string? resourceId,
        string? artifactId,
        LogSourceKind? sourceKind,
        ILogManager logs,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok((await logs.ListLogSourcesAsync(
                new LogQuery(resourceId, artifactId, sourceKind),
                cancellationToken))
            .Select(source => source.ToResponse())
            .ToArray());
        }
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ListResourceEvents(
        string? resourceId,
        string? eventType,
        string? triggeredBy,
        string? traceId,
        string? spanId,
        DateTimeOffset? since,
        DateTimeOffset? before,
        int? maxEvents,
        IResourceEventManager events,
        CancellationToken cancellationToken) =>
        Results.Ok((await events.ListResourceEventsAsync(
                new ResourceEventQuery(
                    ResourceId: NormalizeOptional(resourceId),
                    EventType: NormalizeOptional(eventType),
                    TriggeredBy: NormalizeOptional(triggeredBy),
                    Since: since,
                    Before: before,
                    MaxEvents: Math.Clamp(maxEvents ?? 200, 1, 1000),
                    TraceId: NormalizeOptional(traceId),
                    SpanId: NormalizeOptional(spanId)),
                cancellationToken))
            .Select(resourceEvent => resourceEvent.ToResponse())
            .ToArray());

    private static async Task<IResult> ListResourceDeployments(
        string? sourceResourceId,
        string? deploymentId,
        string? orchestratorId,
        int? maxRecords,
        IResourceDeploymentManager deployments,
        CancellationToken cancellationToken) =>
        Results.Ok((await deployments.ListResourceDeploymentsAsync(
                new ResourceDeploymentQuery(
                    SourceResourceId: NormalizeOptional(sourceResourceId),
                    DeploymentId: NormalizeOptional(deploymentId),
                    OrchestratorId: NormalizeOptional(orchestratorId),
                    MaxRecords: Math.Clamp(maxRecords ?? 200, 1, 1000)),
                cancellationToken))
            .Select(deployment => deployment.ToResponse())
            .ToArray());

    private static async Task<IResult> ListReplicaSlotStates(
        string? resourceId,
        int? slotOrdinal,
        ResourceReplicaSlotReconciliationStatus? status,
        int? maxRecords,
        IResourceReplicaSlotStateManager replicaSlots,
        CancellationToken cancellationToken) =>
        Results.Ok((await replicaSlots.ListReplicaSlotStatesAsync(
                new ResourceReplicaSlotStateQuery(
                    ResourceId: NormalizeOptional(resourceId),
                    SlotOrdinal: slotOrdinal,
                    Status: status,
                    MaxRecords: Math.Clamp(maxRecords ?? 200, 1, 1000)),
                cancellationToken))
            .Select(state => state.ToResponse())
            .ToArray());

    private static async Task<IResult> GetLog(
        string logId,
        ILogManager logs,
        CancellationToken cancellationToken)
    {
        try
        {
            var log = await logs.GetLogAsync(logId, cancellationToken);
            return log is null
                ? Results.NotFound()
                : Results.Ok(log.ToResponse());
        }
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> GetLogSource(
        string logSourceId,
        ILogManager logs,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = await logs.GetLogSourceAsync(logSourceId, cancellationToken);
            return source is null
                ? Results.NotFound()
                : Results.Ok(source.ToResponse());
        }
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ReadLogSourceEntries(
        string logSourceId,
        int? maxEntries,
        DateTimeOffset? before,
        ILogManager logs,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await logs.GetLogSourceAsync(logSourceId, cancellationToken) is null)
            {
                return Results.NotFound();
            }

            var entries = await logs.ReadLogSourceAsync(
                logSourceId,
                new ReadLogOptions(Math.Clamp(maxEntries ?? 200, 1, 1000), before),
                cancellationToken);

            return Results.Ok(entries.Select(entry => entry.ToResponse()).ToArray());
        }
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> StreamLogSourceEntries(
        string logSourceId,
        int? initialEntries,
        ILogManager logs,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = await logs.GetLogSourceAsync(logSourceId, cancellationToken);
            if (source is null)
            {
                return Results.NotFound();
            }

            if (!source.SupportsStreaming)
            {
                return Problem(
                    StatusCodes.Status405MethodNotAllowed,
                    "Log streaming is unavailable",
                    "The selected log source does not support streaming.");
            }

            return Results.Stream(
                async stream =>
                {
                    await foreach (var entry in logs.StreamLogSourceAsync(
                        logSourceId,
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
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ReadLogEntries(
        string logId,
        int? maxEntries,
        DateTimeOffset? before,
        ILogManager logs,
        CancellationToken cancellationToken)
    {
        try
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
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> StreamLogEntries(
        string logId,
        int? initialEntries,
        ILogManager logs,
        CancellationToken cancellationToken)
    {
        try
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
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> ListTraceSpans(
        string? resourceId,
        string? traceId,
        int? maxSpans,
        string? scopeResourceId,
        string? scopeName,
        string? scopeKind,
        string? deploymentRevision,
        ITraceManager traces,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await traces.ListTraceSpansAsync(
                new TraceQuery(
                    resourceId,
                    traceId,
                    Math.Clamp(maxSpans ?? 200, 1, 1000),
                    CreateScope(
                        scopeResourceId,
                        scopeName,
                        scopeKind,
                        deploymentRevision)),
                cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> IngestTraceSpans(
        TraceIngestRequest request,
        ITraceManager traces,
        CancellationToken cancellationToken)
    {
        await traces.IngestTraceSpansAsync(request.Spans, cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> ListMetricPoints(
        string? resourceId,
        string? metricName,
        int? maxPoints,
        string? scopeResourceId,
        string? scopeName,
        string? scopeKind,
        string? deploymentRevision,
        IMetricManager metrics,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await metrics.ListMetricPointsAsync(
                new MetricQuery(
                    resourceId,
                    metricName,
                    Math.Clamp(maxPoints ?? 200, 1, 1000),
                    CreateScope(
                        scopeResourceId,
                        scopeName,
                        scopeKind,
                        deploymentRevision)),
                cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            return ToProblem(exception);
        }
    }

    private static TelemetryScope? CreateScope(
        string? scopeResourceId,
        string? scopeName,
        string? scopeKind,
        string? deploymentRevision)
    {
        var scope = new TelemetryScope(
            NormalizeNullable(scopeResourceId),
            NormalizeNullable(scopeName),
            NormalizeNullable(scopeKind),
            NormalizeNullable(deploymentRevision));

        return scope.HasAnyFilter ? scope : null;
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<IResult> IngestMetricPoints(
        MetricIngestRequest request,
        IMetricManager metrics,
        CancellationToken cancellationToken)
    {
        await metrics.IngestMetricPointsAsync(request.Points, cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> ListResourceHealth(
        IResourceHealthManager health,
        CancellationToken cancellationToken) =>
        Results.Ok(await health.ListResourceHealthAsync(cancellationToken));

    private static async Task<IResult> RefreshResourceHealth(
        IResourceHealthManager health,
        CancellationToken cancellationToken) =>
        Results.Ok(await health.RefreshResourceHealthAsync(cancellationToken));

    private static async Task<IResult> GetResourceHealth(
        string resourceId,
        IResourceHealthManager health,
        CancellationToken cancellationToken)
    {
        var summary = await health.GetResourceHealthAsync(resourceId, cancellationToken);
        return summary is null ? Results.NotFound() : Results.Ok(summary);
    }

    private static async Task<IResult> ListResourceHealthSnapshots(
        string resourceId,
        int? maxSnapshots,
        IResourceHealthManager health,
        CancellationToken cancellationToken) =>
        Results.Ok(await health.ListResourceHealthSnapshotsAsync(
            resourceId,
            Math.Clamp(maxSnapshots ?? 100, 1, 10_000),
            cancellationToken));

    private static async Task<IResult> RefreshResourceHealthForResource(
        string resourceId,
        IResourceHealthManager health,
        CancellationToken cancellationToken)
    {
        var summary = await health.RefreshResourceHealthAsync(resourceId, cancellationToken);
        return summary is null ? Results.NotFound() : Results.Ok(summary);
    }

    private static async Task<IResult> GetResourceRecoveryPolicy(
        string resourceId,
        IResourceRecoveryManager recovery,
        CancellationToken cancellationToken)
    {
        var policy = await recovery.GetResourceRecoveryPolicyAsync(resourceId, cancellationToken);
        return policy is null ? Results.NotFound() : Results.Ok(policy);
    }

    private static async Task<IResult> SetResourceRecoveryPolicy(
        string resourceId,
        ResourceRecoveryPolicy policy,
        IResourceRecoveryManager recovery,
        CancellationToken cancellationToken) =>
        Results.Ok(await recovery.SetResourceRecoveryPolicyAsync(resourceId, policy, cancellationToken));

    private static async Task<IResult> ClearResourceRecoveryPolicy(
        string resourceId,
        IResourceRecoveryManager recovery,
        CancellationToken cancellationToken)
    {
        await recovery.ClearResourceRecoveryPolicyAsync(resourceId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetResourceRecoveryStatus(
        string resourceId,
        IResourceRecoveryManager recovery,
        CancellationToken cancellationToken)
    {
        var status = await recovery.GetResourceRecoveryStatusAsync(resourceId, cancellationToken);
        return status is null ? Results.NotFound() : Results.Ok(status);
    }

    private static async Task<IResult> RefreshResourceRecovery(
        string resourceId,
        IResourceRecoveryManager recovery,
        CancellationToken cancellationToken)
    {
        var status = await recovery.RefreshResourceRecoveryAsync(resourceId, cancellationToken);
        return status is null ? Results.NotFound() : Results.Ok(status);
    }

    private static async Task<IResult> HasResourceMonitoring(
        string resourceId,
        IResourceMonitoringManager monitoring,
        CancellationToken cancellationToken) =>
        Results.Ok(await monitoring.HasResourceMonitoringAsync(resourceId, cancellationToken));

    private static async Task<IResult> GetResourceMonitoring(
        string resourceId,
        IResourceMonitoringManager monitoring,
        CancellationToken cancellationToken)
    {
        var snapshot = await monitoring.GetResourceMonitoringAsync(resourceId, cancellationToken);
        return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
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

    private static ResourceIdentityReference? CreateActingIdentity(
        string? resourceId,
        string? name) =>
        string.IsNullOrWhiteSpace(resourceId) && string.IsNullOrWhiteSpace(name)
            ? null
            : ResourceIdentityReference.ForResource(
                RequireValue(resourceId, "actingIdentityResourceId"),
                name);

    private static JsonElement RequireConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest("Configuration is required."));
        }

        return configuration;
    }

    private static int RequireReplicas(int replicas)
    {
        if (replicas < 1)
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest("Replicas must be greater than or equal to 1."));
        }

        return replicas;
    }

    private static IReadOnlyList<string> NormalizeRequiredIds(
        IReadOnlyList<string>? values,
        string name) =>
        NormalizeOptionalIds(values, name) ??
        throw new ControlPlaneException(ControlPlaneError.InvalidRequest($"{name} is required."));

    private static IReadOnlySet<ResourcePrincipalKind>? ParsePrincipalKinds(string? kinds)
    {
        if (string.IsNullOrWhiteSpace(kinds))
        {
            return null;
        }

        var parsed = new HashSet<ResourcePrincipalKind>();
        foreach (var kind in kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<ResourcePrincipalKind>(kind, ignoreCase: true, out var parsedKind))
            {
                throw new ControlPlaneException(ControlPlaneError.InvalidRequest(
                    $"Unknown resource principal kind '{kind}'."));
            }

            parsed.Add(parsedKind);
        }

        return parsed.Count == 0 ? null : parsed;
    }

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
            result.RestartMessage,
            result.Signals
                .Select(signal => new ResourceProcedureSignalResponse(
                    ResourceSignalSeverityParser.ToLevel(signal.Severity),
                    signal.Message))
                .ToArray());

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

        if (exception is ResourceSettingResolutionException settingResolutionException)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Control plane request failed",
                settingResolutionException.Message,
                ControlPlaneErrorCodes.ResourceActionUnavailable,
                new Dictionary<string, object?>
                {
                    ["settingName"] = settingResolutionException.SettingName,
                    ["referenceKind"] = settingResolutionException.ReferenceKind
                });
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
            ResourceSettingResolutionException settingResolutionException =>
                ControlPlaneError.ResourceActionUnavailable(settingResolutionException.Message),
            ArgumentException argumentException => ControlPlaneError.InvalidRequest(argumentException.Message),
            _ => new(
                ControlPlaneErrorCodes.OperationFailed,
                "The requested control-plane operation could not be completed.")
        };

    private static IResult Problem(
        int statusCode,
        string title,
        string detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
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

        foreach (var extension in extensions ?? Enumerable.Empty<KeyValuePair<string, object?>>())
        {
            problem.Extensions[extension.Key] = extension.Value;
        }

        return Results.Json(
            problem,
            statusCode: statusCode,
            contentType: "application/problem+json");
    }

    private sealed record TraceIngestRequest(IReadOnlyList<TraceSpan> Spans);

    private sealed record MetricIngestRequest(IReadOnlyList<MetricPoint> Points);
}
