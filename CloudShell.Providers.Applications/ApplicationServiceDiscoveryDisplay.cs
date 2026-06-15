using CloudShell.Abstractions.ResourceManager;
using System.Text.RegularExpressions;

namespace CloudShell.Providers.Applications;

internal static partial class ApplicationServiceDiscoveryDisplay
{
    public static IReadOnlyList<string> GetServiceNames(Resource resource) =>
        new[]
            {
                CreateConfigurationSegment(resource.Name),
                CreateConfigurationSegment(resource.Id)
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<string> GetEndpointKeys(ResourceEndpoint endpoint) =>
        new[]
            {
                CreateConfigurationSegment(endpoint.Name),
                CreateConfigurationSegment(endpoint.Protocol)
            }
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<ServiceDiscoveryEndpointBinding> GetEndpointBindings(Resource resource)
    {
        var serviceNames = GetServiceNames(resource);
        if (serviceNames.Count == 0)
        {
            return [];
        }

        return resource.Endpoints
            .Where(IsDiscoverableEndpoint)
            .SelectMany(endpoint => GetEndpointKeys(endpoint)
                .SelectMany(endpointKey => serviceNames.Select(serviceName =>
                    new ServiceDiscoveryEndpointBinding(
                        serviceName,
                        endpointKey,
                        endpoint.Address,
                        $"services__{serviceName}__{endpointKey}__0"))))
            .ToArray();
    }

    private static bool IsDiscoverableEndpoint(ResourceEndpoint endpoint) =>
        !string.IsNullOrWhiteSpace(endpoint.Address) &&
        !endpoint.Protocol.Equals("process", StringComparison.OrdinalIgnoreCase) &&
        !endpoint.Address.StartsWith("process://", StringComparison.OrdinalIgnoreCase);

    public static string CreateConfigurationSegment(string value) =>
        ServiceDiscoveryConfigurationSegmentPattern()
            .Replace(value.Trim().ToLowerInvariant(), "-")
            .Trim('-');

    [GeneratedRegex("[^a-z0-9_.-]+")]
    private static partial Regex ServiceDiscoveryConfigurationSegmentPattern();
}

internal sealed record ServiceDiscoveryEndpointBinding(
    string ServiceName,
    string EndpointKey,
    string Address,
    string EnvironmentVariableName);
