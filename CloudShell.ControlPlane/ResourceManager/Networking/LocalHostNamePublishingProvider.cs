using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Platform;

namespace CloudShell.ControlPlane.ResourceManager.Networking;

public sealed class LocalHostNamePublishingProvider(
    PlatformResourceOptions options,
    ILocalHostNameResolverCacheRefresher? resolverCacheRefresher = null) :
    INamePublishingProvider,
    INamePublishingActionAvailabilityProvider,
    INamePublishingObservationAttributeProvider
{
    private const string BeginMarker = "# BEGIN CloudShell local hostnames";
    private const string EndMarker = "# END CloudShell local hostnames";
    private const string HostsFileTargetSystem = "System";
    private const string HostsFileTargetCustom = "Custom";
    private const string ResolverRefreshDisabled = "Disabled";
    private const string ResolverRefreshFailed = "Failed";
    private const string ResolverRefreshNotAttempted = "NotAttempted";
    private const string ResolverRefreshSkipped = "Skipped";
    private const string ResolverRefreshSucceeded = "Succeeded";
    private const string ResolverRefreshUnavailable = "Unavailable";

    public const string DefaultProviderName = "local-hostnames";

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> observationAttributes =
        new(StringComparer.OrdinalIgnoreCase);

    public string ProviderName => options.LocalHostNameProviderName;

    public bool CanPublish(DnsNamePublishingContext context) =>
        string.Equals(context.Definition.Provider, ProviderName, StringComparison.OrdinalIgnoreCase);

    public string? GetUnavailableReason(DnsNamePublishingContext context)
    {
        foreach (var mapping in context.Mappings)
        {
            try
            {
                CreateHostsEntry(mapping);
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }
        }

        return null;
    }

    public IReadOnlyDictionary<string, string> GetObservationAttributes(DnsNamePublishingContext context) =>
        observationAttributes.TryGetValue(context.Definition.Id, out var attributes)
            ? attributes
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public async Task<ResourceProcedureResult> ReconcileAsync(
        DnsNamePublishingContext context,
        CancellationToken cancellationToken = default)
    {
        if (!CanPublish(context))
        {
            throw new InvalidOperationException(
                $"DNS zone resource '{context.Definition.Id}' is not configured for provider '{ProviderName}'.");
        }

        observationAttributes.TryRemove(context.Definition.Id, out _);
        var entries = context.Mappings
            .Select(CreateHostsEntry)
            .OrderBy(entry => entry.HostName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hostsFile = ResolveHostsFile();
        try
        {
            await UpdateHostsFileAsync(hostsFile.Path, entries, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                $"Local host name mappings could not be written to '{hostsFile.Path}'. Configure '{nameof(PlatformResourceOptions.LocalHostNameHostsFilePath)}' or run the host with permission to update the selected hosts file.",
                exception);
        }

        var message =
            $"Published {entries.Length.ToString(CultureInfo.InvariantCulture)} local host name mapping(s) to '{hostsFile.Path}'.";
        var refresh = await GetResolverRefreshObservationAsync(hostsFile, cancellationToken).ConfigureAwait(false);
        message += " " + refresh.Message;
        if (entries.Any(entry => IsLocalDomain(entry.HostName)))
        {
            message += " Warning: .local host names may conflict with mDNS/Bonjour on the host network.";
        }

        observationAttributes[context.Definition.Id] = CreateObservationAttributes(hostsFile, refresh);
        return ResourceProcedureResult.Completed(message);
    }

    private static IReadOnlyDictionary<string, string> CreateObservationAttributes(
        HostsFileTarget hostsFile,
        ResolverRefreshObservation refresh) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.NameMappingLocalHostNamesHostsFilePath] = hostsFile.Path,
            [ResourceAttributeNames.NameMappingLocalHostNamesHostsFileTarget] =
                hostsFile.IsSystemHostsFile ? HostsFileTargetSystem : HostsFileTargetCustom,
            [ResourceAttributeNames.NameMappingLocalHostNamesResolverRefreshStatus] = refresh.Status,
            [ResourceAttributeNames.NameMappingLocalHostNamesResolverRefreshReason] = refresh.Message
        };

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
        if (!resolution.TargetResource.TryGetResolvedEndpointUri(endpoint, out var endpointUri))
        {
            throw new InvalidOperationException(
                $"Name mapping '{resolution.Mapping.Id}' target endpoint '{endpoint.Name}' must use a mapped absolute address with a host.");
        }

        var address = ResolveAddress(endpoint.Name, endpointUri, resolution.Mapping.Id);
        return new HostsEntry(address, hostName);
    }

    private string ResolveAddress(string endpointName, Uri endpointUri, string mappingId)
    {
        if (string.IsNullOrWhiteSpace(endpointUri.Host))
        {
            throw new InvalidOperationException(
                $"Name mapping '{mappingId}' target endpoint '{endpointName}' must use a mapped absolute address with a host.");
        }

        var host = endpointUri.Host.Trim('[', ']');
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
            $"Name mapping '{mappingId}' target endpoint '{endpointName}' host '{host}' is not a local or IP address that can be published through provider '{ProviderName}'.");
    }

    private async Task<ResolverRefreshObservation> GetResolverRefreshObservationAsync(
        HostsFileTarget hostsFile,
        CancellationToken cancellationToken)
    {
        if (options.LocalHostNameResolverRefreshMode == LocalHostNameResolverRefreshMode.Disabled)
        {
            return new ResolverRefreshObservation(
                ResolverRefreshDisabled,
                "Resolver cache refresh is disabled.");
        }

        if (!hostsFile.IsSystemHostsFile)
        {
            return new ResolverRefreshObservation(
                ResolverRefreshSkipped,
                "Resolver cache was not refreshed because a custom hosts-file target is configured.");
        }

        if (resolverCacheRefresher is null)
        {
            return new ResolverRefreshObservation(
                ResolverRefreshUnavailable,
                "Resolver cache was not refreshed because no refresh service is registered.");
        }

        var result = await resolverCacheRefresher.RefreshAsync(cancellationToken).ConfigureAwait(false);
        var status = result.Attempted
            ? result.Succeeded ? ResolverRefreshSucceeded : ResolverRefreshFailed
            : ResolverRefreshNotAttempted;
        return new ResolverRefreshObservation(status, result.Message);
    }

    private HostsFileTarget ResolveHostsFile()
    {
        if (!string.IsNullOrWhiteSpace(options.LocalHostNameHostsFilePath))
        {
            return new HostsFileTarget(Path.GetFullPath(options.LocalHostNameHostsFilePath), false);
        }

        if (OperatingSystem.IsWindows())
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return new HostsFileTarget(Path.Combine(system, "drivers", "etc", "hosts"), true);
        }

        return new HostsFileTarget("/etc/hosts", true);
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

    private sealed record HostsFileTarget(string Path, bool IsSystemHostsFile);

    private sealed record ResolverRefreshObservation(string Status, string Message);
}
