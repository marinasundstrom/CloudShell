using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using System.Net;

namespace CloudShell.ControlPlane.Tests;

public sealed class ResourceHealthProbeServiceTests
{
    [Fact]
    public async Task CheckAsync_UsesResourceHealthProbeHttpClient()
    {
        var factory = new RecordingHttpClientFactory();
        var service = new ResourceHealthProbeService(factory);
        var resource = new Resource(
            "application:api",
            "API",
            "Application",
            "Applications",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            HealthChecks: [new ResourceHealthCheck("http://localhost/health")]);

        var health = await service.CheckAsync([resource]);

        Assert.Equal(ResourceHealthProbeService.HttpClientName, factory.ClientName);
        Assert.Equal(ResourceHealthStatus.Healthy, Assert.Single(health).Value.Status);
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public string? ClientName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            ClientName = name;
            return new HttpClient(new HealthyHandler());
        }
    }

    private sealed class HealthyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
