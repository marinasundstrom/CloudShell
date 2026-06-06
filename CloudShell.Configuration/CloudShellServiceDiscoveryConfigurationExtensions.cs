using Microsoft.Extensions.Configuration;

namespace CloudShell.Configuration;

public static class CloudShellServiceDiscoveryConfigurationExtensions
{
    public static string? GetCloudShellServiceDiscoveryEndpoint(
        this IConfiguration configuration,
        string providerName,
        string? endpointName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var serviceSection = configuration
            .GetSection("services")
            .GetSection(providerName);
        if (!serviceSection.Exists())
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(endpointName))
        {
            return GetFirstEndpointValue(serviceSection.GetSection(endpointName));
        }

        return serviceSection
            .GetChildren()
            .Select(GetFirstEndpointValue)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public static Uri? GetResourceUri(
        this IConfiguration configuration,
        string providerName,
        string? endpointName = null) =>
        Uri.TryCreate(
            configuration.GetCloudShellServiceDiscoveryEndpoint(providerName, endpointName),
            UriKind.Absolute,
            out var uri)
            ? uri
            : null;

    private static string? GetFirstEndpointValue(IConfigurationSection endpointSection)
    {
        if (!endpointSection.Exists())
        {
            return null;
        }

        return endpointSection.Value ??
            endpointSection
                .GetChildren()
                .OrderBy(child => int.TryParse(child.Key, out var index) ? index : int.MaxValue)
                .ThenBy(child => child.Key, StringComparer.OrdinalIgnoreCase)
                .Select(child => child.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
