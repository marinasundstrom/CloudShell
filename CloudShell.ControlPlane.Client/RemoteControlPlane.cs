using CloudShell.Client.Authentication;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CloudShell.ControlPlane.Client;

public sealed class RemoteControlPlane : IControlPlane
{
    private const string RoutePrefix = "api/control-plane/v1";
    private const string ContainerAppsRoutePrefix = "api/container-apps/v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;

    public RemoteControlPlane(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
    }

    public RemoteControlPlane(
        Uri baseAddress,
        CloudShellResourceCredential credential,
        IEnumerable<string>? scopes = null)
        : this(CreateAuthenticatedHttpClient(baseAddress, credential, scopes))
    {
    }

    public event EventHandler<ResourceChangeNotification>? ResourcesChanged;

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
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceCreated,
            command.ResourceId,
            AffectedResourceIds: [command.ResourceId]));
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

    public async Task<IReadOnlyList<ResourcePermissionGrant>> ListResourcePermissionGrantsAsync(
        ResourcePermissionGrantQuery? query = null,
        CancellationToken cancellationToken = default) =>
        (await GetRequiredAsync<IReadOnlyList<ResourcePermissionGrantResponse>>(
            "resource-permission-grants",
            cancellationToken,
            ("identityResourceId", query?.IdentityResourceId),
            ("identityName", query?.IdentityName),
            ("targetResourceId", query?.TargetResourceId),
            ("permission", query?.Permission)))
        .Select(response => response.ToResourcePermissionGrant())
        .ToArray();

    public async Task<ResourcePermissionEvaluation> EvaluateResourcePermissionGrantAsync(
        ResourceIdentityReference identity,
        string targetResourceId,
        string permission,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("resource-permission-grants/evaluate"),
            new ResourcePermissionEvaluationRequest(
                identity.ToResponse(),
                targetResourceId,
                permission),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadRequiredAsync<ResourcePermissionEvaluationResponse>(response, cancellationToken))
            .ToResourcePermissionEvaluation();
    }

    public async Task GrantResourcePermissionAsync(
        GrantResourcePermissionCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("resource-permission-grants"),
            new GrantResourcePermissionRequest(
                command.IdentityResourceId,
                command.IdentityName,
                command.TargetResourceId,
                command.Permission),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourcePermissionGrantsChanged,
            command.IdentityResourceId,
            AffectedResourceIds: [command.IdentityResourceId, command.TargetResourceId]));
    }

    public async Task RevokeResourcePermissionAsync(
        RevokeResourcePermissionCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("resource-permission-grants/revoke"),
            new RevokeResourcePermissionRequest(
                command.IdentityResourceId,
                command.IdentityName,
                command.TargetResourceId,
                command.Permission),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourcePermissionGrantsChanged,
            command.IdentityResourceId,
            AffectedResourceIds: [command.IdentityResourceId, command.TargetResourceId]));
    }

    public async Task<ResourceIdentityProvisioningResult> ProvisionResourceIdentityAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync(
            BuildUri($"resources/{Escape(resourceId)}/identity/provision"),
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadRequiredAsync<ResourceIdentityProvisioningResponse>(response, cancellationToken))
            .ToResourceIdentityProvisioningResult();
    }

    public async Task<ResourceIdentityProvisioningStatusResult> GetResourceIdentityProvisioningStatusAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            BuildUri($"resources/{Escape(resourceId)}/identity/provisioning-status"),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadRequiredAsync<ResourceIdentityProvisioningStatusResultResponse>(response, cancellationToken))
            .ToResourceIdentityProvisioningStatusResult();
    }

    public async Task<ResourceIdentityProviderSetupResult> SetupResourceIdentityProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync(
            BuildUri($"identity-providers/{Escape(providerId)}/setup"),
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadRequiredAsync<ResourceIdentityProviderSetupResponse>(response, cancellationToken))
            .ToResourceIdentityProviderSetupResult();
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
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceRegistered,
            command.ResourceId,
            AffectedResourceIds: [command.ResourceId]));
    }

    public async Task RemoveResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            BuildUri($"registrations/{Escape(resourceId)}"),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceRegistrationRemoved,
            resourceId,
            AffectedResourceIds: [resourceId]));
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
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceGroupAssigned,
            command.ResourceId,
            AffectedResourceIds: [command.ResourceId]));
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
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceDependenciesChanged,
            command.ResourceId,
            AffectedResourceIds: [command.ResourceId]));
    }

    public async Task<ResourceProcedureResult> DeleteResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            BuildUri($"resources/{Escape(resourceId)}"),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = (await ReadRequiredAsync<ResourceProcedureResponse>(response, cancellationToken))
            .ToProcedureResult();
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceDeleted,
            resourceId,
            AffectedResourceIds: [resourceId]));
        return result;
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
                ("triggeredBy", command.TriggeredBy),
                ("actingIdentityResourceId", command.ActingIdentity?.ResourceId),
                ("actingIdentityName", command.ActingIdentity?.Name)),
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = (await ReadRequiredAsync<ResourceProcedureResponse>(response, cancellationToken))
            .ToProcedureResult();
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceActionExecuted,
            command.ResourceId,
            command.ActionId,
            [command.ResourceId]));
        return result;
    }

    public async Task<ResourceProcedureResult> UpdateResourceImageAsync(
        UpdateResourceImageCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri(ContainerAppsRoutePrefix, $"{Escape(command.ResourceId)}/revisions"),
            new UpdateResourceImageRequest(command.Image, command.RestartIfRunning, command.TriggeredBy),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = (await ReadRequiredAsync<ResourceProcedureResponse>(response, cancellationToken))
            .ToProcedureResult();
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceImageUpdated,
            command.ResourceId,
            AffectedResourceIds: [command.ResourceId]));
        return result;
    }

    public async Task<ResourceProcedureResult> UpdateResourceReplicasAsync(
        UpdateResourceReplicasCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            BuildUri(ContainerAppsRoutePrefix, $"{Escape(command.ResourceId)}/replicas"),
            new UpdateResourceReplicasRequest(command.Replicas, command.RestartIfRunning, command.TriggeredBy),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = (await ReadRequiredAsync<ResourceProcedureResponse>(response, cancellationToken))
            .ToProcedureResult();
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceReplicasUpdated,
            command.ResourceId,
            AffectedResourceIds: [command.ResourceId]));
        return result;
    }

    private void NotifyResourcesChanged(ResourceChangeNotification notification) =>
        ResourcesChanged?.Invoke(this, notification);

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

    public async Task<IReadOnlyList<ResourceEvent>> ListResourceEventsAsync(
        ResourceEventQuery? query = null,
        CancellationToken cancellationToken = default) =>
        (await GetRequiredAsync<IReadOnlyList<ResourceEventResponse>>(
            "resource-events",
            cancellationToken,
            ("resourceId", query?.ResourceId),
            ("eventType", query?.EventType),
            ("triggeredBy", query?.TriggeredBy),
            ("traceId", query?.TraceId),
            ("spanId", query?.SpanId),
            ("since", query?.Since?.ToString("O")),
            ("before", query?.Before?.ToString("O")),
            ("maxEvents", (query?.MaxEvents ?? 200).ToString())))
        .Select(response => response.ToResourceEvent())
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
            ("maxSpans", (query?.MaxSpans ?? 200).ToString()),
            ("scopeResourceId", query?.Scope?.ScopeResourceId),
            ("scopeName", query?.Scope?.ScopeName),
            ("scopeKind", query?.Scope?.ScopeKind),
            ("deploymentRevision", query?.Scope?.DeploymentRevision));

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

    public Task<IReadOnlyList<MetricPoint>> ListMetricPointsAsync(
        MetricQuery? query = null,
        CancellationToken cancellationToken = default) =>
        GetRequiredAsync<IReadOnlyList<MetricPoint>>(
            "metrics",
            cancellationToken,
            ("resourceId", query?.ResourceId),
            ("metricName", query?.MetricName),
            ("maxPoints", (query?.MaxPoints ?? 200).ToString()),
            ("scopeResourceId", query?.Scope?.ScopeResourceId),
            ("scopeName", query?.Scope?.ScopeName),
            ("scopeKind", query?.Scope?.ScopeKind),
            ("deploymentRevision", query?.Scope?.DeploymentRevision));

    public async Task IngestMetricPointsAsync(
        IEnumerable<MetricPoint> points,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("metrics/ingest"),
            new MetricIngestRequest(points.ToArray()),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<bool> HasResourceMonitoringAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        GetRequiredAsync<bool>(
            $"resources/{Escape(resourceId)}/monitoring/availability",
            cancellationToken);

    public Task<ResourceMonitoringSnapshot?> GetResourceMonitoringAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        GetOptionalAsync<ResourceMonitoringSnapshot>(
            $"resources/{Escape(resourceId)}/monitoring",
            cancellationToken);

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

    private static HttpClient CreateAuthenticatedHttpClient(
        Uri baseAddress,
        CloudShellResourceCredential credential,
        IEnumerable<string>? scopes)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(credential);

        var options = new RemoteControlPlaneOptions
        {
            BaseAddress = baseAddress
        };
        if (scopes is not null)
        {
            options.Credential.Scopes = scopes.ToArray();
        }

        var handler = new ControlPlaneAuthenticationHandler(
            new CloudShellResourceControlPlaneCredential(credential),
            Options.Create(options))
        {
            InnerHandler = new HttpClientHandler()
        };

        return new HttpClient(handler)
        {
            BaseAddress = baseAddress
        };
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
        return BuildUri(RoutePrefix, path, query);
    }

    private static string BuildUri(
        string routePrefix,
        string path,
        params (string Name, string? Value)[] query)
    {
        var uri = $"{routePrefix.TrimEnd('/')}/{path.TrimStart('/')}";
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
    IReadOnlyDictionary<string, string>? Attributes,
    IReadOnlyList<ResourceCapabilityResponse>? Capabilities,
    IReadOnlyList<ResourceEndpointMappingResponse>? EndpointMappings,
    IReadOnlyList<ResourceEndpointNetworkMappingResponse>? EndpointNetworkMappings,
    IReadOnlyList<LoadBalancerRouteResponse>? LoadBalancerRoutes,
    ResourceIdentityBindingResponse? Identity,
    IReadOnlyDictionary<string, ResourceActionResponse> ResourceActions,
    ResourceSource Source = ResourceSource.User,
    ResourceManagementMode ManagementMode = ResourceManagementMode.UserManaged,
    ResourceVisibility Visibility = ResourceVisibility.Normal,
    string? OwnerResourceId = null,
    ResourceCleanupBehavior CleanupBehavior = ResourceCleanupBehavior.None,
    ResourceObservabilityResponse? Observability = null);

