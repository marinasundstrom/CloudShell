using CloudShell.Abstractions.Observability;
using System.Globalization;

namespace CloudShell.Providers.Applications;

public static class LocalProcessMonitoringMetrics
{
    public static IReadOnlyList<ResourceMetricSample> CreateMetricSamples(
        LocalProcessMonitoringSnapshot snapshot,
        string processDescription = "local process")
    {
        var samples = new List<ResourceMetricSample>
        {
            new(
                "resource.cpu.usage",
                snapshot.CpuUsagePercent,
                "%",
                snapshot.Timestamp,
                "CPU usage",
                $"Current CPU usage sampled from the {processDescription}."),
            new(
                "resource.cpu.total",
                snapshot.TotalProcessorTime.TotalSeconds,
                "seconds",
                snapshot.Timestamp,
                "CPU time",
                $"Total processor time consumed by the {processDescription}."),
            new(
                "resource.memory.workingSet",
                snapshot.WorkingSetBytes,
                "bytes",
                snapshot.Timestamp,
                "Working set",
                $"Current working set memory used by the {processDescription}."),
            new(
                "resource.memory.private",
                snapshot.PrivateMemoryBytes,
                "bytes",
                snapshot.Timestamp,
                "Private memory",
                $"Current private memory used by the {processDescription}."),
            new(
                "resource.process.threads",
                snapshot.ThreadCount,
                "count",
                snapshot.Timestamp,
                "Thread count",
                $"Current thread count reported for the {processDescription}."),
            new(
                "resource.process.count",
                1,
                "count",
                snapshot.Timestamp,
                "Process count",
                $"Current process count for this {processDescription}.",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["process.id"] = snapshot.ProcessId.ToString(CultureInfo.InvariantCulture)
                })
        };

        if (snapshot.StartedAt is { } startedAt)
        {
            samples.Add(new ResourceMetricSample(
                "resource.process.uptime",
                Math.Max(0, (snapshot.Timestamp - startedAt).TotalSeconds),
                "seconds",
                snapshot.Timestamp,
                "Process uptime",
                $"Seconds since the {processDescription} started."));
        }

        return samples;
    }
}
