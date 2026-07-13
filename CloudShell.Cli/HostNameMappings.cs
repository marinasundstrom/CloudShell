using System.Net;

namespace CloudShell.Cli;

internal sealed class HostNameMappings
{
    private const string BeginMarker = "# BEGIN CloudShell local hostnames";
    private const string EndMarker = "# END CloudShell local hostnames";
    private readonly HostNameMappingPlatform _platform;

    public HostNameMappings()
        : this(HostNameMappingPlatform.Current)
    {
    }

    internal HostNameMappings(HostNameMappingPlatform platform)
    {
        _platform = platform;
    }

    public HostNameMappingPlan PlanAdd(string hostName, string address, string? hostsFile)
    {
        var normalizedHostName = NormalizeHostName(hostName);
        var normalizedAddress = NormalizeAddress(address);
        return new HostNameMappingPlan(
            ResolveHostsFile(hostsFile),
            normalizedHostName,
            normalizedAddress,
            Add: true);
    }

    public HostNameMappingPlan PlanRemove(string hostName, string? hostsFile) =>
        new(
            ResolveHostsFile(hostsFile),
            NormalizeHostName(hostName),
            Address: null,
            Add: false);

    public async Task ApplyAsync(
        HostNameMappingPlan plan,
        CancellationToken cancellationToken = default)
    {
        var entries = await ReadEntriesAsync(plan.HostsFile, cancellationToken);
        entries.RemoveAll(entry => string.Equals(
            entry.HostName,
            plan.HostName,
            StringComparison.OrdinalIgnoreCase));
        if (plan.Add)
        {
            entries.Add(new HostsEntry(plan.Address!, plan.HostName));
        }

        entries = entries
            .OrderBy(entry => entry.HostName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await WriteEntriesAsync(plan.HostsFile, entries, cancellationToken);
    }

    private static async Task<List<HostsEntry>> ReadEntriesAsync(
        string hostsFile,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(hostsFile))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(hostsFile, cancellationToken);
        var block = GetManagedBlock(lines);
        if (block is null)
        {
            return [];
        }

        var entries = new List<HostsEntry>();
        foreach (var line in block)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                entries.Add(new HostsEntry(parts[0], parts[1]));
            }
        }

        return entries;
    }

    private static async Task WriteEntriesAsync(
        string hostsFile,
        IReadOnlyList<HostsEntry> entries,
        CancellationToken cancellationToken)
    {
        var existing = File.Exists(hostsFile)
            ? await File.ReadAllLinesAsync(hostsFile, cancellationToken)
            : [];
        var preserved = RemoveManagedBlock(existing);
        var output = new List<string>(preserved);
        if (entries.Count != 0)
        {
            if (output.Count != 0 && !string.IsNullOrWhiteSpace(output[^1]))
            {
                output.Add(string.Empty);
            }

            output.Add(BeginMarker);
            output.AddRange(entries.Select(entry => $"{entry.Address} {entry.HostName}"));
            output.Add(EndMarker);
        }

        var directory = Path.GetDirectoryName(hostsFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllLinesAsync(hostsFile, output, cancellationToken);
    }

    private static IReadOnlyList<string>? GetManagedBlock(IReadOnlyList<string> lines)
    {
        var begin = Array.FindIndex(lines.ToArray(), line =>
            string.Equals(line.Trim(), BeginMarker, StringComparison.Ordinal));
        if (begin < 0)
        {
            return null;
        }

        var end = Array.FindIndex(lines.ToArray(), begin + 1, line =>
            string.Equals(line.Trim(), EndMarker, StringComparison.Ordinal));
        if (end < 0)
        {
            throw new InvalidOperationException(
                $"The hosts file contains '{BeginMarker}' without '{EndMarker}'.");
        }

        return lines.Skip(begin + 1).Take(end - begin - 1).ToArray();
    }

    private static IReadOnlyList<string> RemoveManagedBlock(IReadOnlyList<string> lines)
    {
        var begin = Array.FindIndex(lines.ToArray(), line =>
            string.Equals(line.Trim(), BeginMarker, StringComparison.Ordinal));
        if (begin < 0)
        {
            return lines;
        }

        var end = Array.FindIndex(lines.ToArray(), begin + 1, line =>
            string.Equals(line.Trim(), EndMarker, StringComparison.Ordinal));
        if (end < 0)
        {
            throw new InvalidOperationException(
                $"The hosts file contains '{BeginMarker}' without '{EndMarker}'.");
        }

        return lines
            .Take(begin)
            .Concat(lines.Skip(end + 1))
            .TrimTrailingEmptyLines()
            .ToArray();
    }

    private string ResolveHostsFile(string? hostsFile)
    {
        if (!string.IsNullOrWhiteSpace(hostsFile))
        {
            return Path.GetFullPath(hostsFile);
        }

        return _platform.GetDefaultHostsFile();
    }

    private static string NormalizeHostName(string hostName)
    {
        var normalized = hostName.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains('*', StringComparison.Ordinal) ||
            normalized.Contains(' ', StringComparison.Ordinal))
        {
            throw new CliUsageException($"Host name '{hostName}' is not a supported local host name.");
        }

        return normalized;
    }

    private static string NormalizeAddress(string address)
    {
        if (!IPAddress.TryParse(address.Trim(), out var parsed))
        {
            throw new CliUsageException($"Address '{address}' is not a valid IP address.");
        }

        return parsed.ToString();
    }

    private sealed record HostsEntry(string Address, string HostName);
}

internal sealed record HostNameMappingPlan(
    string HostsFile,
    string HostName,
    string? Address,
    bool Add);

internal sealed record HostNameMappingPlatform(bool IsWindows, string? SystemDirectory)
{
    public static HostNameMappingPlatform Current =>
        new(
            OperatingSystem.IsWindows(),
            Environment.GetFolderPath(Environment.SpecialFolder.System));

    public string GetDefaultHostsFile()
    {
        if (!IsWindows)
        {
            return "/etc/hosts";
        }

        if (string.IsNullOrWhiteSpace(SystemDirectory))
        {
            throw new InvalidOperationException(
                "The Windows system directory could not be resolved.");
        }

        return Path.Combine(SystemDirectory, "drivers", "etc", "hosts");
    }
}

internal static class EnumerableExtensions
{
    public static IEnumerable<string> TrimTrailingEmptyLines(this IEnumerable<string> lines)
    {
        var items = lines.ToList();
        while (items.Count > 0 && string.IsNullOrWhiteSpace(items[^1]))
        {
            items.RemoveAt(items.Count - 1);
        }

        return items;
    }
}