file sealed record ResourceObservabilityResponse(
    bool Logs,
    bool Traces,
    bool Metrics,
    string? OtlpEndpoint,
    string? OtlpProtocol,
    string? OtlpHeaders,
    string? ServiceName,
    IReadOnlyDictionary<string, string>? Attributes,
    IReadOnlyList<TelemetryScopeDescriptorResponse>? Scopes,
    IReadOnlyList<TelemetrySourceDescriptorResponse>? Sources);

file sealed record TelemetryScopeDescriptorResponse(
    string ScopeResourceId,
    string Name,
    string Kind,
    string? Description,
    string? DeploymentRevision,
    IReadOnlyDictionary<string, string>? Attributes);

file sealed record TelemetrySourceDescriptorResponse(
    string Id,
    string Name,
    TelemetrySignalKind Signals,
    TelemetrySourceKind Kind,
    string? Endpoint,
    string? Protocol,
    string? Description,
    IReadOnlyList<TelemetryScopeDescriptorResponse>? Scopes,
    IReadOnlyDictionary<string, string>? Attributes);

file sealed record ResourceEndpointResponse(
    string Name,
    string Protocol,
    bool IsExternal,
    ResourceExposureScope? Exposure,
    int? TargetPort);

file sealed record ResourceCapabilityResponse(
    string Id,
    IReadOnlyDictionary<string, string>? Metadata);

