using System.Globalization;
using System.Text;

namespace CloudShell.Providers.Applications;

internal static class ApplicationResourceNames
{
    public static string CreateRuntimeContainerResourceId(string resourceId, int replica) =>
        $"runtime-container:{CreateStableIdentifier(resourceId)}:replica-{Math.Max(1, replica).ToString(CultureInfo.InvariantCulture)}";

    public static string CreateRuntimeNetworkAlias(
        string serviceName,
        string containerName,
        int replicaOrdinal)
    {
        const int maxDnsLabelLength = 63;
        if (containerName.Length <= maxDnsLabelLength)
        {
            return containerName;
        }

        var normalizedServiceName = CreateStableIdentifier(serviceName);
        if (normalizedServiceName.Length > 40)
        {
            normalizedServiceName = normalizedServiceName[..40].Trim('-');
        }

        var hash = ApplicationResourceHash.StableHash(containerName).ToString("x8", CultureInfo.InvariantCulture);
        var replica = Math.Max(1, replicaOrdinal).ToString(CultureInfo.InvariantCulture);
        return $"{normalizedServiceName}-r{replica}-{hash}";
    }

    public static string CreateStableIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var identifier = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(identifier) ? "cloudshell" : identifier;
    }
}
