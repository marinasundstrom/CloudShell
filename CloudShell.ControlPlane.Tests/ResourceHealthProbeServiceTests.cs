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
        var service = new ResourceHealthProbeService([new HttpResourceProbeEvaluator(factory)]);
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

    [Fact]
    public async Task CheckAsync_UsesExplicitHttpProbeSource()
    {
        var factory = new RecordingHttpClientFactory();
        var service = new ResourceHealthProbeService([new HttpResourceProbeEvaluator(factory)]);
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
            HealthChecks:
            [
                new ResourceHealthCheck(
                    "/legacy",
                    Source: ResourceProbeSource.ForHttp("http://localhost/source-health"))
            ]);

        var health = await service.CheckAsync([resource]);

        Assert.Equal(ResourceHealthStatus.Healthy, Assert.Single(health).Value.Status);
        Assert.Equal(new Uri("http://localhost/source-health"), factory.Handler.RequestUri);
    }

    [Fact]
    public async Task CheckAsync_ReturnsUnknownForUnsupportedProbeSource()
    {
        var factory = new RecordingHttpClientFactory();
        var service = new ResourceHealthProbeService([new HttpResourceProbeEvaluator(factory)]);
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
            HealthChecks:
            [
                new ResourceHealthCheck(
                    new ResourceProbeSource("provider.process"),
                    ResourceProbeType.Liveness,
                    "process")
            ]);

        var health = await service.CheckAsync([resource]);
        var result = Assert.Single(Assert.Single(health).Value.Checks);

        Assert.Equal(ResourceHealthStatus.Unknown, result.Status);
        Assert.Equal("Unsupported probe source 'provider.process'", result.Detail);
        Assert.Null(factory.ClientName);
    }

    [Fact]
    public async Task CheckAsync_UsesMatchingProviderProbeEvaluator()
    {
        var evaluator = new ProviderProcessProbeEvaluator();
        var service = new ResourceHealthProbeService([evaluator]);
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
            HealthChecks:
            [
                new ResourceHealthCheck(
                    new ResourceProbeSource("provider.process"),
                    ResourceProbeType.Liveness,
                    "process")
            ]);

        var health = await service.CheckAsync([resource]);
        var result = Assert.Single(Assert.Single(health).Value.Checks);

        Assert.True(evaluator.Called);
        Assert.Equal(ResourceHealthStatus.Healthy, result.Status);
        Assert.Equal("Process is running", result.Detail);
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public RecordingHandler Handler { get; } = new();

        public string? ClientName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            ClientName = name;
            return new HttpClient(Handler);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ProviderProcessProbeEvaluator : IResourceProbeEvaluator
    {
        public bool Called { get; private set; }

        public bool CanEvaluate(Resource resource, ResourceHealthCheck check) =>
            string.Equals(check.EffectiveSource.Kind, "provider.process", StringComparison.OrdinalIgnoreCase);

        public Task<ResourceHealthCheckResult> EvaluateAsync(
            Resource resource,
            ResourceHealthCheck check,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult(new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Healthy,
                "Process is running",
                null));
        }
    }
}
