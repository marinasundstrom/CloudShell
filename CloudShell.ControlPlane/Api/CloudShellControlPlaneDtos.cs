using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using System.Text.Json;

namespace CloudShell.ControlPlane.Api;

public sealed record ResourceResponse(
    string Id,
    string Name,
    string? DisplayName,
    string Kind,
    string TypeId,
    ResourceClass ResourceClass,
    string Provider,
    string Region,
    ResourceState? State,
    IReadOnlyList<ResourceEndpointResponse> Endpoints,
    string PrimaryEndpoint,
    string Version,
    DateTimeOffset LastUpdated,
    IReadOnlyList<string> DependsOn,
    string? DetailRoute,
    string? ParentResourceId,
    ResourceGroupResponse? ResourceGroup,
    bool IsRegistered,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<ResourceCapabilityResponse> Capabilities,
    IReadOnlyList<ResourceEndpointMappingResponse> EndpointMappings,
    IReadOnlyList<LoadBalancerRouteResponse> LoadBalancerRoutes,
    ResourceIdentityBindingResponse? Identity,
    IReadOnlyDictionary<string, ResourceActionResponse> ResourceActions,
    ResourceSource Source = ResourceSource.User,
    ResourceManagementMode ManagementMode = ResourceManagementMode.UserManaged,
    ResourceVisibility Visibility = ResourceVisibility.Normal,
    string? OwnerResourceId = null,
    ResourceCleanupBehavior CleanupBehavior = ResourceCleanupBehavior.None);

public sealed record ResourceEndpointResponse(
    string Name,
    string Address,
    string Protocol,
    bool IsExternal,
    ResourceExposureScope Exposure);

