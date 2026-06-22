using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceHealthProbeService(IEnumerable<IResourceProbeEvaluator> evaluators)
{
    public const string HttpClientName = CloudShellLogCategories.ResourceHealthProbes;
    public const string HttpClientLogCategory = CloudShellLogCategories.ResourceHealthProbeHttpClient;

    public async Task<IReadOnlyDictionary<string, ResourceHealthSummary>> CheckAsync(
        IReadOnlyList<Resource> resources,
        CancellationToken cancellationToken = default)
    {
        var probeable = resources
            .Where(resource => resource.ResourceHealthChecks.Count > 0)
            .ToArray();

        if (probeable.Length == 0)
        {
            return new Dictionary<string, ResourceHealthSummary>(StringComparer.OrdinalIgnoreCase);
        }

        var results = await Task.WhenAll(probeable.Select(resource => CheckResourceAsync(resource, cancellationToken)));
        return results.ToDictionary(
            result => result.ResourceId,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ResourceHealthSummary> CheckResourceAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        var checks = new List<ResourceHealthCheckResult>();
        foreach (var check in resource.ResourceHealthChecks)
        {
            var evaluator = evaluators.FirstOrDefault(candidate => candidate.CanEvaluate(resource, check));
            checks.Add(evaluator is null
                ? CreateUnsupportedSourceResult(check)
                : await evaluator.EvaluateAsync(resource, check, cancellationToken));
        }

        var status = checks.Any(check => check.Status == ResourceHealthStatus.Unhealthy)
            ? ResourceHealthStatus.Unhealthy
            : checks.Any(check => check.Status == ResourceHealthStatus.Unknown)
                ? ResourceHealthStatus.Unknown
                : ResourceHealthStatus.Healthy;

        return new ResourceHealthSummary(
            resource.Id,
            status,
            DateTimeOffset.UtcNow,
            checks);
    }

    private static ResourceHealthCheckResult CreateUnsupportedSourceResult(ResourceHealthCheck check) =>
        new(
            check,
            ResourceHealthStatus.Unknown,
            $"Unsupported probe source '{check.EffectiveSource.Kind}'",
            null);
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
                null);
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
                uri);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unhealthy,
                "Timed out",
                uri);
        }
        catch (HttpRequestException exception)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unhealthy,
                exception.Message,
                uri);
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
