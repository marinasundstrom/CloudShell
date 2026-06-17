using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public sealed class ResourceHealthCheckService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly HttpClient Client = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

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
            checks.Add(await CheckEndpointAsync(resource, check, cancellationToken));
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

    private async Task<ResourceHealthCheckResult> CheckEndpointAsync(
        Resource resource,
        ResourceHealthCheck check,
        CancellationToken cancellationToken)
    {
        var uri = ResolveCheckUri(resource, check);
        if (uri is null)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unknown,
                "No matching HTTP endpoint",
                null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(check.Timeout ?? DefaultTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
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

    private static Uri? ResolveCheckUri(Resource resource, ResourceHealthCheck check)
    {
        if (Uri.TryCreate(check.Path, UriKind.Absolute, out var absolute) &&
            IsHttpScheme(absolute.Scheme))
        {
            return absolute;
        }

        var endpoint = ResolveEndpoint(resource, check);
        if (endpoint is null ||
            !resource.TryGetResolvedEndpointUri(endpoint, out var endpointUri) ||
            !IsHttpScheme(endpointUri.Scheme))
        {
            return null;
        }

        var path = string.IsNullOrWhiteSpace(check.Path) ? "/" : check.Path.Trim();
        return Uri.TryCreate(endpointUri, path, out var checkUri)
            ? checkUri
            : null;
    }

    private static ResourceEndpoint? ResolveEndpoint(Resource resource, ResourceHealthCheck check)
    {
        if (!string.IsNullOrWhiteSpace(check.EndpointName))
        {
            return resource.Endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Name, check.EndpointName, StringComparison.OrdinalIgnoreCase));
        }

        return resource.Endpoints.FirstOrDefault(endpoint => IsHttpScheme(endpoint.Protocol)) ??
            resource.Endpoints.FirstOrDefault(endpoint => HasResolvableEndpointAddress(resource, endpoint));
    }

    private static bool HasResolvableEndpointAddress(Resource resource, ResourceEndpoint endpoint)
    {
        if (!string.IsNullOrWhiteSpace(resource.GetEndpointNetworkAddress(endpoint.Name)))
        {
            return true;
        }

        return endpoint.TryGetUri(out var uri) &&
            IsHttpScheme(uri.Scheme);
    }

    private static bool IsHttpScheme(string? scheme) =>
        string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase);
}

public enum ResourceHealthStatus
{
    Healthy,
    Unhealthy,
    Unknown
}

public sealed record ResourceHealthSummary(
    string ResourceId,
    ResourceHealthStatus Status,
    DateTimeOffset CheckedAt,
    IReadOnlyList<ResourceHealthCheckResult> Checks);

public sealed record ResourceHealthCheckResult(
    ResourceHealthCheck Check,
    ResourceHealthStatus Status,
    string Detail,
    Uri? Uri);
