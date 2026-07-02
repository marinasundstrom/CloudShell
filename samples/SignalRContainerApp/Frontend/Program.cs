using System.Buffers;
using System.Net.WebSockets;

var builder = WebApplication.CreateBuilder(args);

var backendBaseUrl = builder.Configuration["SignalRBackend:BaseUrl"] ??
    "http://localhost:5095";
var backendBaseUri = new Uri($"{backendBaseUrl.TrimEnd('/')}/", UriKind.Absolute);
const string backendProxyPath = "/signalr-backend";

builder.Services.AddHttpClient("SignalRBackendProxy");

var app = builder.Build();

app.UseWebSockets();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    backendBaseUrl
}));
app.MapGet("/alive", () => Results.Ok(new
{
    status = "alive",
    backendBaseUrl
}));
app.MapGet("/sample-config.json", () => Results.Json(new SignalRContainerAppClientOptions(
    backendProxyPath)));
app.Map(
    $"{backendProxyPath}/{{**path}}",
    async (
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        string? path,
        CancellationToken cancellationToken) =>
    {
        var targetUri = CreateBackendUri(backendBaseUri, path, context.Request.QueryString);
        if (context.WebSockets.IsWebSocketRequest)
        {
            await ProxyWebSocketAsync(context, targetUri, cancellationToken);
            return;
        }

        await ProxyHttpAsync(context, httpClientFactory.CreateClient("SignalRBackendProxy"), targetUri, cancellationToken);
    });
app.MapFallbackToFile("index.html");

app.Run();

static Uri CreateBackendUri(Uri backendBaseUri, string? path, QueryString queryString)
{
    var uriBuilder = new UriBuilder(new Uri(backendBaseUri, path ?? string.Empty));
    if (queryString.HasValue)
    {
        uriBuilder.Query = queryString.Value![1..];
    }

    return uriBuilder.Uri;
}

static async Task ProxyHttpAsync(
    HttpContext context,
    HttpClient httpClient,
    Uri targetUri,
    CancellationToken cancellationToken)
{
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);
    foreach (var header in context.Request.Headers)
    {
        if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
        {
            request.Content ??= new StreamContent(context.Request.Body);
            request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    if (request.Content is null &&
        (context.Request.ContentLength.GetValueOrDefault() > 0 ||
            HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method)))
    {
        request.Content = new StreamContent(context.Request.Body);
    }

    using var response = await httpClient.SendAsync(
        request,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);
    context.Response.StatusCode = (int)response.StatusCode;
    CopyHeaders(response.Headers, context.Response.Headers);
    CopyHeaders(response.Content.Headers, context.Response.Headers);
    context.Response.Headers.Remove("transfer-encoding");
    await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
}

static async Task ProxyWebSocketAsync(
    HttpContext context,
    Uri targetUri,
    CancellationToken cancellationToken)
{
    var targetWebSocketUri = new UriBuilder(targetUri)
    {
        Scheme = targetUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? "wss" : "ws"
    }.Uri;

    using var backendSocket = new ClientWebSocket();
    if (context.Request.Headers.TryGetValue("Cookie", out var cookieHeader))
    {
        backendSocket.Options.SetRequestHeader("Cookie", string.Join("; ", cookieHeader.ToArray()));
    }

    foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols)
    {
        backendSocket.Options.AddSubProtocol(protocol);
    }

    await backendSocket.ConnectAsync(targetWebSocketUri, cancellationToken);
    using var browserSocket = await context.WebSockets.AcceptWebSocketAsync(backendSocket.SubProtocol);
    var browserToBackend = CopyWebSocketAsync(browserSocket, backendSocket, cancellationToken);
    var backendToBrowser = CopyWebSocketAsync(backendSocket, browserSocket, cancellationToken);
    await Task.WhenAny(browserToBackend, backendToBrowser);
}

static async Task CopyWebSocketAsync(
    WebSocket source,
    WebSocket destination,
    CancellationToken cancellationToken)
{
    var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
    try
    {
        while (source.State == WebSocketState.Open &&
            destination.State == WebSocketState.Open)
        {
            var result = await source.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await destination.CloseOutputAsync(
                    source.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                    source.CloseStatusDescription,
                    cancellationToken);
                return;
            }

            await destination.SendAsync(
                buffer.AsMemory(0, result.Count),
                result.MessageType,
                result.EndOfMessage,
                cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (WebSocketException)
    {
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}

static void CopyHeaders(
    IEnumerable<KeyValuePair<string, IEnumerable<string>>> source,
    IHeaderDictionary target)
{
    foreach (var header in source)
    {
        target[header.Key] = header.Value.ToArray();
    }
}

internal sealed record SignalRContainerAppClientOptions(
    string BackendBaseUrl);
