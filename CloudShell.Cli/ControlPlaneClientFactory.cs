using CloudShell.ControlPlane.Client;

namespace CloudShell.Cli;

internal static class ControlPlaneClientFactory
{
    public static ControlPlaneApiClient Create(Uri controlPlaneUrl, string? bearerToken)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = NormalizeBaseAddress(controlPlaneUrl)
        };
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return new ControlPlaneApiClient(httpClient, new RemoteControlPlane(httpClient));
    }

    private static Uri NormalizeBaseAddress(Uri baseUrl)
    {
        var value = baseUrl.ToString();
        return value.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl
            : new Uri(value + "/", UriKind.Absolute);
    }
}

internal sealed class ControlPlaneApiClient(
    HttpClient httpClient,
    RemoteControlPlane controlPlane) : IDisposable
{
    public RemoteControlPlane ControlPlane { get; } = controlPlane;

    public void Dispose() => httpClient.Dispose();
}
