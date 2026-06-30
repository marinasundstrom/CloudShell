using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalContainerApplicationProcessRuntimeOptions
{
    private readonly Dictionary<string, LocalContainerApplicationProcessDefinition> applications =
        new(StringComparer.OrdinalIgnoreCase);

    public IDictionary<string, LocalContainerApplicationProcessDefinition> Applications => applications;

    public LocalContainerApplicationProcessRuntimeOptions AddProject(
        string resourceId,
        string projectPath,
        Action<LocalContainerApplicationProcessDefinition>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var definition = new LocalContainerApplicationProcessDefinition
        {
            ProjectPath = Path.GetFullPath(projectPath)
        };
        configure?.Invoke(definition);
        applications[resourceId] = definition;
        return this;
    }
}

public sealed class LocalContainerApplicationProcessDefinition
{
    public string ProjectPath { get; set; } = string.Empty;

    public string? WorkingDirectory { get; set; }

    public string DotNetExecutable { get; set; } = "dotnet";

    public string HealthPath { get; set; } = "/health";

    public int? IngressPort { get; set; }

    public int? ReplicaPortStart { get; set; }

    public string ReplicaServiceNamePrefix { get; set; } = "container-app-replica-";

    public string? TraceIngestEndpoint { get; set; }

    public string? MetricIngestEndpoint { get; set; }

    public TimeSpan ReplicaStartTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public IDictionary<string, string?> Environment { get; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

public sealed class LocalContainerApplicationProcessRuntimeHandler(
    LocalContainerApplicationProcessRuntimeBridge bridge,
    IOptions<LocalContainerApplicationProcessRuntimeOptions> options) :
    IContainerApplicationRuntimeHandler
{
    private const string RuntimeUnavailableDiagnosticCode =
        "application.container.localProcessRuntimeUnavailable";
    private const string RuntimeUnsupportedOperationDiagnosticCode =
        "application.container.localProcessRuntimeUnsupportedOperation";
    private readonly LocalContainerApplicationProcessRuntimeOptions options = options.Value;

    public ContainerApplicationRuntimeStatus GetStatus(Resource resource) =>
        TryGetDefinition(resource, out _)
            ? bridge.GetStatus(resource.EffectiveResourceId)
            : ContainerApplicationRuntimeStatus.Unknown;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDefinition(resource, out var definition))
        {
            return RuntimeUnavailable(resource);
        }

        if (string.Equals(operationId.Value, ResourceActionIds.Start, StringComparison.OrdinalIgnoreCase))
        {
            return await bridge.StartAsync(resource, definition, cancellationToken);
        }

        if (string.Equals(operationId.Value, ResourceActionIds.Stop, StringComparison.OrdinalIgnoreCase))
        {
            return await bridge.StopAsync(resource, cancellationToken);
        }

        if (string.Equals(operationId.Value, ResourceActionIds.Restart, StringComparison.OrdinalIgnoreCase))
        {
            return await bridge.RestartAsync(resource, definition, cancellationToken);
        }

        return
        [
            ResourceDefinitionDiagnostic.Error(
                RuntimeUnsupportedOperationDiagnosticCode,
                $"The local container application process runtime does not support operation '{operationId}'.",
                resource.EffectiveResourceId)
        ];
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDefinition(resource, out var definition))
        {
            return RuntimeUnavailable(resource);
        }

        return await bridge.RestartAsync(resource, definition, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDefinition(resource, out var definition))
        {
            return RuntimeUnavailable(resource);
        }

        return await bridge.RestartAsync(resource, definition, cancellationToken);
    }

    private bool TryGetDefinition(
        Resource resource,
        out LocalContainerApplicationProcessDefinition definition) =>
        options.Applications.TryGetValue(resource.EffectiveResourceId, out definition!);

    private static IReadOnlyList<ResourceDefinitionDiagnostic> RuntimeUnavailable(Resource resource) =>
    [
        ResourceDefinitionDiagnostic.Error(
            RuntimeUnavailableDiagnosticCode,
            $"No local process runtime mapping is configured for container app resource '{resource.EffectiveResourceId}'.",
            resource.EffectiveResourceId)
    ];
}