file sealed record ResourceEndpointReferenceResponse(
    string ResourceId,
    string EndpointName);

file sealed record ResourceEndpointMappingResponse(
    string Id,
    string Name,
    ResourceEndpointReferenceResponse Source,
    ResourceEndpointReferenceResponse Target,
    string? NetworkResourceId,
    string? ProviderResourceId);

file sealed record ResourceEndpointNetworkMappingResponse(
    string Id,
    string Name,
    ResourceEndpointReferenceResponse Target,
    string Address,
    ResourceExposureScope Exposure,
    string? NetworkResourceId,
    string? ProviderResourceId,
    string? SourceEndpointName);

file sealed record LoadBalancerRouteResponse(
    string Id,
    string Name,
    LoadBalancerRouteKind Kind,
    string EntrypointName,
    LoadBalancerRouteMatchResponse Match,
    LoadBalancerRouteTargetResponse Target);

file sealed record LoadBalancerRouteMatchResponse(
    string? Host,
    string? PathPrefix,
    int? Port);

file sealed record LoadBalancerRouteTargetResponse(
    string ResourceId,
    string? EndpointName,
    int? Port);

file sealed record ResourceActionResponse(
    string Id,
    string DisplayName,
    ResourceActionKind Kind,
    string? Description,
    string? RequiredPermission,
    ResourceActionDisplayStyle DisplayStyle,
    ResourceActionIcon Icon,
    bool RequiresConfirmation,
    string? Method,
    string? Href);

