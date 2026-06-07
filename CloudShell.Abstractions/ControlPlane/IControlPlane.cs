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
    Task<IReadOnlyList<ResourceGroup>> ListResourceGroupsAsync(
        CancellationToken cancellationToken = default);

    Task<ResourceGroup?> GetResourceGroupForResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<ResourceGroup> CreateResourceGroupAsync(
        CreateResourceGroupCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudResource>> ListAvailableResourcesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudResource>> ListResourcesAsync(
        ResourceQuery? query = null,
        CancellationToken cancellationToken = default);

    Task<CloudResource?> GetResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudResource>> ListResourceChildrenAsync(
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
    bool? IsRegistered = null);

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
    string? ResourceGroupId = null);

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
    bool IgnoreDependentWarning = false);

public sealed record ResourceOperationCapabilities(
    string ResourceId,
    bool CanManage,
    bool CanDelete,
    IReadOnlySet<string> ExecutableActionIds);

public sealed record ReadLogOptions(
    int MaxEntries = 200,
    DateTimeOffset? Before = null);

public sealed record StreamLogOptions(
    int InitialEntries = 50);
