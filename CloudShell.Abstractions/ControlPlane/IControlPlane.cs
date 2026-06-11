using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.Abstractions.ControlPlane;

public interface IControlPlane :
    IResourceManager,
    IResourceTemplateManager,
    ILogManager,
    ITraceManager;

public interface IResourceManager
{
    event EventHandler<ResourceChangeNotification>? ResourcesChanged;

    Task<IReadOnlyList<ResourceGroup>> ListResourceGroupsAsync(
        CancellationToken cancellationToken = default);

    Task<ResourceGroup?> GetResourceGroupForResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<ResourceGroup> CreateResourceGroupAsync(
        CreateResourceGroupCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Resource>> ListAvailableResourcesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Resource>> ListResourcesAsync(
        ResourceQuery? query = null,
        CancellationToken cancellationToken = default);

    Task<Resource?> GetResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Resource>> ListResourceChildrenAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourceRegistration>> ListResourceRegistrationsAsync(
        CancellationToken cancellationToken = default);

    Task<ResourceRegistration?> GetResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task CreateResourceAsync(
        CreateResourceCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, ResourceOperationCapabilities>> GetResourceOperationCapabilitiesAsync(
        IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourcePermissionGrant>> ListResourcePermissionGrantsAsync(
        ResourcePermissionGrantQuery? query = null,
        CancellationToken cancellationToken = default);

    Task<ResourcePermissionEvaluation> EvaluateResourcePermissionGrantAsync(
        ResourceIdentityReference identity,
        string targetResourceId,
        string permission,
        CancellationToken cancellationToken = default);

    Task RegisterResourceAsync(
        RegisterResourceCommand command,
        CancellationToken cancellationToken = default);

    Task RemoveResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task AssignResourceGroupAsync(
        AssignResourceGroupCommand command,
        CancellationToken cancellationToken = default);

    Task SetResourceDependenciesAsync(
        SetResourceDependenciesCommand command,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> DeleteResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> ExecuteResourceActionAsync(
        ExecuteResourceActionCommand command,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> UpdateResourceImageAsync(
        UpdateResourceImageCommand command,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> UpdateResourceReplicasAsync(
        UpdateResourceReplicasCommand command,
        CancellationToken cancellationToken = default);
}

public interface IResourceTemplateManager
{
    Task<ResourceGroupTemplateExportResult> ExportResourceGroupTemplateAsync(
        string resourceGroupId,
        CancellationToken cancellationToken = default);

    Task<ResourceGroupTemplateImportResult> ImportResourceGroupTemplateAsync(
        ResourceGroupTemplate template,
        CancellationToken cancellationToken = default);
}

public interface ILogManager
{
    Task<IReadOnlyList<LogDescriptor>> ListLogsAsync(
        LogQuery? query = null,
        CancellationToken cancellationToken = default);

    Task<LogDescriptor?> GetLogAsync(
        string logId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        ReadLogOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        StreamLogOptions? options = null,
        CancellationToken cancellationToken = default);
}

public interface ITraceManager
{
    Task<IReadOnlyList<TraceSpan>> ListTraceSpansAsync(
        TraceQuery? query = null,
        CancellationToken cancellationToken = default);

    Task IngestTraceSpansAsync(
        IEnumerable<TraceSpan> spans,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceQuery(
    string? ResourceGroupId = null,
    string? ParentResourceId = null,
    string? ResourceType = null,
    bool? IsRegistered = null,
    ResourceClass? ResourceClass = null);

public sealed record ResourcePermissionGrantQuery(
    string? IdentityResourceId = null,
    string? IdentityName = null,
    string? TargetResourceId = null,
    string? Permission = null);

public sealed record ResourceChangeNotification(
    ResourceChangeKind Kind,
    string? ResourceId = null,
    string? ActionId = null,
    IReadOnlyList<string>? AffectedResourceIds = null)
{
    public IReadOnlyList<string> Resources =>
        AffectedResourceIds ?? [];
}

public enum ResourceChangeKind
{
    ResourceCreated,
    ResourceRegistered,
    ResourceRegistrationRemoved,
    ResourceGroupAssigned,
    ResourceDependenciesChanged,
    ResourceDeleted,
    ResourceActionExecuted,
    ResourceImageUpdated,
    ResourceReplicasUpdated
}

public sealed record LogQuery(
    string? ResourceId = null,
    string? ArtifactId = null,
    LogSourceKind? SourceKind = null);

public sealed record TraceQuery(
    string? ResourceId = null,
    string? TraceId = null,
    int MaxSpans = 200);

public sealed record CreateResourceGroupCommand(
    string Name,
    string Description);

public sealed record CreateResourceCommand(
    string ProviderId,
    string ResourceType,
    string ResourceId,
    string Name,
    JsonElement Configuration,
    string? ResourceGroupId = null,
    ResourceClass? ResourceClass = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    bool StartAfterCreate = false)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ResourceAttributes => Attributes ?? EmptyAttributes;
}

public sealed record RegisterResourceCommand(
    string ProviderId,
    string ResourceId,
    string? ResourceGroupId = null,
    IReadOnlyList<string>? DependsOn = null);

public sealed record AssignResourceGroupCommand(
    string ResourceId,
    string? ResourceGroupId,
    IReadOnlyList<string>? DependsOn = null);

public sealed record SetResourceDependenciesCommand(
    string ResourceId,
    IReadOnlyList<string> DependsOn);

public sealed record ExecuteResourceActionCommand(
    string ResourceId,
    string ActionId,
    bool StartDependencies = false,
    bool IgnoreDependentWarning = false,
    string? TriggeredBy = null);

public sealed record UpdateResourceImageCommand(
    string ResourceId,
    string Image,
    bool RestartIfRunning = true,
    string? TriggeredBy = null);

public sealed record UpdateResourceReplicasCommand(
    string ResourceId,
    int Replicas,
    bool RestartIfRunning = true,
    string? TriggeredBy = null);

public sealed record ResourceOperationCapabilities(
    string ResourceId,
    bool CanManage,
    bool CanDelete,
    IReadOnlySet<string> ExecutableActionIds,
    IReadOnlyList<ResourceActionCapability> ResourceActionCapabilities)
{
    public static ResourceOperationCapabilities None(string resourceId) =>
        new(
            resourceId,
            false,
            false,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            []);

    public ResourceOperationCapabilities(
        string resourceId,
        bool canManage,
        bool canDelete,
        IReadOnlySet<string> executableActionIds)
        : this(
            resourceId,
            canManage,
            canDelete,
            executableActionIds,
            executableActionIds
                .Select(actionId => new ResourceActionCapability(actionId, true))
                .ToArray())
    {
    }

    public ResourceActionCapability? GetActionCapability(string actionId) =>
        ResourceActionCapabilities.FirstOrDefault(capability =>
            string.Equals(capability.ActionId, actionId, StringComparison.OrdinalIgnoreCase));

    public bool CanExecuteAction(string actionId) =>
        GetActionCapability(actionId)?.CanExecute ??
        ExecutableActionIds.Contains(actionId, StringComparer.OrdinalIgnoreCase);

    public string? GetActionUnavailableReason(string actionId) =>
        GetActionCapability(actionId)?.Reason;

    public bool CanRun => CanExecuteAction(ResourceActionIds.Run);

    public bool CanStop => CanExecuteAction(ResourceActionIds.Stop);

    public bool CanPause => CanExecuteAction(ResourceActionIds.Pause);

    public bool CanRestart => CanExecuteAction(ResourceActionIds.Restart);
}

public sealed record ResourceActionCapability(
    string ActionId,
    bool CanExecute,
    string? Reason = null);

public sealed record ReadLogOptions(
    int MaxEntries = 200,
    DateTimeOffset? Before = null);

public sealed record StreamLogOptions(
    int InitialEntries = 50);
