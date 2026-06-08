using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CloudShell.ControlPlane.Client;

public sealed class RemoteControlPlane(HttpClient httpClient) : IControlPlane
{
    private const string RoutePrefix = "api/control-plane/v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ResourceGroup>> ListResourceGroupsAsync(
        CancellationToken cancellationToken = default) =>
        (await GetRequiredAsync<IReadOnlyList<ResourceGroupResponse>>(
            "resource-groups",
            cancellationToken))
        .Select(response => response.ToResourceGroup())
        .ToArray();

    public async Task<ResourceGroup?> GetResourceGroupForResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            BuildUri($"resources/{Escape(resourceId)}/resource-group"),
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadRequiredAsync<ResourceGroupResponse>(response, cancellationToken))
            .ToResourceGroup();
    }

    public async Task<ResourceGroup> CreateResourceGroupAsync(
        CreateResourceGroupCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("resource-groups"),
            new CreateResourceGroupRequest(command.Name, command.Description),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadRequiredAsync<ResourceGroupResponse>(response, cancellationToken))
            .ToResourceGroup();
    }

    public async Task<IReadOnlyList<Resource>> ListAvailableResourcesAsync(
        CancellationToken cancellationToken = default) =>
        (await GetRequiredAsync<IReadOnlyList<ResourceResponse>>(
            "resources/available",
            cancellationToken))
        .Select(response => response.ToResource())
        .ToArray();

    public async Task<IReadOnlyList<Resource>> ListResourcesAsync(
        ResourceQuery? query = null,
        CancellationToken cancellationToken = default) =>
        (await GetRequiredAsync<IReadOnlyList<ResourceResponse>>(
            "resources",
            cancellationToken,
            ("resourceGroupId", query?.ResourceGroupId),
            ("parentResourceId", query?.ParentResourceId),
            ("resourceType", query?.ResourceType),
            ("isRegistered", query?.IsRegistered?.ToString()),
            ("resourceClass", query?.ResourceClass?.ToString())))
        .Select(response => response.ToResource())
        .ToArray();

    public async Task<Resource?> GetResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var response = await GetOptionalAsync<ResourceResponse>(
            $"resources/{Escape(resourceId)}",
            cancellationToken);
        return response?.ToResource();
    }

    public async Task<IReadOnlyList<Resource>> ListResourceChildrenAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        (await GetRequiredAsync<IReadOnlyList<ResourceResponse>>(
            $"resources/{Escape(resourceId)}/children",
            cancellationToken))
        .Select(response => response.ToResource())
        .ToArray();

    public async Task<IReadOnlyList<ResourceRegistration>> ListResourceRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        (await GetRequiredAsync<IReadOnlyList<ResourceRegistrationResponse>>(
            "registrations",
            cancellationToken))
        .Select(response => response.ToResourceRegistration())
        .ToArray();

    public async Task<ResourceRegistration?> GetResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var response = await GetOptionalAsync<ResourceRegistrationResponse>(
            $"registrations/{Escape(resourceId)}",
            cancellationToken);
        return response?.ToResourceRegistration();
    }

    public async Task CreateResourceAsync(
        CreateResourceCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("resources"),
            new CreateResourceRequest(
                command.ProviderId,
                command.ResourceType,
                command.ResourceId,
                command.Name,
                command.Configuration,
                command.ResourceGroupId,
                command.ResourceClass,
                command.Attributes,
                command.StartAfterCreate),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, ResourceOperationCapabilities>> GetResourceOperationCapabilitiesAsync(
        IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("resources/capabilities"),
            new ResourceOperationCapabilitiesRequest(resourceIds),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return (await ReadRequiredAsync<IReadOnlyList<ResourceOperationCapabilitiesResponse>>(
                response,
                cancellationToken))
            .Select(item => item.ToCapabilities())
            .ToDictionary(
                item => item.ResourceId,
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task RegisterResourceAsync(
        RegisterResourceCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("registrations"),
            new RegisterResourceRequest(
                command.ProviderId,
                command.ResourceId,
                command.ResourceGroupId,
                command.DependsOn),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RemoveResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            BuildUri($"registrations/{Escape(resourceId)}"),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task AssignResourceGroupAsync(
        AssignResourceGroupCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            BuildUri($"registrations/{Escape(command.ResourceId)}/group"),
            new AssignResourceGroupRequest(command.ResourceGroupId, command.DependsOn),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SetResourceDependenciesAsync(
        SetResourceDependenciesCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            BuildUri($"registrations/{Escape(command.ResourceId)}/dependencies"),
            new SetResourceDependenciesRequest(command.DependsOn),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<ResourceProcedureResult> DeleteResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            BuildUri($"resources/{Escape(resourceId)}"),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadRequiredAsync<ResourceProcedureResponse>(response, cancellationToken))
            .ToProcedureResult();
    }

    public async Task<ResourceProcedureResult> ExecuteResourceActionAsync(
        ExecuteResourceActionCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync(
            BuildUri(
                $"resources/{Escape(command.ResourceId)}/actions/{Escape(command.ActionId)}",
                ("startDependencies", command.StartDependencies.ToString()),
                ("ignoreDependentWarning", command.IgnoreDependentWarning.ToString()),
                ("triggeredBy", command.TriggeredBy)),
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadRequiredAsync<ResourceProcedureResponse>(response, cancellationToken))
            .ToProcedureResult();
    }

    public async Task<ResourceProcedureResult> UpdateResourceImageAsync(
        UpdateResourceImageCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri($"resources/{Escape(command.ResourceId)}/image"),
            new UpdateResourceImageRequest(command.Image, command.RestartIfRunning, command.TriggeredBy),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadRequiredAsync<ResourceProcedureResponse>(response, cancellationToken))
            .ToProcedureResult();
    }

    public Task<ResourceGroupTemplateExportResult> ExportResourceGroupTemplateAsync(
        string resourceGroupId,
        CancellationToken cancellationToken = default) =>
        GetRequiredAsync<ResourceGroupTemplateExportResult>(
            $"resource-groups/{Escape(resourceGroupId)}/template",
            cancellationToken);

    public async Task<ResourceGroupTemplateImportResult> ImportResourceGroupTemplateAsync(
        ResourceGroupTemplate template,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("resource-group-templates/import"),
            template,
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadRequiredAsync<ResourceGroupTemplateImportResult>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<LogDescriptor>> ListLogsAsync(
        LogQuery? query = null,
        CancellationToken cancellationToken = default) =>
        (await GetRequiredAsync<IReadOnlyList<LogResponse>>(
            "logs",
            cancellationToken,
            ("resourceId", query?.ResourceId),
            ("artifactId", query?.ArtifactId),
            ("sourceKind", query?.SourceKind?.ToString())))
        .Select(response => response.ToLogDescriptor())
        .ToArray();

    public async Task<LogDescriptor?> GetLogAsync(
        string logId,
        CancellationToken cancellationToken = default)
    {
        var response = await GetOptionalAsync<LogResponse>(
            $"logs/{Escape(logId)}",
            cancellationToken);
        return response?.ToLogDescriptor();
    }

    public async Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        ReadLogOptions? options = null,
        CancellationToken cancellationToken = default) =>
        (await GetRequiredAsync<IReadOnlyList<LogEntryResponse>>(
            $"logs/{Escape(logId)}/entries",
            cancellationToken,
            ("maxEntries", (options?.MaxEntries ?? 200).ToString()),
            ("before", options?.Before?.ToString("O"))))
        .Select(response => response.ToLogEntry())
        .ToArray();

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        StreamLogOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            BuildUri(
                $"logs/{Escape(logId)}/stream",
                ("initialEntries", (options?.InitialEntries ?? 50).ToString())),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<LogEntryResponse>(line, SerializerOptions);
            if (entry is not null)
            {
                yield return entry.ToLogEntry();
            }
        }
    }

    public Task<IReadOnlyList<TraceSpan>> ListTraceSpansAsync(
        TraceQuery? query = null,
        CancellationToken cancellationToken = default) =>
        GetRequiredAsync<IReadOnlyList<TraceSpan>>(
            "traces",
            cancellationToken,
            ("resourceId", query?.ResourceId),
            ("traceId", query?.TraceId),
            ("maxSpans", (query?.MaxSpans ?? 200).ToString()));

    public async Task IngestTraceSpansAsync(
        IEnumerable<TraceSpan> spans,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("traces/ingest"),
            new TraceIngestRequest(spans.ToArray()),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(BuildUri(path), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private async Task<T> GetRequiredAsync<T>(
        string path,
        CancellationToken cancellationToken,
        params (string Name, string? Value)[] query)
    {
        var response = await httpClient.GetAsync(BuildUri(path, query), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        return value ?? throw new InvalidOperationException("The control plane returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var problem = await ReadProblemAsync(response, cancellationToken);
        var message = problem?.Detail ?? problem?.Title;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"The control plane returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        if (!string.IsNullOrWhiteSpace(problem?.Code))
        {
            var error = new ControlPlaneError(problem.Code, message);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                throw new ControlPlaneAccessDeniedException(error);
            }

            throw new ControlPlaneException(error);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized ||
            ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400))
        {
            throw new UnauthorizedAccessException(message);
        }

        throw new ControlPlaneException(new ControlPlaneError(ControlPlaneErrorCodes.OperationFailed, message));
    }

    private static async Task<ProblemResponse?> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemResponse>(
                SerializerOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildUri(
        string path,
        params (string Name, string? Value)[] query)
    {
        var uri = $"{RoutePrefix}/{path.TrimStart('/')}";
        var queryValues = query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Escape(item.Name)}={Escape(item.Value!)}")
            .ToArray();
        return queryValues.Length == 0
            ? uri
            : $"{uri}?{string.Join('&', queryValues)}";
    }

    private static string Escape(string value) =>
        Uri.EscapeDataString(value);
}

file sealed record ResourceResponse(
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
    IReadOnlyDictionary<string, string>? Attributes,
    IReadOnlyDictionary<string, ResourceActionResponse> ResourceActions);

file sealed record ResourceEndpointResponse(
    string Name,
    string Address,
    string Protocol,
    bool IsExternal);

file sealed record ResourceActionResponse(
    string Id,
    string DisplayName,
    ResourceActionKind Kind,
    string? Description,
    ResourceActionDisplayStyle DisplayStyle,
    ResourceActionIcon Icon,
    bool RequiresConfirmation,
    string? Method,
    string? Href);

file sealed record ResourceGroupResponse(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> ResourceIds);

file sealed record ResourceRegistrationResponse(
    string ResourceId,
    string ProviderId,
    string? ResourceGroupId,
    DateTimeOffset RegisteredAt,
    IReadOnlyList<string> DependsOn);

file sealed record CreateResourceGroupRequest(
    string Name,
    string? Description);

file sealed record CreateResourceRequest(
    string ProviderId,
    string ResourceType,
    string ResourceId,
    string Name,
    JsonElement Configuration,
    string? ResourceGroupId,
    ResourceClass? ResourceClass = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    bool StartAfterCreate = false);

file sealed record RegisterResourceRequest(
    string ProviderId,
    string ResourceId,
    string? ResourceGroupId,
    IReadOnlyList<string>? DependsOn);

file sealed record AssignResourceGroupRequest(
    string? ResourceGroupId,
    IReadOnlyList<string>? DependsOn = null);

file sealed record SetResourceDependenciesRequest(IReadOnlyList<string> DependsOn);

file sealed record UpdateResourceImageRequest(
    string Image,
    bool RestartIfRunning = true,
    string? TriggeredBy = null);

file sealed record ResourceOperationCapabilitiesRequest(IReadOnlyList<string> ResourceIds);

file sealed record ResourceOperationCapabilitiesResponse(
    string ResourceId,
    bool CanManage,
    bool CanDelete,
    IReadOnlySet<string> ExecutableActionIds,
    IReadOnlyList<ResourceActionCapabilityResponse>? ResourceActionCapabilities);

file sealed record ResourceActionCapabilityResponse(
    string ActionId,
    bool CanExecute,
    string? Reason);

file sealed record ResourceProcedureResponse(
    string Message,
    bool RestartRequired = false,
    string? RestartResourceId = null,
    string? RestartMessage = null);

file sealed record LogResponse(
    string Id,
    string Name,
    string Provider,
    string SourceName,
    LogSourceKind SourceKind,
    string? ResourceId,
    string? ArtifactId,
    bool SupportsStreaming);

file sealed record LogEntryResponse(
    DateTimeOffset Timestamp,
    string Message,
    string? Level,
    string? Source);

file sealed record TraceIngestRequest(IReadOnlyList<TraceSpan> Spans);

sealed record ProblemResponse(string? Title, string? Detail, string? Code);

file static class RemoteControlPlaneMapper
{
    public static Resource ToResource(this ResourceResponse response) =>
        new(
            response.Id,
            response.Name,
            response.Kind,
            response.Provider,
            response.Region,
            response.State,
            response.Endpoints.Select(endpoint => endpoint.ToResourceEndpoint()).ToArray(),
            response.Version,
            response.LastUpdated,
            response.DependsOn,
            response.DetailRoute,
            response.ParentResourceId,
            response.TypeId,
            response.GetResourceActionResponses()
                .Select(action => action.ToResourceAction())
                .ToArray(),
            ResourceClass: response.ResourceClass,
            Attributes: response.Attributes);

    public static ResourceEndpoint ToResourceEndpoint(this ResourceEndpointResponse response) =>
        new(response.Name, response.Address, response.Protocol, response.IsExternal);

    public static ResourceAction ToResourceAction(this ResourceActionResponse response) =>
        new(
            response.Id,
            response.DisplayName,
            response.Kind,
            response.Description,
            new ResourceActionPresentation(
                response.DisplayStyle,
                response.Icon,
                response.RequiresConfirmation));

    private static IReadOnlyCollection<ResourceActionResponse> GetResourceActionResponses(
        this ResourceResponse response) =>
        response.ResourceActions.Values.ToArray();

    public static ResourceGroup ToResourceGroup(this ResourceGroupResponse response) =>
        new(response.Id, response.Name, response.Description, response.ResourceIds);

    public static ResourceRegistration ToResourceRegistration(this ResourceRegistrationResponse response) =>
        new(
            response.ResourceId,
            response.ProviderId,
            response.ResourceGroupId,
            response.RegisteredAt,
            response.DependsOn);

    public static ResourceOperationCapabilities ToCapabilities(
        this ResourceOperationCapabilitiesResponse response) =>
        new(
            response.ResourceId,
            response.CanManage,
            response.CanDelete,
            response.ExecutableActionIds,
            response.ResourceActionCapabilities?.Select(capability => capability.ToCapability()).ToArray() ??
                response.ExecutableActionIds
                    .Select(actionId => new ResourceActionCapability(actionId, true))
                    .ToArray());

    public static ResourceActionCapability ToCapability(
        this ResourceActionCapabilityResponse response) =>
        new(
            response.ActionId,
            response.CanExecute,
            response.Reason);

    public static ResourceProcedureResult ToProcedureResult(this ResourceProcedureResponse response) =>
        new(
            response.Message,
            response.RestartRequired,
            response.RestartResourceId,
            response.RestartMessage);

    public static LogDescriptor ToLogDescriptor(this LogResponse response) =>
        new(
            response.Id,
            response.Name,
            response.Provider,
            response.SourceName,
            response.SourceKind,
            response.ResourceId,
            response.ArtifactId,
            response.SupportsStreaming);

    public static LogEntry ToLogEntry(this LogEntryResponse response) =>
        new(response.Timestamp, response.Message, response.Level, response.Source);
}
