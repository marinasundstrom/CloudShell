using CloudShell.Abstractions.Usage;

namespace CloudShell.ControlPlane.Usage;

public sealed class InMemoryUsageStore : IUsageStore
{
    private const int MaxStoredSamples = 10_000;
    private readonly object _gate = new();
    private readonly List<UsageSample> _samples = [];

    public IReadOnlyList<UsageSample> GetSamples(
        string? resourceId = null,
        string? usageName = null,
        int maxSamples = 200,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        lock (_gate)
        {
            return QuerySamples(resourceId, usageName, from, to)
                .OrderByDescending(sample => sample.Timestamp)
                .Take(Math.Clamp(maxSamples, 1, MaxStoredSamples))
                .ToArray();
        }
    }

    public IReadOnlyList<UsageStatistic> GetStatistics(
        string? resourceId = null,
        string? usageName = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int maxStatistics = 200)
    {
        lock (_gate)
        {
            return QuerySamples(resourceId, usageName, from, to)
                .GroupBy(sample => new UsageStatisticKey(
                    sample.ResourceId.Trim().ToUpperInvariant(),
                    sample.Name.Trim().ToUpperInvariant(),
                    sample.Unit))
                .Select(group =>
                {
                    var ordered = group.OrderBy(sample => sample.Timestamp).ToArray();
                    var values = ordered.Select(sample => sample.Value).ToArray();
                    return new UsageStatistic(
                        ordered[0].ResourceId,
                        ordered[0].Name,
                        group.Key.Unit,
                        ordered.Length,
                        values.Sum(),
                        values.Average(),
                        values.Min(),
                        values.Max(),
                        ordered[^1].Value,
                        ordered[0].Timestamp,
                        ordered[^1].Timestamp);
                })
                .OrderBy(statistic => statistic.ResourceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(statistic => statistic.Name, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(maxStatistics, 1, MaxStoredSamples))
                .ToArray();
        }
    }

    public void AddSamples(IEnumerable<UsageSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        lock (_gate)
        {
            _samples.AddRange(samples.Where(sample =>
                !string.IsNullOrWhiteSpace(sample.Name) &&
                !string.IsNullOrWhiteSpace(sample.ResourceId)));

            if (_samples.Count > MaxStoredSamples)
            {
                _samples.RemoveRange(0, _samples.Count - MaxStoredSamples);
            }
        }
    }

    private IEnumerable<UsageSample> QuerySamples(
        string? resourceId,
        string? usageName,
        DateTimeOffset? from,
        DateTimeOffset? to) =>
        _samples
            .Where(sample => string.IsNullOrWhiteSpace(resourceId) ||
                string.Equals(sample.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            .Where(sample => string.IsNullOrWhiteSpace(usageName) ||
                string.Equals(sample.Name, usageName, StringComparison.OrdinalIgnoreCase))
            .Where(sample => from is null || sample.Timestamp >= from)
            .Where(sample => to is null || sample.Timestamp <= to);

    private sealed record UsageStatisticKey(
        string ResourceId,
        string Name,
        string? Unit);
}