file sealed record ResourceIdentityBindingResponse(
    ResourceIdentityBindingKind Kind,
    string? Name,
    string? ProviderId,
    string? Subject,
    IReadOnlyList<string>? Scopes,
    IReadOnlyDictionary<string, string>? Claims);

file sealed record ResourceIdentityReferenceResponse(
    string ResourceId,
    string? Name);

file sealed record ResourcePermissionGrantResponse(
    ResourceIdentityReferenceResponse Identity,
    string TargetResourceId,
    string Permission);

file sealed record GrantResourcePermissionRequest(
    string IdentityResourceId,
    string? IdentityName,
    string TargetResourceId,
    string Permission);

file sealed record RevokeResourcePermissionRequest(
    string IdentityResourceId,
    string? IdentityName,
    string TargetResourceId,
    string Permission);

file sealed record ResourcePermissionEvaluationRequest(
    ResourceIdentityReferenceResponse Identity,
    string TargetResourceId,
    string Permission);

file sealed record ResourcePermissionEvaluationResponse(
    ResourceIdentityReferenceResponse Identity,
    string TargetResourceId,
    string Permission,
    bool IsAllowed,
    ResourcePermissionGrantResponse? Grant);

file sealed record ResourceIdentityProvisioningDiagnosticResponse(
    ResourceIdentityProvisioningDiagnosticSeverity Severity,
    string Message,
    ResourceIdentityReferenceResponse? Identity,
    string? ProviderId);

file sealed record ResourceIdentityProvisioningResponse(
    string ProviderId,
    IReadOnlyList<ResourceIdentityProvisioningDiagnosticResponse>? Diagnostics);

file sealed record ResourceIdentityProviderSetupResponse(
    string ProviderId,
    IReadOnlyList<ResourceIdentityProvisioningDiagnosticResponse>? Diagnostics);

file sealed record ResourceIdentityProvisioningStatusResponse(
    ResourceIdentityReferenceResponse Identity,
    ResourceIdentityProvisioningState State,
    string? Detail,
    DateTimeOffset? ObservedAt);

file sealed record ResourceIdentityProvisioningStatusResultResponse(
    string ProviderId,
    IReadOnlyList<ResourceIdentityProvisioningStatusResponse>? Statuses,
    IReadOnlyList<ResourceIdentityProvisioningDiagnosticResponse>? Diagnostics);

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