public sealed class LocalContainerApplicationProcessRuntimeBridge(
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    ILogger<LocalContainerApplicationProcessRuntimeBridge> logger) : IAsyncDisposable
{
    private const string RuntimeDiagnosticCode = "application.container.localProcessRuntime";
    private const string RuntimeFailureDiagnosticCode = "application.container.localProcessRuntimeFailed";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HttpClient httpClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });
    private readonly Dictionary<string, LocalContainerApplicationProcessRuntimeState> states =
        new(StringComparer.OrdinalIgnoreCase);

    public ContainerApplicationRuntimeStatus GetStatus(string resourceId)
    {
        if (!states.TryGetValue(resourceId, out var state) ||
            state.Replicas.Count == 0 ||
            state.ProxyTask is null)
        {
            return ContainerApplicationRuntimeStatus.Stopped;
        }

        return state.Replicas.All(replica => !replica.Process.HasExited) &&
            !state.ProxyTask.IsCompleted
                ? ContainerApplicationRuntimeStatus.Running
                : ContainerApplicationRuntimeStatus.Unknown;
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        LocalContainerApplicationProcessDefinition definition,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (GetStatus(resource.EffectiveResourceId) == ContainerApplicationRuntimeStatus.Running)
            {
                return
                [
                    new(
                        ResourceDefinitionDiagnosticSeverity.Information,
                        RuntimeDiagnosticCode,
                        $"Local process runtime for '{GetResourceLabel(resource)}' is already running.",
                        resource.EffectiveResourceId)
                ];
            }

            var state = GetState(resource.EffectiveResourceId);
            await StopCoreAsync(state, cancellationToken);

            var replicaCount = GetReplicaCount(resource);
            var ingressPort = GetIngressPort(resource, definition);
            var replicaPortStart = definition.ReplicaPortStart ?? ingressPort + 1;

            for (var replica = 1; replica <= replicaCount; replica++)
            {
                var port = replicaPortStart + replica - 1;
                var process = StartReplica(definition, replica, port, resource);
                state.Replicas.Add(new(replica, port, process));
            }

            await WaitForReplicasReadyAsync(state, definition, cancellationToken);

            state.Proxy = CreateProxy(resource, state, ingressPort);
            state.ProxyTask = state.Proxy.RunAsync(cancellationToken);

            return
            [
                new(
                    ResourceDefinitionDiagnosticSeverity.Information,
                    RuntimeDiagnosticCode,
                    $"Started local process runtime for '{GetResourceLabel(resource)}' with {replicaCount.ToString(CultureInfo.InvariantCulture)} replica(s) at http://localhost:{ingressPort.ToString(CultureInfo.InvariantCulture)}.",
                    resource.EffectiveResourceId)
            ];
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to start the local process container app runtime for {ResourceId}.",
                resource.EffectiveResourceId);
            if (states.TryGetValue(resource.EffectiveResourceId, out var state))
            {
                await StopCoreAsync(state, CancellationToken.None);
            }

            return
            [
                ResourceDefinitionDiagnostic.Error(
                    RuntimeFailureDiagnosticCode,
                    $"Failed to start local process runtime for '{GetResourceLabel(resource)}': {exception.Message}",
                    resource.EffectiveResourceId)
            ];
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StopAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (states.TryGetValue(resource.EffectiveResourceId, out var state))
            {
                await StopCoreAsync(state, cancellationToken);
            }

            return
            [
                new(
                    ResourceDefinitionDiagnosticSeverity.Information,
                    RuntimeDiagnosticCode,
                    $"Stopped local process runtime for '{GetResourceLabel(resource)}'.",
                    resource.EffectiveResourceId)
            ];
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to stop the local process container app runtime for {ResourceId}.",
                resource.EffectiveResourceId);
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    RuntimeFailureDiagnosticCode,
                    $"Failed to stop local process runtime for '{GetResourceLabel(resource)}': {exception.Message}",
                    resource.EffectiveResourceId)
            ];
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> RestartAsync(
        Resource resource,
        LocalContainerApplicationProcessDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var stopDiagnostics = await StopAsync(resource, cancellationToken);
        if (stopDiagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error))
        {
            return stopDiagnostics;
        }

        return await StartAsync(resource, definition, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var state in states.Values)
        {
            await StopCoreAsync(state, CancellationToken.None);
        }

        httpClient.Dispose();
        gate.Dispose();
    }

    private LocalContainerApplicationProcessRuntimeState GetState(string resourceId)
    {
        if (states.TryGetValue(resourceId, out var state))
        {
            return state;
        }

        state = new();
        states[resourceId] = state;
        return state;
    }

    private WebApplication CreateProxy(
        Resource resource,
        LocalContainerApplicationProcessRuntimeState state,
        int ingressPort)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://localhost:{ingressPort.ToString(CultureInfo.InvariantCulture)}");
        var app = builder.Build();
        app.UseWebSockets();
        app.Run(context => ProxyAsync(context, resource, state));
        return app;
    }

    private async Task ProxyAsync(
        HttpContext context,
        Resource resource,
        LocalContainerApplicationProcessRuntimeState state)
    {
        var replica = SelectReplica(context, resource, state);
        try
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                await ProxyWebSocketAsync(context, replica);
                return;
            }

            using var request = CreateProxyRequest(context, replica);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            CopyResponseHeaders(response, context.Response);
            AppendAffinityCookie(context, resource, replica);

            if (IsSignalRNegotiateRequest(context))
            {
                var payload = await response.Content.ReadAsByteArrayAsync(context.RequestAborted);
                TrackSignalRConnection(state, payload, replica);
                await context.Response.Body.WriteAsync(payload, context.RequestAborted);
            }
            else
            {
                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Local process container app proxy failed while forwarding {Method} {Path}{QueryString} to replica {ReplicaOrdinal} on port {ReplicaPort}.",
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString,
                replica.Ordinal,
                replica.Port);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "local_container_app_proxy_failed",
                    message = exception.Message,
                    exception = exception.GetType().Name,
                    replica = replica.Ordinal,
                    replicaPort = replica.Port
                });
            }
        }
    }

    private static ReplicaProcess SelectReplica(
        HttpContext context,
        Resource resource,
        LocalContainerApplicationProcessRuntimeState state)
    {
        if (TryGetSignalRConnectionId(context, out var connectionId) &&
            state.SignalRConnections.TryGetValue(connectionId, out var connectionReplica) &&
            state.Replicas.Any(replica => replica.Ordinal == connectionReplica.Ordinal))
        {
            return connectionReplica;
        }

        var cookieName = GetAffinityCookieName(resource);
        if (!string.IsNullOrWhiteSpace(cookieName) &&
            context.Request.Cookies.TryGetValue(cookieName, out var cookieValue) &&
            int.TryParse(cookieValue, CultureInfo.InvariantCulture, out var ordinal) &&
            state.Replicas.FirstOrDefault(replica => replica.Ordinal == ordinal) is { } matched)
        {
            return matched;
        }

        var index = Interlocked.Increment(ref state.NextReplicaIndex) - 1;
        return state.Replicas[index % state.Replicas.Count];
    }

    private static bool IsSignalRNegotiateRequest(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Value?.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase) == true;

    private void TrackSignalRConnection(
        LocalContainerApplicationProcessRuntimeState state,
        byte[] payload,
        ReplicaProcess replica)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("connectionToken", out var connectionToken) &&
                !string.IsNullOrWhiteSpace(connectionToken.GetString()))
            {
                state.SignalRConnections[connectionToken.GetString()!] = replica;
            }

            if (document.RootElement.TryGetProperty("connectionId", out var connectionId) &&
                !string.IsNullOrWhiteSpace(connectionId.GetString()))
            {
                state.SignalRConnections[connectionId.GetString()!] = replica;
            }
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Local process container app proxy could not read negotiate response for replica {ReplicaOrdinal}.",
                replica.Ordinal);
        }
    }

    private static bool TryGetSignalRConnectionId(
        HttpContext context,
        out string connectionId)
    {
        connectionId = string.Empty;
        if (!context.Request.Query.TryGetValue("id", out var values))
        {
            return false;
        }

        connectionId = values.FirstOrDefault() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(connectionId);
    }

    private static void AppendAffinityCookie(
        HttpContext context,
        Resource resource,
        ReplicaProcess replica)
    {
        var cookieName = GetAffinityCookieName(resource);
        if (string.IsNullOrWhiteSpace(cookieName))
        {
            return;
        }

        var maxAge = GetAffinityDuration(resource);
        context.Response.Cookies.Append(
            cookieName,
            replica.Ordinal.ToString(CultureInfo.InvariantCulture),
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = maxAge
            });
    }

    private static HttpRequestMessage CreateProxyRequest(HttpContext context, ReplicaProcess replica)
    {
        var targetUri = new UriBuilder(
            Uri.UriSchemeHttp,
            IPAddress.Loopback.ToString(),
            replica.Port,
            context.Request.Path)
        {
            Query = context.Request.QueryString.HasValue
                ? context.Request.QueryString.Value
                : null
        }.Uri;

        var request = new HttpRequestMessage(
            new HttpMethod(context.Request.Method),
            targetUri);

        if (HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method))
        {
            request.Content = new StreamContent(context.Request.Body);
        }

        foreach (var header in context.Request.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        return request;
    }

    private static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse target)
    {
        foreach (var header in source.Headers)
        {
            target.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in source.Content.Headers)
        {
            target.Headers[header.Key] = header.Value.ToArray();
        }

        target.Headers.Remove("transfer-encoding");
    }

    private async Task ProxyWebSocketAsync(HttpContext context, ReplicaProcess replica)
    {
        using var clientSocket = await context.WebSockets.AcceptWebSocketAsync();
        using var backendSocket = new ClientWebSocket();
        foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols)
        {
            backendSocket.Options.AddSubProtocol(protocol);
        }

        var targetUri = new UriBuilder(
            Uri.UriSchemeWs,
            IPAddress.Loopback.ToString(),
            replica.Port,
            context.Request.Path)
        {
            Query = context.Request.QueryString.HasValue
                ? context.Request.QueryString.Value
                : null
        }.Uri;

        try
        {
            await backendSocket.ConnectAsync(targetUri, context.RequestAborted);
            await Task.WhenAny(
                RelayWebSocketAsync(clientSocket, backendSocket, context.RequestAborted),
                RelayWebSocketAsync(backendSocket, clientSocket, context.RequestAborted));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Local process container app WebSocket proxy failed for {Path}{QueryString} to replica {ReplicaOrdinal} on port {ReplicaPort}.",
                context.Request.Path,
                context.Request.QueryString,
                replica.Ordinal,
                replica.Port);
            throw;
        }
    }

    private static async Task RelayWebSocketAsync(
        WebSocket source,
        WebSocket target,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        while (!cancellationToken.IsCancellationRequested &&
            source.State == WebSocketState.Open &&
            target.State == WebSocketState.Open)
        {
            var result = await source.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await target.CloseOutputAsync(
                    result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                    result.CloseStatusDescription,
                    cancellationToken);
                return;
            }

            await target.SendAsync(
                buffer.AsMemory(0, result.Count),
                result.MessageType,
                result.EndOfMessage,
                cancellationToken);
        }
    }

    private Process StartReplica(
        LocalContainerApplicationProcessDefinition definition,
        int replicaOrdinal,
        int port,
        Resource resource)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = definition.DotNetExecutable,
            WorkingDirectory = definition.WorkingDirectory ?? hostEnvironment.ContentRootPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(definition.ProjectPath);
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}";
        startInfo.Environment["CLOUDSHELL_REPLICA_ORDINAL"] = replicaOrdinal.ToString(CultureInfo.InvariantCulture);
        var replicaResourceId =
            $"{resource.EffectiveResourceId}:replica-{replicaOrdinal.ToString(CultureInfo.InvariantCulture)}";
        var replicaCount = GetReplicaCount(resource);
        startInfo.Environment["CLOUDSHELL_RESOURCE_ID"] = replicaResourceId;
        startInfo.Environment["CLOUDSHELL_TELEMETRY_RESOURCE_ID"] = resource.EffectiveResourceId;
        startInfo.Environment["OTEL_SERVICE_NAME"] =
            $"{definition.ReplicaServiceNamePrefix}{replicaOrdinal.ToString(CultureInfo.InvariantCulture)}";
        startInfo.Environment["OTEL_RESOURCE_ATTRIBUTES"] =
            CreateOtelResourceAttributes(resource, replicaResourceId, replicaOrdinal, replicaCount);
        AddEnvironment(
            startInfo,
            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
            FirstNonEmpty(definition.TraceIngestEndpoint, configuration["Observability:TraceIngestEndpoint"]));
        AddEnvironment(
            startInfo,
            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
            FirstNonEmpty(definition.MetricIngestEndpoint, configuration["Observability:MetricIngestEndpoint"]));
        foreach (var item in definition.Environment)
        {
            startInfo.Environment[item.Key] = item.Value;
        }

        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                $"Failed to start the local process replica for '{GetResourceLabel(resource)}'.");
        _ = DrainOutputAsync(resource, process.StandardOutput, replicaOrdinal, isError: false);
        _ = DrainOutputAsync(resource, process.StandardError, replicaOrdinal, isError: true);
        return process;
    }

    private async Task WaitForReplicasReadyAsync(
        LocalContainerApplicationProcessRuntimeState state,
        LocalContainerApplicationProcessDefinition definition,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(definition.ReplicaStartTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var readyCount = 0;
            foreach (var replica in state.Replicas)
            {
                if (replica.Process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Local process replica {replica.Ordinal.ToString(CultureInfo.InvariantCulture)} exited before it became ready.");
                }

                try
                {
                    using var response = await httpClient.GetAsync(
                        $"http://127.0.0.1:{replica.Port.ToString(CultureInfo.InvariantCulture)}{NormalizePath(definition.HealthPath)}",
                        cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        readyCount++;
                    }
                }
                catch (HttpRequestException)
                {
                }
            }

            if (readyCount == state.Replicas.Count)
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException(
            $"Local process container app replicas did not become ready within {definition.ReplicaStartTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
    }

    private async Task StopCoreAsync(
        LocalContainerApplicationProcessRuntimeState state,
        CancellationToken cancellationToken)
    {
        if (state.Proxy is not null)
        {
            await state.Proxy.StopAsync(cancellationToken);
            await state.Proxy.DisposeAsync();
            state.Proxy = null;
        }

        if (state.ProxyTask is not null)
        {
            try
            {
                await state.ProxyTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
            }

            state.ProxyTask = null;
        }

        foreach (var replica in state.Replicas)
        {
            if (replica.Process.HasExited)
            {
                replica.Process.Dispose();
                continue;
            }

            try
            {
                replica.Process.Kill(entireProcessTree: true);
                await replica.Process.WaitForExitAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                replica.Process.Dispose();
            }
        }

        state.Replicas.Clear();
        state.SignalRConnections.Clear();
        state.NextReplicaIndex = 0;
    }

    private static int GetReplicaCount(Resource resource)
    {
        var rawReplicas = resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas);
        return int.TryParse(rawReplicas, CultureInfo.InvariantCulture, out var replicas)
            ? Math.Max(1, replicas)
            : 1;
    }

    private static string CreateOtelResourceAttributes(
        Resource resource,
        string replicaResourceId,
        int replica,
        int replicaCount)
    {
        var replicaOrdinal = replica.ToString(CultureInfo.InvariantCulture);
        var totalReplicas = replicaCount.ToString(CultureInfo.InvariantCulture);
        return string.Join(
            ',',
            CreateOtelAttribute("service.instance.id", replicaResourceId),
            CreateOtelAttribute("cloudshell.resource.id", replicaResourceId),
            CreateOtelAttribute("cloudshell.resource.type", "runtime.process"),
            CreateOtelAttribute(TelemetryAttributeNames.ScopeResourceId, resource.EffectiveResourceId),
            CreateOtelAttribute(TelemetryAttributeNames.ScopeName, $"Replica {replicaOrdinal}"),
            CreateOtelAttribute(TelemetryAttributeNames.ScopeKind, "runtime"),
            CreateOtelAttribute(TelemetryAttributeNames.RuntimeReplicaOrdinal, replicaOrdinal),
            CreateOtelAttribute(TelemetryAttributeNames.RuntimeReplicaCount, totalReplicas));
    }

    private static string CreateOtelAttribute(
        string name,
        string value) =>
        $"{name}={EscapeOtelAttributeValue(value)}";

    private static string EscapeOtelAttributeValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var current in value)
        {
            if (current is '\\' or ',' or '=')
            {
                builder.Append('\\');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static void AddEnvironment(
        ProcessStartInfo startInfo,
        string name,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        startInfo.Environment[name] = value;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static int GetIngressPort(
        Resource resource,
        LocalContainerApplicationProcessDefinition definition)
    {
        if (definition.IngressPort is { } configuredPort)
        {
            return configuredPort;
        }

        var request = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests)?
            .FirstOrDefault(request => request.Port is > 0);
        if (request?.Port is { } mappedPort)
        {
            return mappedPort;
        }

        return 5095;
    }

    private static string? GetAffinityCookieName(Resource resource)
    {
        var mode = resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityMode);
        if (!string.Equals(mode, "Cookie", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityCookieName);
    }

    private static TimeSpan? GetAffinityDuration(Resource resource)
    {
        var rawSeconds = resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityDurationSeconds);
        return int.TryParse(rawSeconds, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private async Task DrainOutputAsync(
        Resource resource,
        StreamReader reader,
        int replicaOrdinal,
        bool isError)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (isError)
            {
                logger.LogWarning(
                    "{ResourceLabel} replica {Replica}: {Line}",
                    GetResourceLabel(resource),
                    replicaOrdinal,
                    line);
            }
            else
            {
                logger.LogInformation(
                    "{ResourceLabel} replica {Replica}: {Line}",
                    GetResourceLabel(resource),
                    replicaOrdinal,
                    line);
            }
        }
    }

    private static string NormalizePath(string path) =>
        path.StartsWith('/', StringComparison.Ordinal)
            ? path
            : "/" + path;

    private static string GetResourceLabel(Resource resource) =>
        string.IsNullOrWhiteSpace(resource.State.DisplayName)
            ? resource.EffectiveResourceId
            : resource.State.DisplayName;

    private sealed record ReplicaProcess(
        int Ordinal,
        int Port,
        Process Process);

    private sealed class LocalContainerApplicationProcessRuntimeState
    {
        public List<ReplicaProcess> Replicas { get; } = [];

        public ConcurrentDictionary<string, ReplicaProcess> SignalRConnections { get; } =
            new(StringComparer.Ordinal);

        public WebApplication? Proxy { get; set; }

        public Task? ProxyTask { get; set; }

        public int NextReplicaIndex;
    }
}
