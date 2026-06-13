using System.Net;
using CloudShell.Abstractions.Authentication;

namespace CloudShell.Abstractions.Tests;

public sealed class CloudShellResourceCredentialTests
{
    [Fact]
    public async Task EnvironmentCredential_AcquiresClientCredentialsToken()
    {
        var handler = new RecordingTokenHandler(
            """
            {
              "access_token": "sample-token",
              "expires_in": 3600
            }
            """);
        var credential = new EnvironmentCloudShellResourceCredential(
            new EnvironmentCloudShellResourceCredentialOptions
            {
                TokenEndpoint = "http://localhost/token",
                ClientId = "application:api/api-service",
                ClientSecret = "local-development-secret"
            },
            new HttpClient(handler));

        var token = await credential.GetTokenAsync(
            new CloudShellResourceTokenRequest(["ControlPlane.Access"]));

        Assert.Equal("sample-token", token.Token);
        Assert.NotNull(token.ExpiresOn);
        Assert.Equal("http://localhost/token", handler.RequestUri?.ToString());
        Assert.Contains("grant_type=client_credentials", handler.Body);
        Assert.Contains("client_id=application%3Aapi%2Fapi-service", handler.Body);
        Assert.Contains("scope=ControlPlane.Access", handler.Body);
    }

    [Fact]
    public async Task EnvironmentCredential_ReportsUnavailableWhenSettingsAreMissing()
    {
        var credential = new EnvironmentCloudShellResourceCredential(
            new EnvironmentCloudShellResourceCredentialOptions(),
            new HttpClient(new RecordingTokenHandler("{}")));

        var exception = await Assert.ThrowsAsync<CloudShellCredentialUnavailableException>(() =>
            credential.GetTokenAsync(new CloudShellResourceTokenRequest(["ControlPlane.Access"])).AsTask());

        Assert.Contains("token endpoint, client id, or client secret", exception.Message);
    }

    [Fact]
    public async Task DefaultCredential_TriesConfiguredCredentialSources()
    {
        var credential = new DefaultCloudShellResourceCredential(
            [
                new UnavailableCredential(),
                new StaticCredential("fallback-token")
            ]);

        var token = await credential.GetTokenAsync(new CloudShellResourceTokenRequest());

        Assert.Equal("fallback-token", token.Token);
    }

    private sealed class StaticCredential(string token) : CloudShellResourceCredential
    {
        public override ValueTask<CloudShellResourceAccessToken> GetTokenAsync(
            CloudShellResourceTokenRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CloudShellResourceAccessToken(token));
    }

    private sealed class UnavailableCredential : CloudShellResourceCredential
    {
        public override ValueTask<CloudShellResourceAccessToken> GetTokenAsync(
            CloudShellResourceTokenRequest request,
            CancellationToken cancellationToken = default) =>
            throw new CloudShellCredentialUnavailableException("Credential is unavailable.");
    }

    private sealed class RecordingTokenHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        }
    }
}
