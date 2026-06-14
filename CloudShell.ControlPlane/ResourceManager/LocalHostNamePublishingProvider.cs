using System.Globalization;
using System.Net;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class LocalHostNamePublishingProvider(
    PlatformResourceOptions options) : INamePublishingProvider
{
    private const string BeginMarker = "# BEGIN CloudShell local hostnames";
    private const string EndMarker = "# END CloudShell local hostnames";

    public const string DefaultProviderName = "local-hostnames";

    public string ProviderName => options.LocalHostNameProviderName;

    public bool CanPublish(DnsNamePublishingContext context) =>
        string.Equals(context.Definition.Provider, ProviderName, StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceProcedureResult> ReconcileAsync(
        DnsNamePublishingContext context,
        CancellationToken cancellationToken = default)
    {
        if (!CanPublish(context))
        {
            throw new InvalidOperationException(
                $"DNS zone resource '{context.Definition.Id}' is not configured for provider '{ProviderName}'.");
        }

        var entries = context.Mappings
            .Select(CreateHostsEntry)
            .OrderBy(entry => entry.HostName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var path = ResolveHostsFilePath();
        try
        {
            await UpdateHostsFileAsync(path, entries, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                $"Local host name mappings could not be written to '{path}'. Configure '{nameof(PlatformResourceOptions.LocalHostNameHostsFilePath)}' or run the host with permission to update the selected hosts file.",
                exception);
        }

        var message =
            $"Published {entries.Length.ToString(CultureInfo.InvariantCulture)} local host name mapping(s) to '{path}'.";
        if (entries.Any(entry => IsLocalDomain(entry.HostName)))
        {
            message += " Warning: .local host names may conflict with mDNS/Bonjour on the host network.";
        }

        return ResourceProcedureResult.Completed(message);
    }

    private HostsEntry CreateHostsEntry(DnsNameMappingResolution resolution)
    {
        var hostName = NormalizeHostName(resolution.Mapping.HostName, resolution.Mapping.Id);
        if (hostName.Contains('*', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Name mapping '{resolution.Mapping.Id}' uses wildcard host '{resolution.Mapping.HostName}', but provider '{ProviderName}' only supports exact host mappings.");
        }

        var endpoint = resolution.TargetEndpoint
            ?? throw new InvalidOperationException(
                $"Name mapping '{resolution.Mapping.Id}' must target a specific endpoint to be published by provider '{ProviderName}'.");
        var address = ResolveAddress(endpoint, resolution.Mapping.Id);
        return new HostsEntry(address, hostName);
    }

    private string ResolveAddress(ResourceEndpoint endpoint, string mappingId)
    {
        if (!Uri.TryCreate(endpoint.Address, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException(
                $"Name mapping '{mappingId}' target endpoint '{endpoint.Name}' must use an absolute address with a host.");
        }

        var host = uri.Host.Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "0.0.0.0", StringComparison.Ordinal) ||
            string.Equals(host, "::", StringComparison.Ordinal))
        {
            return NormalizeAddress(options.LocalHostNameDefaultAddress);
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any) ||
                IPAddress.IsLoopback(address)
                    ? NormalizeAddress(options.LocalHostNameDefaultAddress)
                    : address.ToString();
        }

        throw new InvalidOperationException(
            $"Name mapping '{mappingId}' target endpoint '{endpoint.Name}' host '{host}' is not a local or IP address that can be published through provider '{ProviderName}'.");
    }

    private string ResolveHostsFilePath()
    {
        if (!string.IsNullOrWhiteSpace(options.LocalHostNameHostsFilePath))
        {
            return Path.GetFullPath(options.LocalHostNameHostsFilePath);
        }

        if (OperatingSystem.IsWindows())
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return Path.Combine(system, "drivers", "etc", "hosts");
        }

        return "/etc/hosts";
    }

    private static async Task UpdateHostsFileAsync(
        string path,
        IReadOnlyList<HostsEntry> entries,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var existing = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            : string.Empty;
        var withoutManagedBlock = RemoveManagedBlock(existing);
        var block = CreateManagedBlock(entries);
        var content = string.IsNullOrWhiteSpace(withoutManagedBlock)
            ? block
            : $"{withoutManagedBlock.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{block}";
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    private static string RemoveManagedBlock(string content)
    {
        var start = content.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return content;
        }

        var end = content.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return content[..start];
        }

        end += EndMarker.Length;
        if (end < content.Length && content[end] == '\r')
        {
            end++;
        }

        if (end < content.Length && content[end] == '\n')
        {
            end++;
        }

        return content.Remove(start, end - start);
    }

    private static string CreateManagedBlock(IReadOnlyList<HostsEntry> entries)
    {
        var lines = new List<string>
        {
            BeginMarker,
            "# Managed by CloudShell. Manual changes inside this block may be overwritten."
        };
        lines.AddRange(entries
            .GroupBy(entry => entry.HostName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(entry => $"{entry.Address} {entry.HostName}"));
        lines.Add(EndMarker);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

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

    private static string NormalizeAddress(string address)
    {
        if (IPAddress.TryParse(address, out var parsed))
        {
            return parsed.ToString();
        }

        throw new InvalidOperationException(
            $"Local host name default address '{address}' is not a valid IP address.");
    }

    private static bool IsLocalDomain(string hostName) =>
        hostName.Equals("local", StringComparison.OrdinalIgnoreCase) ||
        hostName.EndsWith(".local", StringComparison.OrdinalIgnoreCase);

    private sealed record HostsEntry(string Address, string HostName);
}
