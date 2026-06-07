using Microsoft.Extensions.Configuration;

namespace CloudShell.Configuration;

public static class CloudShellServiceDiscoveryConfigurationExtensions
{
    public static Uri? GetResourceUri(
        this IConfiguration configuration,
        string resourceId,
        string endpointName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);

        var serviceSection = configuration
            .GetSection("services")
            .GetSection(resourceId);
        if (!serviceSection.Exists())
        {
            return null;
        }

        return Uri.TryCreate(
            GetFirstEndpointValue(serviceSection.GetSection(endpointName)),
            UriKind.Absolute,
            out var uri)
            ? uri
            : null;
    }

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
