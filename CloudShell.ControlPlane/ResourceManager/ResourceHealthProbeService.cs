using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceHealthProbeService(IEnumerable<IResourceProbeEvaluator> evaluators)
{
    public const string HttpClientName = CloudShellLogCategories.ResourceHealthProbes;
    public const string HttpClientLogCategory = CloudShellLogCategories.ResourceHealthProbeHttpClient;

    public async Task<IReadOnlyDictionary<string, ResourceHealthSummary>> CheckAsync(
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<string, ResourceHealthSummary>? previousSummaries = null,
        Func<Resource, ResourceHealthCheck, DateTimeOffset?, bool>? shouldEvaluate = null,
        CancellationToken cancellationToken = default)
    {
        var probeable = resources
            .Where(resource => resource.ResourceHealthChecks.Count > 0)
            .ToArray();

        if (probeable.Length == 0)
        {
            return new Dictionary<string, ResourceHealthSummary>(StringComparer.OrdinalIgnoreCase);
        }

        var results = await Task.WhenAll(probeable.Select(resource =>
            CheckResourceAsync(
                resource,
                previousSummaries?.GetValueOrDefault(resource.Id),
                shouldEvaluate,
                cancellationToken)));
        return results.ToDictionary(
            result => result.ResourceId,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ResourceHealthSummary> CheckResourceAsync(
        Resource resource,
        ResourceHealthSummary? previousSummary,
        Func<Resource, ResourceHealthCheck, DateTimeOffset?, bool>? shouldEvaluate,
        CancellationToken cancellationToken)
    {
        var checks = new List<ResourceHealthCheckResult>();
        var now = DateTimeOffset.UtcNow;
        foreach (var check in resource.ResourceHealthChecks)
        {
            var previousResult = FindPreviousResult(previousSummary, check);
            if (previousResult is not null &&
                shouldEvaluate is not null &&
                !shouldEvaluate(resource, check, previousResult.CheckedAt ?? previousSummary!.CheckedAt))
            {
                checks.Add(previousResult);
                continue;
            }

            if (check.Type == ResourceProbeType.Liveness &&
                !IsLivenessActive(resource))
            {
                checks.Add(CreateInactiveLivenessResult(resource, check, now));
                continue;
            }

            var evaluator = evaluators.FirstOrDefault(candidate => candidate.CanEvaluate(resource, check));
            var result = evaluator is null
                ? CreateUnsupportedSourceResult(check, now)
                : await evaluator.EvaluateAsync(resource, check, cancellationToken);
            checks.Add(NormalizeResult(result, now));
        }

        var status = checks.Any(check => check.Status == ResourceHealthStatus.Unhealthy)
            ? ResourceHealthStatus.Unhealthy
            : checks.Any(check => check.Status == ResourceHealthStatus.Unknown)
                ? ResourceHealthStatus.Unknown
                : ResourceHealthStatus.Healthy;

        return new ResourceHealthSummary(
            resource.Id,
            status,
            now,
            checks);
    }

    private static ResourceHealthCheckResult CreateUnsupportedSourceResult(
        ResourceHealthCheck check,
        DateTimeOffset checkedAt) =>
        new(
            check,
            ResourceHealthStatus.Unknown,
            $"Unsupported probe source '{check.EffectiveSource.Kind}'",
            null,
            ResourceHealthCheckOutcome.Unsupported,
            checkedAt);

    private static ResourceHealthCheckResult CreateInactiveLivenessResult(
        Resource resource,
        ResourceHealthCheck check,
        DateTimeOffset checkedAt) =>
        new(
            check,
            ResourceHealthStatus.Unknown,
            $"Liveness check is inactive while resource state is {resource.State?.ToString() ?? "Unknown"}.",
            null,
            ResourceHealthCheckOutcome.Unknown,
            checkedAt);

    private static ResourceHealthCheckResult NormalizeResult(
        ResourceHealthCheckResult result,
        DateTimeOffset checkedAt)
    {
        var normalized = result.CheckedAt is null ? result with { CheckedAt = checkedAt } : result;
        var observationCheckedAt = normalized.CheckedAt ?? checkedAt;
        if (normalized.ScopeObservations.Count == 0 ||
            normalized.ScopeObservations.All(observation => observation.CheckedAt is not null))
        {
            return normalized;
        }

        return normalized with
        {
            Observations = normalized.ScopeObservations
                .Select(observation => observation.CheckedAt is null
                    ? observation with { CheckedAt = observationCheckedAt }
                    : observation)
                .ToArray()
        };
    }

    private static bool IsLivenessActive(Resource resource) =>
        resource.State is ResourceState.Running or ResourceState.Degraded;

    private static ResourceHealthCheckResult? FindPreviousResult(
        ResourceHealthSummary? previousSummary,
        ResourceHealthCheck check) =>
        previousSummary?.Checks.FirstOrDefault(result =>
            ReferenceEquals(result.Check, check) ||
            result.Check == check ||
            (string.Equals(result.Check.Name, check.Name, StringComparison.OrdinalIgnoreCase) &&
             result.Check.Type == check.Type));
}

public sealed class HttpResourceProbeEvaluator(IHttpClientFactory httpClientFactory) : IResourceProbeEvaluator
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public bool CanEvaluate(Resource resource, ResourceHealthCheck check) =>
        check.EffectiveSource.IsHttp;

    public async Task<ResourceHealthCheckResult> EvaluateAsync(
        Resource resource,
        ResourceHealthCheck check,
        CancellationToken cancellationToken)
    {
        var source = check.EffectiveSource.Http!;
        var uri = ResolveCheckUri(resource, source);
        if (uri is null)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unknown,
                "No matching HTTP endpoint",
                null,
                ResourceHealthCheckOutcome.Unresolved);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(source.Timeout ?? DefaultTimeout);

        try
        {
            var client = httpClientFactory.CreateClient(ResourceHealthProbeService.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode < 400
                ? ResourceHealthStatus.Healthy
                : ResourceHealthStatus.Unhealthy;

            return new ResourceHealthCheckResult(
                check,
                status,
                $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim(),
                uri,
                ResourceHealthCheckOutcome.Responded);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unhealthy,
                "Timed out",
                uri,
                ResourceHealthCheckOutcome.NoResponse);
        }
        catch (HttpRequestException exception)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unhealthy,
                exception.Message,
                uri,
                ResourceHealthCheckOutcome.NoResponse);
        }
    }

    private static Uri? ResolveCheckUri(Resource resource, ResourceHttpProbeSource source)
    {
        if (Uri.TryCreate(source.Path, UriKind.Absolute, out var absolute) &&
            IsHttpScheme(absolute.Scheme))
        {
            return absolute;
        }

        var endpoint = ResolveEndpoint(resource, source);
        if (endpoint is null ||
            !resource.TryGetResolvedEndpointUri(endpoint, out var endpointUri) ||
            !IsHttpScheme(endpointUri.Scheme))
        {
            return null;
        }

        var path = string.IsNullOrWhiteSpace(source.Path) ? "/" : source.Path.Trim();
        return Uri.TryCreate(endpointUri, path, out var checkUri)
            ? checkUri
            : null;
    }

    private static ResourceEndpoint? ResolveEndpoint(Resource resource, ResourceHttpProbeSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.EndpointName))
        {
            return resource.Endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Name, source.EndpointName, StringComparison.OrdinalIgnoreCase));
        }

        return resource.Endpoints.FirstOrDefault(endpoint => IsHttpScheme(endpoint.Protocol)) ??
            resource.Endpoints.FirstOrDefault(endpoint => HasResolvableEndpointAddress(resource, endpoint));
    }

    private static bool HasResolvableEndpointAddress(Resource resource, ResourceEndpoint endpoint) =>
        !string.IsNullOrWhiteSpace(resource.GetEndpointNetworkAddress(endpoint.Name));

    private static bool IsHttpScheme(string? scheme) =>
        string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase);
}