public sealed record ResourceCapabilityResponse(
    string Id,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record ResourceEndpointReferenceResponse(
    string ResourceId,
    string EndpointName);

public sealed record ResourceEndpointMappingResponse(
    string Id,
    string Name,
    ResourceEndpointReferenceResponse Source,
    ResourceEndpointReferenceResponse Target,
    string? NetworkResourceId,
    string? ProviderResourceId);

public sealed record LoadBalancerRouteResponse(
    string Id,
    string Name,
    LoadBalancerRouteKind Kind,
    string EntrypointName,
    LoadBalancerRouteMatchResponse Match,
    LoadBalancerRouteTargetResponse Target);

public sealed record LoadBalancerRouteMatchResponse(
    string? Host,
    string? PathPrefix,
    int? Port);

public sealed record LoadBalancerRouteTargetResponse(
    string ResourceId,
    string? EndpointName,
    int? Port);

public sealed record ResourceActionResponse(
    string Id,
    string DisplayName,
    ResourceActionKind Kind,
    string? Description,
    string RequiredPermission,
    ResourceActionDisplayStyle DisplayStyle,
    ResourceActionIcon Icon,
    bool RequiresConfirmation,
    string Method,
    string Href);

public sealed record ResourceIdentityBindingResponse(
    ResourceIdentityBindingKind Kind,
    string? Name,
    string? ProviderId,
    string? Subject,
    IReadOnlyList<string> Scopes,
    IReadOnlyDictionary<string, string> Claims);

public sealed record ResourceIdentityReferenceResponse(
    string ResourceId,
    string? Name);

public sealed record ResourcePermissionGrantResponse(
    ResourceIdentityReferenceResponse Identity,
    string TargetResourceId,
    string Permission);

public sealed record ResourcePermissionEvaluationRequest(
    ResourceIdentityReferenceResponse Identity,
    string TargetResourceId,
    string Permission);

public sealed record ResourcePermissionEvaluationResponse(
    ResourceIdentityReferenceResponse Identity,
    string TargetResourceId,
    string Permission,
    bool IsAllowed,
    ResourcePermissionGrantResponse? Grant);

public sealed record ResourceIdentityProvisioningDiagnosticResponse(
    ResourceIdentityProvisioningDiagnosticSeverity Severity,
    string Message,
    ResourceIdentityReferenceResponse? Identity,
    string? ProviderId);

public sealed record ResourceIdentityProvisioningResponse(
    string ProviderId,
    IReadOnlyList<ResourceIdentityProvisioningDiagnosticResponse> Diagnostics);

public sealed record ResourceIdentityProviderSetupResponse(
    string ProviderId,
    IReadOnlyList<ResourceIdentityProvisioningDiagnosticResponse> Diagnostics);

public sealed record ResourceIdentityProvisioningStatusResponse(
    ResourceIdentityReferenceResponse Identity,
    ResourceIdentityProvisioningState State,
    string? Detail,
    DateTimeOffset? ObservedAt);

public sealed record ResourceIdentityProvisioningStatusResultResponse(
    string ProviderId,
    IReadOnlyList<ResourceIdentityProvisioningStatusResponse> Statuses,
    IReadOnlyList<ResourceIdentityProvisioningDiagnosticResponse> Diagnostics);

public sealed record ResourceGroupResponse(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> ResourceIds);

public sealed record ResourceRegistrationResponse(
    string ResourceId,
    string ProviderId,
    string? ResourceGroupId,
    DateTimeOffset RegisteredAt,
    IReadOnlyList<string> DependsOn);

public sealed record CreateResourceGroupRequest(
    string Name,
    string? Description);

public sealed record CreateResourceRequest(
    string ProviderId,
    string ResourceType,
    string ResourceId,
    string Name,
    JsonElement Configuration,
    string? ResourceGroupId,
    ResourceClass? ResourceClass = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    bool StartAfterCreate = false);

public sealed record RegisterResourceRequest(
    string ProviderId,
    string ResourceId,
    string? ResourceGroupId,
    IReadOnlyList<string>? DependsOn);

public sealed record AssignResourceGroupRequest(
    string? ResourceGroupId,
    IReadOnlyList<string>? DependsOn = null);

public sealed record SetResourceDependenciesRequest(IReadOnlyList<string> DependsOn);

public sealed record UpdateResourceImageRequest(
    string Image,
    bool RestartIfRunning = true,
    string? TriggeredBy = null);

public sealed record UpdateResourceReplicasRequest(
    int Replicas,
    bool RestartIfRunning = true,
    string? TriggeredBy = null);

public sealed record ResourceOperationCapabilitiesRequest(IReadOnlyList<string> ResourceIds);

public sealed record ResourceOperationCapabilitiesResponse(
    string ResourceId,
    bool CanManage,
    bool CanDelete,
    IReadOnlySet<string> ExecutableActionIds,
    IReadOnlyList<ResourceActionCapabilityResponse> ResourceActionCapabilities);

public sealed record ResourceActionCapabilityResponse(
    string ActionId,
    bool CanExecute,
    string? Reason);

public sealed record ResourceProcedureResponse(
    string Message,
    bool RestartRequired = false,
    string? RestartResourceId = null,
    string? RestartMessage = null);

public sealed record LogResponse(
    string Id,
    string Name,
    string Provider,
    string SourceName,
    LogSourceKind SourceKind,
    string? ResourceId,
    string? ArtifactId,
    bool SupportsStreaming);

public sealed record ResourceEventResponse(
    string ResourceId,
    string EventType,
    string Message,
    DateTimeOffset Timestamp,
    string? TriggeredBy,
    string Level,
    string? TraceId,
    string? SpanId);

public sealed record LogEntryResponse(
    DateTimeOffset Timestamp,
    string Message,
    string? Severity,
    string? Source,
    string? EventId,
    string? Category,
    string? TraceId,
    string? SpanId,
    string? ExceptionSummary,
    IReadOnlyDictionary<string, string>? Attributes);

public sealed record CloudShellUserSettingResponse(
    string Key,
    string Value,
    DateTimeOffset UpdatedAt);

public sealed record SetCloudShellUserSettingRequest(string Value);

internal static class CloudShellControlPlaneDtoMapper
{
    public static ResourceResponse ToResponse(
        this Resource resource,
        ResourceGroup? group,
        bool isRegistered) =>
        new(
            resource.Id,
            resource.Name,
            resource.DisplayName,
            resource.Kind,
            resource.EffectiveTypeId,
            resource.ResourceClass,
            resource.Provider,
            resource.Region,
            resource.State,
            resource.Endpoints.Select(ToResponse).ToArray(),
            resource.PrimaryEndpoint,
            resource.Version,
            resource.LastUpdated,
            resource.DependsOn,
            resource.DetailRoute,
            resource.ParentResourceId,
            group?.ToResponse(),
            isRegistered,
            resource.ResourceAttributes,
            resource.ResourceCapabilities.Select(ToResponse).ToArray(),
            resource.ResourceEndpointMappings.Select(ToResponse).ToArray(),
            resource.ResourceLoadBalancerRoutes.Select(ToResponse).ToArray(),
            resource.IdentityBinding?.ToResponse(),
            CreateResourceActionDictionary(resource),
            resource.Source,
            resource.ManagementMode,
            resource.Visibility,
            resource.OwnerResourceId,
            resource.CleanupBehavior);

    public static ResourceEndpointResponse ToResponse(this ResourceEndpoint endpoint) =>
        new(endpoint.Name, endpoint.Address, endpoint.Protocol, endpoint.IsExternal, endpoint.Exposure);

    public static ResourceCapabilityResponse ToResponse(this ResourceCapability capability) =>
        new(capability.Id, capability.Metadata);

    public static ResourceEndpointReferenceResponse ToResponse(this ResourceEndpointReference reference) =>
        new(reference.ResourceId, reference.EndpointName);

    public static ResourceEndpointMappingResponse ToResponse(this ResourceEndpointMappingDefinition mapping) =>
        new(
            mapping.Id,
            mapping.Name,
            mapping.Source.ToResponse(),
            mapping.Target.ToResponse(),
            mapping.NetworkResourceId,
            mapping.ProviderResourceId);

    public static LoadBalancerRouteResponse ToResponse(this LoadBalancerRoute route) =>
        new(
            route.Id,
            route.Name,
            route.Kind,
            route.EntrypointName,
            route.Match.ToResponse(),
            route.Target.ToResponse());

    public static LoadBalancerRouteMatchResponse ToResponse(this LoadBalancerRouteMatch match) =>
        new(match.Host, match.PathPrefix, match.Port);

    public static LoadBalancerRouteTargetResponse ToResponse(this LoadBalancerRouteTarget target) =>
        new(target.ResourceId, target.EndpointName, target.Port);

    public static ResourceActionResponse ToResponse(
        this ResourceAction action,
        string resourceId)
    {
        var presentation = action.EffectivePresentation;
        return new(
            action.Id,
            action.DisplayName,
            action.Kind,
            action.Description,
            ResourceActionPermissions.GetRequiredPermission(action),
            presentation.DisplayStyle,
            presentation.Icon,
            action.RequiresConfirmation,
            "POST",
            $"{CloudShellControlPlaneApiDefaults.RoutePrefix}/resources/{Uri.EscapeDataString(resourceId)}/actions/{Uri.EscapeDataString(action.Id)}");
    }

    public static ResourceIdentityBindingResponse ToResponse(
        this ResourceIdentityBinding identity) =>
        new(
            identity.Kind,
            identity.Name,
            identity.ProviderId,
            identity.Subject,
            identity.IdentityScopes,
            identity.IdentityClaims);

    public static ResourceIdentityReferenceResponse ToResponse(
        this ResourceIdentityReference identity) =>
        new(identity.ResourceId, identity.Name);

    public static ResourcePermissionGrantResponse ToResponse(
        this ResourcePermissionGrant grant) =>
        new(
            grant.Identity.ToResponse(),
            grant.TargetResourceId,
            grant.Permission);

    public static ResourcePermissionEvaluationResponse ToResponse(
        this ResourcePermissionEvaluation evaluation) =>
        new(
            evaluation.Identity.ToResponse(),
            evaluation.TargetResourceId,
            evaluation.Permission,
            evaluation.IsAllowed,
            evaluation.Grant?.ToResponse());

    public static ResourceIdentityProvisioningResponse ToResponse(
        this ResourceIdentityProvisioningResult result) =>
        new(
            result.ProviderId,
            result.ProvisioningDiagnostics
                .Select(diagnostic => diagnostic.ToResponse())
                .ToArray());

    public static ResourceIdentityProviderSetupResponse ToResponse(
        this ResourceIdentityProviderSetupResult result) =>
        new(
            result.ProviderId,
            result.SetupDiagnostics
                .Select(diagnostic => diagnostic.ToResponse())
                .ToArray());

    public static ResourceIdentityProvisioningStatusResultResponse ToResponse(
        this ResourceIdentityProvisioningStatusResult result) =>
        new(
            result.ProviderId,
            result.Statuses
                .Select(status => status.ToResponse())
                .ToArray(),
            result.ProvisioningDiagnostics
                .Select(diagnostic => diagnostic.ToResponse())
                .ToArray());

    public static ResourceIdentityProvisioningStatusResponse ToResponse(
        this ResourceIdentityProvisioningStatus status) =>
        new(
            status.Identity.ToResponse(),
            status.State,
            status.Detail,
            status.ObservedAt);

    public static ResourceIdentityProvisioningDiagnosticResponse ToResponse(
        this ResourceIdentityProvisioningDiagnostic diagnostic) =>
        new(
            diagnostic.Severity,
            diagnostic.Message,
            diagnostic.Identity?.ToResponse(),
            diagnostic.ProviderId);

    public static ResourceIdentityReference ToResourceIdentityReference(
        this ResourceIdentityReferenceResponse response) =>
        new(response.ResourceId, response.Name);

    private static IReadOnlyDictionary<string, ResourceActionResponse> CreateResourceActionDictionary(
        Resource resource) =>
        resource.ResourceActions
            .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().ToResponse(resource.Id),
                StringComparer.OrdinalIgnoreCase);

    public static ResourceGroupResponse ToResponse(this ResourceGroup group) =>
        new(group.Id, group.Name, group.Description, group.ResourceIds);

    public static ResourceRegistrationResponse ToResponse(this ResourceRegistration registration) =>
        new(
            registration.ResourceId,
            registration.ProviderId,
            registration.ResourceGroupId,
            registration.RegisteredAt,
            registration.DependsOn);

    public static ResourceOperationCapabilitiesResponse ToResponse(
        this ResourceOperationCapabilities capabilities) =>
        new(
            capabilities.ResourceId,
            capabilities.CanManage,
            capabilities.CanDelete,
            capabilities.ExecutableActionIds,
            capabilities.ResourceActionCapabilities.Select(ToResponse).ToArray());

    public static ResourceActionCapabilityResponse ToResponse(
        this ResourceActionCapability capability) =>
        new(
            capability.ActionId,
            capability.CanExecute,
            capability.Reason);

    public static LogResponse ToResponse(this LogDescriptor log) =>
        new(
            log.Id,
            log.Name,
            log.Provider,
            log.SourceName,
            log.SourceKind,
            log.ResourceId,
            log.ArtifactId,
            log.SupportsStreaming);

    public static ResourceEventResponse ToResponse(this ResourceEvent resourceEvent) =>
        new(
            resourceEvent.ResourceId,
            resourceEvent.EventType,
            resourceEvent.Message,
            resourceEvent.Timestamp,
            resourceEvent.TriggeredBy,
            resourceEvent.Level,
            resourceEvent.TraceId,
            resourceEvent.SpanId);

    public static LogEntryResponse ToResponse(this LogEntry entry) =>
        new(
            entry.Timestamp,
            entry.Message,
            entry.Severity,
            entry.Source,
            entry.EventId,
            entry.Category,
            entry.TraceId,
            entry.SpanId,
            entry.ExceptionSummary,
            entry.Attributes);

    public static CloudShellUserSettingResponse ToResponse(this CloudShellUserSetting setting) =>
        new(setting.Key, setting.Value, setting.UpdatedAt);
}
