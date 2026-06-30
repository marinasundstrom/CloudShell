using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;
using Microsoft.AspNetCore.Builder;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

internal sealed class SignalRContainerAppRuntimeHandler(
    SignalRContainerAppRuntimeBridge bridge) : IContainerApplicationRuntimeHandler
{
    public const string ApiResourceId = "application.container-app:signalr-api";

    public ContainerApplicationRuntimeStatus GetStatus(ResourceModelResource resource) =>
        IsSignalRApi(resource)
            ? bridge.GetStatus()
            : ContainerApplicationRuntimeStatus.Unknown;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSignalRApi(resource))
        {
            return [];
        }

        return await bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsSignalRApi(resource))
        {
            return [];
        }

        return await bridge.RestartAsync(resource, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsSignalRApi(resource))
        {
            return [];
        }

        return await bridge.RestartAsync(resource, cancellationToken);
    }

    private static bool IsSignalRApi(ResourceModelResource resource) =>
        string.Equals(resource.EffectiveResourceId, ApiResourceId, StringComparison.OrdinalIgnoreCase);
}

internal sealed class SignalRContainerAppRuntimeBridge(
    IWebHostEnvironment hostEnvironment,
    IConfiguration configuration,
    ILogger<SignalRContainerAppRuntimeBridge> logger) : IAsyncDisposable
{
    private const string RuntimeDiagnosticCode = "signalrContainerApp.runtime";
    private const string RuntimeFailureDiagnosticCode = "signalrContainerApp.runtimeFailed";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HttpClient httpClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });

    private readonly List<ReplicaProcess> replicas = [];
    private readonly ConcurrentDictionary<string, ReplicaProcess> signalRConnections =
        new(StringComparer.Ordinal);
    private WebApplication? proxy;
    private Task? proxyTask;
    private int nextReplicaIndex;

    public ContainerApplicationRuntimeStatus GetStatus()
    {
        if (replicas.Count == 0 || proxyTask is null)
        {
            return ContainerApplicationRuntimeStatus.Stopped;
        }

        return replicas.All(replica => !replica.Process.HasExited) && !proxyTask.IsCompleted
            ? ContainerApplicationRuntimeStatus.Running
            : ContainerApplicationRuntimeStatus.Unknown;
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(operationId.Value, ResourceActionIds.Start, StringComparison.OrdinalIgnoreCase))
        {
            return await StartAsync(resource, cancellationToken);
        }

        if (string.Equals(operationId.Value, ResourceActionIds.Stop, StringComparison.OrdinalIgnoreCase))
        {
            return await StopAsync(resource, cancellationToken);
        }

        if (string.Equals(operationId.Value, ResourceActionIds.Restart, StringComparison.OrdinalIgnoreCase))
        {
            return await RestartAsync(resource, cancellationToken);
        }

        return
        [
            ResourceDefinitionDiagnostic.Error(
                RuntimeFailureDiagnosticCode,
                $"The SignalR sample runtime does not support container app operation '{operationId}'.",
                resource.EffectiveResourceId)
        ];
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (GetStatus() == ContainerApplicationRuntimeStatus.Running)
            {
                return
                [
                    new(
                        ResourceDefinitionDiagnosticSeverity.Information,
                        RuntimeDiagnosticCode,
                        "SignalR container app runtime is already running.",
                        resource.EffectiveResourceId)
                ];
            }

            await StopCoreAsync(cancellationToken);

            var replicaCount = GetReplicaCount(resource);
            var ingressPort = GetIngressPort(resource);
            var apiProjectPath = Path.Combine(
                hostEnvironment.ContentRootPath,
                "Api",
                "CloudShell.SignalRContainerApp.Api.csproj");
            var replicaPortStart =
                configuration.GetValue<int?>("SignalRContainerApp:ReplicaPortStart") ??
                ingressPort + 1;

            for (var replica = 1; replica <= replicaCount; replica++)
            {
                var port = replicaPortStart + replica - 1;
                var process = StartReplica(apiProjectPath, replica, port, resource);
                replicas.Add(new(replica, port, process));
            }

            await WaitForReplicasReadyAsync(cancellationToken);

            proxy = CreateProxy(resource, ingressPort);
            proxyTask = proxy.RunAsync(cancellationToken);

            return
            [
                new(
                    ResourceDefinitionDiagnosticSeverity.Information,
                    RuntimeDiagnosticCode,
                    $"Started SignalR API runtime with {replicaCount.ToString(CultureInfo.InvariantCulture)} replica(s) at http://localhost:{ingressPort.ToString(CultureInfo.InvariantCulture)}.",
                    resource.EffectiveResourceId)
            ];
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to start the SignalR container app sample runtime.");
            await StopCoreAsync(CancellationToken.None);
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    RuntimeFailureDiagnosticCode,
                    $"Failed to start SignalR container app runtime: {exception.Message}",
                    resource.EffectiveResourceId)
            ];
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StopAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(cancellationToken);
            return
            [
                new(
                    ResourceDefinitionDiagnosticSeverity.Information,
                    RuntimeDiagnosticCode,
                    "Stopped SignalR container app runtime.",
                    resource.EffectiveResourceId)
            ];
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to stop the SignalR container app sample runtime.");
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    RuntimeFailureDiagnosticCode,
                    $"Failed to stop SignalR container app runtime: {exception.Message}",
                    resource.EffectiveResourceId)
            ];
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> RestartAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default)
    {
        var stopDiagnostics = await StopAsync(resource, cancellationToken);
        if (stopDiagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error))
        {
            return stopDiagnostics;
        }

        return await StartAsync(resource, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopCoreAsync(CancellationToken.None);
        httpClient.Dispose();
        gate.Dispose();
    }

    private WebApplication CreateProxy(ResourceModelResource resource, int ingressPort)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://localhost:{ingressPort.ToString(CultureInfo.InvariantCulture)}");
        var app = builder.Build();
        app.UseWebSockets();
        app.Run(context => ProxyAsync(context, resource));
        return app;
    }

    private async Task ProxyAsync(HttpContext context, ResourceModelResource resource)
    {
        var replica = SelectReplica(context, resource);
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
                TrackSignalRConnection(payload, replica);
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
                "SignalR sample proxy failed while forwarding {Method} {Path}{QueryString} to replica {ReplicaOrdinal} on port {ReplicaPort}.",
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
                    error = "signalr_proxy_failed",
                    message = exception.Message,
                    exception = exception.GetType().Name,
                    replica = replica.Ordinal,
                    replicaPort = replica.Port
                });
            }
        }
    }

    private ReplicaProcess SelectReplica(HttpContext context, ResourceModelResource resource)
    {
        if (TryGetSignalRConnectionId(context, out var connectionId) &&
            signalRConnections.TryGetValue(connectionId, out var connectionReplica) &&
            replicas.Any(replica => replica.Ordinal == connectionReplica.Ordinal))
        {
            return connectionReplica;
        }

        var cookieName = GetAffinityCookieName(resource);
        if (!string.IsNullOrWhiteSpace(cookieName) &&
            context.Request.Cookies.TryGetValue(cookieName, out var cookieValue) &&
            int.TryParse(cookieValue, CultureInfo.InvariantCulture, out var ordinal) &&
            replicas.FirstOrDefault(replica => replica.Ordinal == ordinal) is { } matched)
        {
            return matched;
        }

        var index = Interlocked.Increment(ref nextReplicaIndex) - 1;
        return replicas[index % replicas.Count];
    }

    private static bool IsSignalRNegotiateRequest(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Value?.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase) == true;

    private void TrackSignalRConnection(
        byte[] payload,
        ReplicaProcess replica)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("connectionToken", out var connectionToken) &&
                !string.IsNullOrWhiteSpace(connectionToken.GetString()))
            {
                signalRConnections[connectionToken.GetString()!] = replica;
            }

            if (document.RootElement.TryGetProperty("connectionId", out var connectionId) &&
                !string.IsNullOrWhiteSpace(connectionId.GetString()))
            {
                signalRConnections[connectionId.GetString()!] = replica;
            }
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "SignalR sample proxy could not read negotiate response for replica {ReplicaOrdinal}.",
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
        ResourceModelResource resource,
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
                "SignalR sample WebSocket proxy failed for {Path}{QueryString} to replica {ReplicaOrdinal} on port {ReplicaPort}.",
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
        string apiProjectPath,
        int replicaOrdinal,
        int port,
        ResourceModelResource resource)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = hostEnvironment.ContentRootPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(apiProjectPath);
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}";
        startInfo.Environment["CLOUDSHELL_REPLICA_ORDINAL"] = replicaOrdinal.ToString(CultureInfo.InvariantCulture);
        startInfo.Environment["CLOUDSHELL_RESOURCE_ID"] =
            $"{resource.EffectiveResourceId}:replica-{replicaOrdinal.ToString(CultureInfo.InvariantCulture)}";

        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the SignalR API replica process.");
        _ = DrainOutputAsync(process.StandardOutput, replicaOrdinal, isError: false);
        _ = DrainOutputAsync(process.StandardError, replicaOrdinal, isError: true);
        return process;
    }

    private async Task WaitForReplicasReadyAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(
            configuration.GetValue<int?>("SignalRContainerApp:ReplicaStartTimeoutSeconds") ?? 60);
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var readyCount = 0;
            foreach (var replica in replicas)
            {
                if (replica.Process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"SignalR API replica {replica.Ordinal.ToString(CultureInfo.InvariantCulture)} exited before it became ready.");
                }

                try
                {
                    using var response = await httpClient.GetAsync(
                        $"http://127.0.0.1:{replica.Port.ToString(CultureInfo.InvariantCulture)}/health",
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

            if (readyCount == replicas.Count)
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException(
            $"SignalR API replicas did not become ready within {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (proxy is not null)
        {
            await proxy.StopAsync(cancellationToken);
            await proxy.DisposeAsync();
            proxy = null;
        }

        if (proxyTask is not null)
        {
            try
            {
                await proxyTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
            }

            proxyTask = null;
        }

        foreach (var replica in replicas)
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

        replicas.Clear();
        signalRConnections.Clear();
        nextReplicaIndex = 0;
    }

    private static int GetReplicaCount(ResourceModelResource resource)
    {
        var rawReplicas = resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas);
        return int.TryParse(rawReplicas, CultureInfo.InvariantCulture, out var replicas)
            ? Math.Max(1, replicas)
            : 1;
    }

    private static int GetIngressPort(ResourceModelResource resource)
    {
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

    private static string? GetAffinityCookieName(ResourceModelResource resource)
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

    private static TimeSpan? GetAffinityDuration(ResourceModelResource resource)
    {
        var rawSeconds = resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityDurationSeconds);
        return int.TryParse(rawSeconds, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private async Task DrainOutputAsync(
        StreamReader reader,
        int replicaOrdinal,
        bool isError)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (isError)
            {
                logger.LogWarning("SignalR API replica {Replica}: {Line}", replicaOrdinal, line);
            }
            else
            {
                logger.LogInformation("SignalR API replica {Replica}: {Line}", replicaOrdinal, line);
            }
        }
    }

    private sealed record ReplicaProcess(
        int Ordinal,
        int Port,
        Process Process);
}
