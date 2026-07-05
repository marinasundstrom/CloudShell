using CloudShell.Client.Authentication;
using System.Net;

namespace CloudShell.Client.Tests;

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

    [Fact]
    public async Task ProfileCredential_ReadsActiveProfileStaticBearerToken()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, CloudShellProfileCredential.DefaultConfigFileName),
                """
                {
                  "activeProfile": "local",
                  "profiles": {
                    "local": {
                      "controlPlane": "http://127.0.0.1:5108",
                      "environment": "local",
                      "credential": {
                        "kind": "staticBearer",
                        "accessToken": "profile-token",
                        "expiresOn": "2099-01-01T00:00:00Z"
                      }
                    }
                  }
                }
                """);
            var credential = new CloudShellProfileCredential(
                new CloudShellProfileCredentialOptions
                {
                    ConfigDirectory = directory
                });

            var token = await credential.GetTokenAsync(new CloudShellResourceTokenRequest(["ControlPlane.Access"]));

            Assert.Equal("profile-token", token.Token);
            Assert.Equal(DateTimeOffset.Parse("2099-01-01T00:00:00Z"), token.ExpiresOn);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProfileCredential_ReadsRelativeStaticBearerTokenFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "tokens"));
            await File.WriteAllTextAsync(
                Path.Combine(directory, "tokens", "local.token"),
                "file-token\n");
            await File.WriteAllTextAsync(
                Path.Combine(directory, CloudShellProfileCredential.DefaultConfigFileName),
                """
                {
                  "activeProfile": "local",
                  "profiles": {
                    "local": {
                      "credential": {
                        "kind": "staticBearer",
                        "accessTokenPath": "tokens/local.token"
                      }
                    }
                  }
                }
                """);
            var credential = new CloudShellProfileCredential(
                new CloudShellProfileCredentialOptions
                {
                    ConfigDirectory = directory
                });

            var token = await credential.GetTokenAsync(new CloudShellResourceTokenRequest());

            Assert.Equal("file-token", token.Token);
            Assert.Null(token.ExpiresOn);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProfileCredential_UsesExplicitProfileName()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, CloudShellProfileCredential.DefaultConfigFileName),
                """
                {
                  "activeProfile": "local",
                  "profiles": {
                    "local": {
                      "credential": {
                        "kind": "staticBearer",
                        "accessToken": "local-token"
                      }
                    },
                    "remote": {
                      "credential": {
                        "kind": "staticBearer",
                        "accessToken": "remote-token"
                      }
                    }
                  }
                }
                """);
            var credential = new CloudShellProfileCredential(
                new CloudShellProfileCredentialOptions
                {
                    ConfigDirectory = directory,
                    ProfileName = "remote"
                });

            var token = await credential.GetTokenAsync(new CloudShellResourceTokenRequest());

            Assert.Equal("remote-token", token.Token);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProfileCredential_ReportsUnavailableWhenProfileTokenExpired()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, CloudShellProfileCredential.DefaultConfigFileName),
                """
                {
                  "activeProfile": "local",
                  "profiles": {
                    "local": {
                      "credential": {
                        "kind": "staticBearer",
                        "accessToken": "expired-token",
                        "expiresOn": "2000-01-01T00:00:00Z"
                      }
                    }
                  }
                }
                """);
            var credential = new CloudShellProfileCredential(
                new CloudShellProfileCredentialOptions
                {
                    ConfigDirectory = directory
                });

            var exception = await Assert.ThrowsAsync<CloudShellCredentialUnavailableException>(() =>
                credential.GetTokenAsync(new CloudShellResourceTokenRequest()).AsTask());

            Assert.Contains("has expired", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
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
