using System.Text.Json.Serialization;

namespace CloudShell.Abstractions.Usage;

public interface IUsageStore
{
    IReadOnlyList<UsageSample> GetSamples(
        string? resourceId = null,
        string? usageName = null,
        int maxSamples = 200,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null);

    IReadOnlyList<UsageStatistic> GetStatistics(
        string? resourceId = null,
        string? usageName = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int maxStatistics = 200);

    void AddSamples(IEnumerable<UsageSample> samples);
}

public sealed record UsageSample(
    string Name,
    string ResourceId,
    double Value,
    DateTimeOffset Timestamp,
    string? Unit = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> UsageAttributes =>
        Attributes ?? EmptyAttributes;

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record UsageQuery(
    string? ResourceId = null,
    string? UsageName = null,
    int MaxSamples = 200,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public sealed record UsageStatisticsQuery(
    string? ResourceId = null,
    string? UsageName = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int MaxStatistics = 200);

public sealed record UsageStatistic(
    string ResourceId,
    string Name,
    string? Unit,
    int Count,
    double Sum,
    double Average,
    double Min,
    double Max,
    double LatestValue,
    DateTimeOffset FirstTimestamp,
    DateTimeOffset LastTimestamp);
