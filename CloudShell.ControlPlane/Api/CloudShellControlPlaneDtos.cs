using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using System.Text.Json;

namespace CloudShell.ControlPlane.Api;

public sealed record ResourceResponse(
    string Id,
    string Name,
    string Kind,
    string TypeId,
    ResourceClass ResourceClass,
    string Provider,
    string Region,
    ResourceState State,
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
    IReadOnlyDictionary<string, ResourceActionResponse> ResourceActions);

public sealed record ResourceEndpointResponse(
    string Name,
    string Address,
    string Protocol,
    bool IsExternal);

public sealed record ResourceCapabilityResponse(
    string Id,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record ResourceActionResponse(
    string Id,
    string DisplayName,
    ResourceActionKind Kind,
    string? Description,
    ResourceActionDisplayStyle DisplayStyle,
    ResourceActionIcon Icon,
    bool RequiresConfirmation,
    string Method,
    string Href);

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

public sealed record LogEntryResponse(
    DateTimeOffset Timestamp,
    string Message,
    string? Level,
    string? Source);

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
            CreateResourceActionDictionary(resource));

    public static ResourceEndpointResponse ToResponse(this ResourceEndpoint endpoint) =>
        new(endpoint.Name, endpoint.Address, endpoint.Protocol, endpoint.IsExternal);

    public static ResourceCapabilityResponse ToResponse(this ResourceCapability capability) =>
        new(capability.Id, capability.Metadata);

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
            presentation.DisplayStyle,
            presentation.Icon,
            action.RequiresConfirmation,
            "POST",
            $"{CloudShellControlPlaneApiDefaults.RoutePrefix}/resources/{Uri.EscapeDataString(resourceId)}/actions/{Uri.EscapeDataString(action.Id)}");
    }

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

    public static LogEntryResponse ToResponse(this LogEntry entry) =>
        new(entry.Timestamp, entry.Message, entry.Level, entry.Source);

    public static CloudShellUserSettingResponse ToResponse(this CloudShellUserSetting setting) =>
        new(setting.Key, setting.Value, setting.UpdatedAt);
}
