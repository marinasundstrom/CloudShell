using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalContainerApplicationRuntimeMonitoringProvider(
    ILocalContainerApplicationCommandRunner commandRunner) : IResourceMonitoringProvider
{
    private const string ProviderDisplayName = "Container app local runtime";

    public bool CanMonitor(ResourceManagerResource resource) =>
        IsRuntimeContainerReplica(resource);

    public async Task<ResourceMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        ResourceManagerResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!CanMonitor(resource))
        {
            return null;
        }

        var timestamp = DateTimeOffset.UtcNow;
        if (resource.State != CloudShell.Abstractions.ResourceManager.ResourceState.Running)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Runtime replica metrics are available only while the replica is running.");
        }

        var containerName = GetAttribute(resource, ResourceAttributeNames.RuntimeContainerName);
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Runtime replica metrics require a projected runtime container name.");
        }

        var result = await commandRunner.RunAsync(
            "docker",
            ["stats", "--no-stream", "--format", "{{json .}}", containerName],
            cancellationToken,
            throwOnError: false);
        if (result.ExitCode != 0 ||
            !ContainerApplicationRuntimeMonitoringMetrics.TryParseStatsJson(
                result.Output,
                DateTimeOffset.UtcNow,
                out var snapshot))
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                string.IsNullOrWhiteSpace(result.Error)
                    ? "The container runtime did not return a stats snapshot for the runtime replica."
                    : result.Error.Trim());
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            snapshot.Timestamp,
            ContainerApplicationRuntimeMonitoringMetrics.CreateMetricSamples(snapshot),
            "Available",
            "Runtime replica container runtime metrics.");
    }

    private static bool IsRuntimeContainerReplica(ResourceManagerResource resource) =>
        string.Equals(resource.EffectiveTypeId, "runtime.container", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            GetAttribute(resource, ResourceAttributeNames.RuntimeKind),
            "containerReplica",
            StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(GetAttribute(resource, ResourceAttributeNames.RuntimeContainerName));

    private static string GetAttribute(ResourceManagerResource resource, string name) =>
        resource.ResourceAttributes.TryGetValue(name, out var value)
            ? value
            : string.Empty;
}

internal static partial class ContainerApplicationRuntimeMonitoringMetrics
{
    public static bool TryParseStatsJson(
        string json,
        DateTimeOffset timestamp,
        out ContainerRuntimeMonitoringSnapshot snapshot)
    {
        snapshot = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var name = GetString(root, "Name") ?? GetString(root, "Container") ?? string.Empty;
            var cpuPercent = ParsePercent(GetString(root, "CPUPerc"));
            var (memoryUsage, memoryLimit) = ParseBytePair(GetString(root, "MemUsage"));
            var (networkReceived, networkSent) = ParseBytePair(GetString(root, "NetIO"));
            var (blockRead, blockWrite) = ParseBytePair(GetString(root, "BlockIO"));
            var processCount = ParseDouble(GetString(root, "PIDs"));

            snapshot = new ContainerRuntimeMonitoringSnapshot(
                name,
                timestamp,
                cpuPercent,
                memoryUsage,
                memoryLimit,
                networkReceived,
                networkSent,
                blockRead,
                blockWrite,
                processCount);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static IReadOnlyList<ResourceMetricSample> CreateMetricSamples(
        ContainerRuntimeMonitoringSnapshot snapshot)
    {
        var samples = new List<ResourceMetricSample>
        {
            new(
                "resource.cpu.usage",
                snapshot.CpuUsagePercent,
                "%",
                snapshot.Timestamp,
                "CPU usage",
                "Current container CPU usage reported by the container host."),
            new(
                "resource.memory.usage",
                snapshot.MemoryUsageBytes,
                "bytes",
                snapshot.Timestamp,
                "Memory usage",
                "Current container memory usage reported by the container host.")
        };

        if (snapshot.MemoryLimitBytes > 0)
        {
            samples.Add(new ResourceMetricSample(
                "resource.memory.limit",
                snapshot.MemoryLimitBytes,
                "bytes",
                snapshot.Timestamp,
                "Memory limit",
                "Current container memory limit reported by the container host."));
            samples.Add(new ResourceMetricSample(
                "resource.memory.usagePercent",
                snapshot.MemoryUsageBytes / snapshot.MemoryLimitBytes * 100,
                "%",
                snapshot.Timestamp,
                "Memory usage percent",
                "Current container memory usage as a percentage of the reported memory limit."));
        }

        samples.Add(new ResourceMetricSample(
            "resource.network.rxBytes",
            snapshot.NetworkReceivedBytes,
            "bytes",
            snapshot.Timestamp,
            "Network received",
            "Total container network bytes received."));
        samples.Add(new ResourceMetricSample(
            "resource.network.txBytes",
            snapshot.NetworkSentBytes,
            "bytes",
            snapshot.Timestamp,
            "Network sent",
            "Total container network bytes sent."));
        samples.Add(new ResourceMetricSample(
            "resource.block.readBytes",
            snapshot.BlockReadBytes,
            "bytes",
            snapshot.Timestamp,
            "Block read",
            "Total bytes read from block devices by the container."));
        samples.Add(new ResourceMetricSample(
            "resource.block.writeBytes",
            snapshot.BlockWrittenBytes,
            "bytes",
            snapshot.Timestamp,
            "Block written",
            "Total bytes written to block devices by the container."));

        if (snapshot.ProcessCount >= 0)
        {
            samples.Add(new ResourceMetricSample(
                "resource.process.count",
                snapshot.ProcessCount,
                "count",
                snapshot.Timestamp,
                "Process count",
                "Current process count reported by the container host."));
        }

        return samples;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return ParseDouble(value.Trim().TrimEnd('%'));
    }

    private static (double First, double Second) ParseBytePair(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (0, 0);
        }

        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (0, 0),
            1 => (ParseBytes(parts[0]), 0),
            _ => (ParseBytes(parts[0]), ParseBytes(parts[1]))
        };
    }

    private static double ParseBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var match = ByteValuePattern().Match(value.Trim());
        if (!match.Success)
        {
            return 0;
        }

        var number = ParseDouble(match.Groups["value"].Value);
        var unit = match.Groups["unit"].Value;
        return number * GetByteMultiplier(unit);
    }

    private static double ParseDouble(string? value) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;

    private static double GetByteMultiplier(string unit) =>
        unit.ToLowerInvariant() switch
        {
            "kb" => 1_000,
            "mb" => 1_000_000,
            "gb" => 1_000_000_000,
            "tb" => 1_000_000_000_000,
            "kib" => 1024,
            "mib" => 1024 * 1024,
            "gib" => 1024 * 1024 * 1024,
            "tib" => 1024d * 1024 * 1024 * 1024,
            _ => 1
        };

    [GeneratedRegex(@"^(?<value>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>[A-Za-z]+)?$")]
    private static partial Regex ByteValuePattern();
}

internal readonly record struct ContainerRuntimeMonitoringSnapshot(
    string ContainerName,
    DateTimeOffset Timestamp,
    double CpuUsagePercent,
    double MemoryUsageBytes,
    double MemoryLimitBytes,
    double NetworkReceivedBytes,
    double NetworkSentBytes,
    double BlockReadBytes,
    double BlockWrittenBytes,
    double ProcessCount);
