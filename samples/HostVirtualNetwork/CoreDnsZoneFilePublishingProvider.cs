using System.Globalization;
using System.Net;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.HostVirtualNetwork;

public sealed class CoreDnsZoneFilePublishingOptions
{
    public string OutputDirectory { get; set; } = Path.Combine("Data", "coredns");

    public string CoreFileName { get; set; } = "Corefile";

    public string HostsFileName { get; set; } = "cloudshell.hosts";

    public int DnsPort { get; set; } = 1053;
}

public sealed class CoreDnsZoneFilePublishingProvider(
    CoreDnsZoneFilePublishingOptions options) : INamePublishingProvider
{
    public const string ProviderNameValue = "coredns-zone-file";

    public string ProviderName => ProviderNameValue;

    public bool CanPublish(DnsNamePublishingContext context) =>
        string.Equals(context.Definition.Provider, ProviderName, StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceProcedureResult> ReconcileAsync(
        DnsNamePublishingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!CanPublish(context))
        {
            throw new InvalidOperationException(
                $"DNS zone resource '{context.Definition.Id}' is not configured for provider '{ProviderName}'.");
        }

        var entries = context.Mappings
            .Select(CreateEntry)
            .OrderBy(entry => entry.HostName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var hostsPath = Path.Combine(outputDirectory, options.HostsFileName);
        var coreFilePath = Path.Combine(outputDirectory, options.CoreFileName);
        await File.WriteAllLinesAsync(
            hostsPath,
            entries.Select(entry => $"{entry.IPAddress} {entry.HostName}"),
            cancellationToken);
        await File.WriteAllTextAsync(
            coreFilePath,
            CreateCoreFile(hostsPath),
            cancellationToken);

        return ResourceProcedureResult.Completed(
            $"Published {entries.Length.ToString(CultureInfo.InvariantCulture)} CoreDNS host mapping(s) to '{hostsPath}'.");
    }

    private static CoreDnsHostEntry CreateEntry(
        DnsNameMappingResolution resolution)
    {
        var hostName = NormalizeHostName(resolution.Mapping.HostName, resolution.Mapping.Id);
        var endpoint = resolution.TargetEndpoint
            ?? throw new InvalidOperationException(
                $"Name mapping '{resolution.Mapping.Id}' must target a specific endpoint.");
        var endpointMapping = resolution.TargetEndpointNetworkMapping
            ?? throw new InvalidOperationException(
                $"Name mapping '{resolution.Mapping.Id}' target endpoint '{endpoint.Name}' does not have a topology-specific endpoint mapping.");
        if (!endpointMapping.TryGetUri(out var endpointUri))
        {
            throw new InvalidOperationException(
                $"Name mapping '{resolution.Mapping.Id}' target endpoint '{endpoint.Name}' must use an absolute address.");
        }

        var endpointHost = endpointUri.Host.Trim('[', ']');
        if (!IPAddress.TryParse(endpointHost, out var ipAddress))
        {
            throw new InvalidOperationException(
                $"Name mapping '{resolution.Mapping.Id}' target endpoint '{endpoint.Name}' host '{endpointHost}' is not an IP address.");
        }

        return new(ipAddress.ToString(), hostName);
    }

    private string CreateCoreFile(string hostsPath) =>
        $$"""
        .:{{options.DnsPort.ToString(CultureInfo.InvariantCulture)}} {
            hosts {{hostsPath}} {
                ttl 30
                fallthrough
            }
            forward . /etc/resolv.conf
        }
        """;

    private static string NormalizeHostName(string hostName, string mappingId)
    {
        var normalized = hostName.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(
                $"Name mapping '{mappingId}' host name is required.");
        }

        return normalized;
    }

    private sealed record CoreDnsHostEntry(
        string IPAddress,
        string HostName);
}
