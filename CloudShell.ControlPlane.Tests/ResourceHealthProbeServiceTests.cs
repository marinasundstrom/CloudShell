using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Health;
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
        Assert.Equal(ResourceHealthCheckOutcome.Unsupported, result.Outcome);
        Assert.Equal("Unsupported probe source 'provider.process'", result.Detail);
        Assert.Null(factory.ClientName);
    }

    [Fact]
    public async Task CheckAsync_ClassifiesHttpErrorAsResponded()
    {
        var factory = new RecordingHttpClientFactory();
        factory.Handler.StatusCode = HttpStatusCode.ServiceUnavailable;
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
        var result = Assert.Single(Assert.Single(health).Value.Checks);

        Assert.Equal(ResourceHealthStatus.Unhealthy, result.Status);
        Assert.Equal(ResourceHealthCheckOutcome.Responded, result.Outcome);
    }

    [Fact]
    public async Task CheckAsync_ClassifiesHttpRequestFailureAsNoResponse()
    {
        var factory = new RecordingHttpClientFactory();
        factory.Handler.Exception = new HttpRequestException("Connection refused");
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
        var result = Assert.Single(Assert.Single(health).Value.Checks);

        Assert.Equal(ResourceHealthStatus.Unhealthy, result.Status);
        Assert.Equal(ResourceHealthCheckOutcome.NoResponse, result.Outcome);
        Assert.Equal("Connection refused", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_ClassifiesMissingHttpEndpointAsUnresolved()
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
            HealthChecks: [new ResourceHealthCheck("/health")]);

        var health = await service.CheckAsync([resource]);
        var result = Assert.Single(Assert.Single(health).Value.Checks);

        Assert.Equal(ResourceHealthStatus.Unknown, result.Status);
        Assert.Equal(ResourceHealthCheckOutcome.Unresolved, result.Outcome);
        Assert.Equal("No matching HTTP endpoint", result.Detail);
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

    [Fact]
    public async Task CheckAsync_PreservesScopedObservationsFromProviderProbeEvaluator()
    {
        var evaluator = new ScopedProviderProbeEvaluator();
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
                    new ResourceProbeSource("provider.container-app"),
                    ResourceProbeType.Liveness,
                    "liveness")
            ]);

        var health = await service.CheckAsync([resource]);
        var result = Assert.Single(Assert.Single(health).Value.Checks);
        var observations = result.ScopeObservations;

        Assert.Equal(ResourceHealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.CheckedAt);
        Assert.Equal(2, observations.Count);
        Assert.All(observations, observation => Assert.Equal(result.CheckedAt, observation.CheckedAt));
        Assert.Equal(ResourceHealthScopeKinds.Runtime, observations[0].ScopeKind);
        Assert.Equal("application:api/runtime:replica-1", observations[0].ResourceId);
        Assert.Equal(ResourceHealthStatus.Healthy, observations[0].Status);
        Assert.Equal(ResourceHealthStatus.Unhealthy, observations[1].Status);
        Assert.Equal(ResourceHealthCheckOutcome.NoResponse, observations[1].Outcome);
    }

    [Fact]
    public async Task CheckAsync_DoesNotEvaluateLivenessWhenResourceIsStopped()
    {
        var evaluator = new ProviderProcessProbeEvaluator();
        var service = new ResourceHealthProbeService([evaluator]);
        var resource = new Resource(
            "application:api",
            "API",
            "Application",
            "Applications",
            "local",
            ResourceState.Stopped,
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

        Assert.False(evaluator.Called);
        Assert.Equal(ResourceHealthStatus.Unknown, result.Status);
        Assert.Equal(ResourceHealthCheckOutcome.Unknown, result.Outcome);
        Assert.Contains("Liveness check is inactive", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_PreservesPreviousResultWhenCheckIsNotDue()
    {
        var evaluator = new ProviderProcessProbeEvaluator();
        var service = new ResourceHealthProbeService([evaluator]);
        var due = new ResourceHealthCheck(
            new ResourceProbeSource("provider.process"),
            ResourceProbeType.Liveness,
            "fast",
            intervalSeconds: 5);
        var notDue = new ResourceHealthCheck(
            new ResourceProbeSource("provider.process"),
            ResourceProbeType.Health,
            "slow",
            intervalSeconds: 60);
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
            HealthChecks: [due, notDue]);
        var previousCheckedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
        var previousSummary = new ResourceHealthSummary(
            resource.Id,
            ResourceHealthStatus.Unhealthy,
            previousCheckedAt,
            [
                new ResourceHealthCheckResult(
                    due,
                    ResourceHealthStatus.Unhealthy,
                    "Previous fast result",
                    null,
                    CheckedAt: previousCheckedAt),
                new ResourceHealthCheckResult(
                    notDue,
                    ResourceHealthStatus.Unhealthy,
                    "Previous slow result",
                    null,
                    CheckedAt: previousCheckedAt)
            ]);

        var health = await service.CheckAsync(
            [resource],
            new Dictionary<string, ResourceHealthSummary>(StringComparer.OrdinalIgnoreCase)
            {
                [resource.Id] = previousSummary
            },
            (_, check, _) => check.IntervalSeconds == 5);
        var summary = Assert.Single(health).Value;

        Assert.Equal(1, evaluator.CallCount);
        var fast = Assert.Single(summary.Checks, check => check.Check.Name == "fast");
        var slow = Assert.Single(summary.Checks, check => check.Check.Name == "slow");
        Assert.Equal(ResourceHealthStatus.Healthy, fast.Status);
        Assert.NotEqual(previousCheckedAt, fast.CheckedAt);
        Assert.Equal(ResourceHealthStatus.Unhealthy, slow.Status);
        Assert.Equal("Previous slow result", slow.Detail);
        Assert.Equal(previousCheckedAt, slow.CheckedAt);
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

        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public HttpRequestException? Exception { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Exception is not null
                ? Task.FromException<HttpResponseMessage>(Exception)
                : Task.FromResult(new HttpResponseMessage(StatusCode));
        }
    }

    private sealed class ProviderProcessProbeEvaluator : IResourceProbeEvaluator
    {
        public bool Called { get; private set; }

        public int CallCount { get; private set; }

        public bool CanEvaluate(Resource resource, ResourceHealthCheck check) =>
            string.Equals(check.EffectiveSource.Kind, "provider.process", StringComparison.OrdinalIgnoreCase);

        public Task<ResourceHealthCheckResult> EvaluateAsync(
            Resource resource,
            ResourceHealthCheck check,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            CallCount++;
            return Task.FromResult(new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Healthy,
                "Process is running",
                null));
        }
    }

    private sealed class ScopedProviderProbeEvaluator : IResourceProbeEvaluator
    {
        public bool CanEvaluate(Resource resource, ResourceHealthCheck check) =>
            string.Equals(check.EffectiveSource.Kind, "provider.container-app", StringComparison.OrdinalIgnoreCase);

        public Task<ResourceHealthCheckResult> EvaluateAsync(
            Resource resource,
            ResourceHealthCheck check,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unhealthy,
                "One replica is unhealthy.",
                null,
                Observations:
                [
                    new ResourceHealthScopeObservation(
                        "replica-1",
                        ResourceHealthScopeKinds.Runtime,
                        ResourceHealthStatus.Healthy,
                        "Replica is running.",
                        DisplayName: "Replica 1",
                        ResourceId: "application:api/runtime:replica-1"),
                    new ResourceHealthScopeObservation(
                        "replica-2",
                        ResourceHealthScopeKinds.Runtime,
                        ResourceHealthStatus.Unhealthy,
                        "Replica did not respond.",
                        ResourceHealthCheckOutcome.NoResponse,
                        DisplayName: "Replica 2",
                        ResourceId: "application:api/runtime:replica-2")
                ]));
    }
}