file sealed record UpdateResourceReplicasRequest(
    int Replicas,
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

file sealed record ResourceEventResponse(
    string ResourceId,
    string EventType,
    string Message,
    DateTimeOffset Timestamp,
    string? TriggeredBy,
    string Level,
    string? TraceId,
    string? SpanId);

file sealed record LogEntryResponse(
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

file sealed record TraceIngestRequest(IReadOnlyList<TraceSpan> Spans);

file sealed record MetricIngestRequest(IReadOnlyList<MetricPoint> Points);

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
            Attributes: response.Attributes,
            Capabilities: response.GetCapabilityResponses()
                .Select(capability => capability.ToResourceCapability())
                .ToArray(),
            EndpointMappings: response.GetEndpointMappingResponses()
                .Select(mapping => mapping.ToResourceEndpointMapping())
                .ToArray(),
            EndpointNetworkMappings: response.GetEndpointNetworkMappingResponses()
                .Select(mapping => mapping.ToResourceEndpointNetworkMapping())
                .ToArray(),
            LoadBalancerRoutes: response.GetLoadBalancerRouteResponses()
                .Select(route => route.ToLoadBalancerRoute())
                .ToArray(),
            Identity: response.Identity?.ToResourceIdentityBinding(),
            Source: response.Source,
            ManagementMode: response.ManagementMode,
            Visibility: response.Visibility,
            OwnerResourceId: response.OwnerResourceId,
            CleanupBehavior: response.CleanupBehavior,
            DisplayName: response.DisplayName,
            Observability: response.Observability?.ToResourceObservability());

    public static ResourceObservability ToResourceObservability(
        this ResourceObservabilityResponse response) =>
        new(
            response.Logs,
            response.Traces,
            response.Metrics,
            response.OtlpEndpoint,
            response.OtlpProtocol,
            response.OtlpHeaders,
            response.ServiceName,
            response.Attributes,
            response.Scopes?
                .Select(scope => scope.ToTelemetryScopeDescriptor())
                .ToArray(),
            response.Sources?
                .Select(source => source.ToTelemetrySourceDescriptor())
                .ToArray());

    public static TelemetryScopeDescriptor ToTelemetryScopeDescriptor(
        this TelemetryScopeDescriptorResponse response) =>
        new(
            response.ScopeResourceId,
            response.Name,
            response.Kind,
            response.Description,
            response.DeploymentRevision,
            response.Attributes);

    public static TelemetrySourceDescriptor ToTelemetrySourceDescriptor(
        this TelemetrySourceDescriptorResponse response) =>
        new(
            response.Id,
            response.Name,
            response.Signals,
            response.Kind,
            response.Endpoint,
            response.Protocol,
            response.Description,
            response.Scopes?
                .Select(scope => scope.ToTelemetryScopeDescriptor())
                .ToArray(),
            response.Attributes);

    public static ResourceEndpoint ToResourceEndpoint(this ResourceEndpointResponse response) =>
        ResourceEndpoint.Contract(
            response.Name,
            response.Protocol,
            response.Exposure ?? (response.IsExternal
                ? ResourceExposureScope.Public
                : ResourceExposureScope.Local),
            response.TargetPort);

    public static ResourceCapability ToResourceCapability(this ResourceCapabilityResponse response) =>
        new(response.Id, response.Metadata);

    public static ResourceEndpointReference ToResourceEndpointReference(
        this ResourceEndpointReferenceResponse response) =>
        new(response.ResourceId, response.EndpointName);

    public static ResourceEndpointMappingDefinition ToResourceEndpointMapping(
        this ResourceEndpointMappingResponse response) =>
        new(
            response.Id,
            response.Name,
            response.Source.ToResourceEndpointReference(),
            response.Target.ToResourceEndpointReference(),
            response.NetworkResourceId,
            response.ProviderResourceId);

    public static ResourceEndpointNetworkMapping ToResourceEndpointNetworkMapping(
        this ResourceEndpointNetworkMappingResponse response) =>
        new(
            response.Id,
            response.Name,
            response.Target.ToResourceEndpointReference(),
            response.Address,
            response.Exposure,
            response.NetworkResourceId,
            response.ProviderResourceId,
            response.SourceEndpointName);

    public static LoadBalancerRoute ToLoadBalancerRoute(this LoadBalancerRouteResponse response) =>
        new(
            response.Id,
            response.Name,
            response.Kind,
            response.EntrypointName,
            response.Match.ToLoadBalancerRouteMatch(),
            response.Target.ToLoadBalancerRouteTarget());

    public static LoadBalancerRouteMatch ToLoadBalancerRouteMatch(
        this LoadBalancerRouteMatchResponse response) =>
        new(response.Host, response.PathPrefix, response.Port);

    public static LoadBalancerRouteTarget ToLoadBalancerRouteTarget(
        this LoadBalancerRouteTargetResponse response) =>
        new(response.ResourceId, response.EndpointName, response.Port);

    public static ResourceAction ToResourceAction(this ResourceActionResponse response) =>
        new(
            response.Id,
            response.DisplayName,
            response.Kind,
            response.Description,
            new ResourceActionPresentation(
                response.DisplayStyle,
                response.Icon,
                response.RequiresConfirmation),
            response.RequiredPermission);

    public static ResourceIdentityBinding ToResourceIdentityBinding(
        this ResourceIdentityBindingResponse response) =>
        new(
            response.ProviderId,
            response.Subject,
            response.Scopes,
            response.Claims,
            response.Kind,
            response.Name);

    public static ResourceIdentityReferenceResponse ToResponse(
        this ResourceIdentityReference identity) =>
        new(identity.ResourceId, identity.Name);

    public static ResourceIdentityReference ToResourceIdentityReference(
        this ResourceIdentityReferenceResponse response) =>
        new(response.ResourceId, response.Name);

    public static ResourcePermissionGrant ToResourcePermissionGrant(
        this ResourcePermissionGrantResponse response) =>
        new(
            response.Identity.ToResourceIdentityReference(),
            response.TargetResourceId,
            response.Permission);

    public static ResourcePermissionEvaluation ToResourcePermissionEvaluation(
        this ResourcePermissionEvaluationResponse response) =>
        new(
            response.Identity.ToResourceIdentityReference(),
            response.TargetResourceId,
            response.Permission,
            response.IsAllowed,
            response.Grant?.ToResourcePermissionGrant());

    public static ResourceIdentityProvisioningResult ToResourceIdentityProvisioningResult(
        this ResourceIdentityProvisioningResponse response) =>
        new(
            response.ProviderId,
            response.Diagnostics?
                .Select(diagnostic => diagnostic.ToResourceIdentityProvisioningDiagnostic())
                .ToArray());

    public static ResourceIdentityProvisioningDiagnostic ToResourceIdentityProvisioningDiagnostic(
        this ResourceIdentityProvisioningDiagnosticResponse response) =>
        new(
            response.Severity,
            response.Message,
            response.Identity?.ToResourceIdentityReference(),
            response.ProviderId);

    public static ResourceIdentityProviderSetupResult ToResourceIdentityProviderSetupResult(
        this ResourceIdentityProviderSetupResponse response) =>
        new(
            response.ProviderId,
            response.Diagnostics?
                .Select(diagnostic => diagnostic.ToResourceIdentityProvisioningDiagnostic())
                .ToArray());

    public static ResourceIdentityProvisioningStatusResult ToResourceIdentityProvisioningStatusResult(
        this ResourceIdentityProvisioningStatusResultResponse response) =>
        new(
            response.ProviderId,
            response.Statuses?
                .Select(status => status.ToResourceIdentityProvisioningStatus())
                .ToArray() ?? [],
            response.Diagnostics?
                .Select(diagnostic => diagnostic.ToResourceIdentityProvisioningDiagnostic())
                .ToArray());

    public static ResourceIdentityProvisioningStatus ToResourceIdentityProvisioningStatus(
        this ResourceIdentityProvisioningStatusResponse response) =>
        new(
            response.Identity.ToResourceIdentityReference(),
            response.State,
            response.Detail,
            response.ObservedAt);

    private static IReadOnlyCollection<ResourceActionResponse> GetResourceActionResponses(
        this ResourceResponse response) =>
        response.ResourceActions.Values.ToArray();

    private static IReadOnlyCollection<ResourceCapabilityResponse> GetCapabilityResponses(
        this ResourceResponse response) =>
        response.Capabilities?.ToArray() ?? [];

    private static IReadOnlyCollection<ResourceEndpointMappingResponse> GetEndpointMappingResponses(
        this ResourceResponse response) =>
        response.EndpointMappings?.ToArray() ?? [];

    private static IReadOnlyCollection<ResourceEndpointNetworkMappingResponse> GetEndpointNetworkMappingResponses(
        this ResourceResponse response) =>
        response.EndpointNetworkMappings?.ToArray() ?? [];

    private static IReadOnlyCollection<LoadBalancerRouteResponse> GetLoadBalancerRouteResponses(
        this ResourceResponse response) =>
        response.LoadBalancerRoutes?.ToArray() ?? [];

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
        new(
            response.Timestamp,
            response.Message,
            response.Severity,
            response.Source,
            response.EventId,
            response.Category,
            response.TraceId,
            response.SpanId,
            response.ExceptionSummary,
            response.Attributes);

    public static ResourceEvent ToResourceEvent(this ResourceEventResponse response) =>
        new(
            response.ResourceId,
            response.EventType,
            response.Message,
            response.Timestamp,
            response.TriggeredBy,
            response.Level,
            response.TraceId,
            response.SpanId);
}
