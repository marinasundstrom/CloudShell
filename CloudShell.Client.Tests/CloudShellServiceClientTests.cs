using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;
using CloudShell.Secrets.Client;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;

namespace CloudShell.Client.Tests;

public sealed class CloudShellServiceClientTests
{
    [Fact]
    public async Task ConfigurationStoreClient_SendsBearerTokenAndReadsEntries()
    {
        var handler = new RecordingHandler("""
            [
              { "name": "Sample:Message", "value": "Hello", "isSecret": false }
            ]
            """);
        var credential = new RecordingCredential("configuration-token");
        var client = new ConfigurationStoreClient(
            new Uri("http://localhost/api/configuration/stores/configuration%3Aapp/entries"),
            credential,
            new HttpClient(handler),
            ["ControlPlane.Access"]);

        var entries = await client.GetEntriesAsync();

        var entry = Assert.Single(entries);
        Assert.Equal("Sample:Message", entry.Name);
        Assert.Equal("Hello", entry.Value);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("configuration-token", handler.Requests[0].Headers.Authorization?.Parameter);
        Assert.Equal(["ControlPlane.Access"], credential.RequestedScopes);
    }

    [Fact]
    public async Task ConfigurationStoreClient_BuildsEntryEndpoint()
    {
        var handler = new RecordingHandler("""
            { "name": "Sample:Mode", "value": "Development", "isSecret": false }
            """);
        var client = new ConfigurationStoreClient(
            new Uri("http://localhost/api/configuration/entries?resourceId=configuration%3Aapp"),
            new RecordingCredential("configuration-token"),
            new HttpClient(handler));

        var entry = await client.GetEntryAsync("Sample:Mode");

        Assert.Equal("Development", entry?.Value);
        Assert.Equal(
            "http://localhost/api/configuration/entries/Sample%3AMode?resourceId=configuration%3Aapp",
            handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task SecretsVaultClient_SendsBearerTokenAndReadsSecret()
    {
        var handler = new RecordingHandler("""
            { "name": "sample-api-key", "value": "secret-value", "version": "v1" }
            """);
        var credential = new RecordingCredential("secrets-token");
        var client = new SecretsVaultClient(
            new Uri("http://localhost/api/secrets/vaults/secrets-vault%3Aapp/secrets"),
            credential,
            new HttpClient(handler),
            ["ControlPlane.Access"]);

        var secret = await client.GetSecretAsync("sample-api-key", version: "v1");

        Assert.Equal("secret-value", secret?.Value);
        Assert.Equal(
            "http://localhost/api/secrets/vaults/secrets-vault%3Aapp/secrets/sample-api-key?version=v1",
            handler.Requests[0].RequestUri?.ToString());
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("secrets-token", handler.Requests[0].Headers.Authorization?.Parameter);
        Assert.Equal(["ControlPlane.Access"], credential.RequestedScopes);
    }

    [Fact]
    public async Task SecretsVaultClient_ReadsSecretMetadata()
    {
        var handler = new RecordingHandler("""
            [
              { "name": "sample-api-key", "version": "v1" }
            ]
            """);
        var client = new SecretsVaultClient(
            new Uri("http://localhost/api/secrets/vaults/secrets-vault%3Aapp/secrets"),
            new RecordingCredential("secrets-token"),
            new HttpClient(handler));

        var secrets = await client.GetSecretsAsync();

        var secret = Assert.Single(secrets);
        Assert.Equal("sample-api-key", secret.Name);
        Assert.Equal("v1", secret.Version);
    }

    [Fact]
    public void AddCloudShellConfigurationStore_LoadsEntriesIntoConfiguration()
    {
        using var server = LoopbackServer.Start("""
            [
              { "name": "Sample:Message", "value": "Hello", "isSecret": false }
            ]
            """);

        var configuration = new ConfigurationBuilder()
            .AddCloudShellConfigurationStore(options =>
            {
                options.Endpoint = server.BaseAddress.ToString();
                options.Credential = new RecordingCredential("configuration-token");
            })
            .Build();

        Assert.Equal("Hello", configuration["Sample:Message"]);
        Assert.Equal("connected", configuration["CloudShell:ConfigurationStore:Status"]);
        Assert.Equal(server.BaseAddress.ToString(), configuration["CloudShell:ConfigurationStore:Source"]);
    }

    [Fact]
    public void AddCloudShellSecretsVault_LoadsSecretsIntoConfiguration()
    {
        using var server = LoopbackServer.Start(
            """
            [
              { "name": "Sample--ApiKey", "version": "v1" }
            ]
            """,
            """
            { "name": "Sample--ApiKey", "value": "secret-value", "version": "v1" }
            """);

        var configuration = new ConfigurationBuilder()
            .AddCloudShellSecretsVault(options =>
            {
                options.Endpoint = server.BaseAddress.ToString();
                options.Credential = new RecordingCredential("secrets-token");
            })
            .Build();

        Assert.Equal("secret-value", configuration["Sample:ApiKey"]);
        Assert.Equal("connected", configuration["CloudShell:SecretsVault:Status"]);
        Assert.Equal("Sample:ApiKey", configuration["CloudShell:SecretsVault:LoadedKeys"]);
    }

    private sealed class RecordingCredential(string token) : CloudShellResourceCredential
    {
        public IReadOnlyList<string> RequestedScopes { get; private set; } = [];

        public override ValueTask<CloudShellResourceAccessToken> GetTokenAsync(
            CloudShellResourceTokenRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestedScopes = request.Scopes.ToArray();
            return ValueTask.FromResult(
                new CloudShellResourceAccessToken(
                    token,
                    DateTimeOffset.UtcNow.AddMinutes(5)));
        }
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener listener;
        private readonly Queue<string> responses;
        private readonly Task serveTask;

        private LoopbackServer(HttpListener listener, Uri baseAddress, IEnumerable<string> responses)
        {
            this.listener = listener;
            BaseAddress = baseAddress;
            this.responses = new Queue<string>(responses);
            serveTask = Task.Run(ServeAsync);
        }

        public Uri BaseAddress { get; }

        public static LoopbackServer Start(params string[] responses)
        {
            var port = GetFreePort();
            var baseAddress = new Uri($"http://127.0.0.1:{port}/");
            var listener = new HttpListener();
            listener.Prefixes.Add(baseAddress.ToString());
            listener.Start();
            return new LoopbackServer(listener, baseAddress, responses);
        }

        public void Dispose()
        {
            listener.Close();
            try
            {
                serveTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
            }
        }

        private async Task ServeAsync()
        {
            while (listener.IsListening && responses.Count > 0)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                var responseJson = responses.Dequeue();
                var bytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        private static int GetFreePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }
    }
}
