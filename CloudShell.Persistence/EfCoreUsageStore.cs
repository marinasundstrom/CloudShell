using CloudShell.Abstractions.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CloudShell.Persistence;

public sealed class EfCoreUsageStore(
    IDbContextFactory<CloudShellDbContext> contextFactory,
    IOptions<UsageOptions> options) : IUsageStore
{
    private const int MaximumRetainedSamplesPerResource = 500_000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<UsageSample> GetSamples(
        string? resourceId = null,
        string? usageName = null,
        int maxSamples = 200,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        using var context = contextFactory.CreateDbContext();
        return QuerySamples(context, resourceId, usageName, from, to)
            .OrderByDescending(sample => sample.Timestamp)
            .ThenByDescending(sample => sample.Id)
            .Take(Math.Clamp(maxSamples, 1, GetRetainedSamplesPerResource()))
            .AsEnumerable()
            .Select(ToSample)
            .ToArray();
    }

    public IReadOnlyList<UsageStatistic> GetStatistics(
        string? resourceId = null,
        string? usageName = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int maxStatistics = 200)
    {
        using var context = contextFactory.CreateDbContext();
        return QuerySamples(context, resourceId, usageName, from, to)
            .AsEnumerable()
            .GroupBy(sample => new UsageStatisticKey(
                sample.ResourceId.Trim().ToUpperInvariant(),
                sample.Name.Trim().ToUpperInvariant(),
                sample.Unit))
            .Select(group =>
            {
                var ordered = group.OrderBy(sample => sample.Timestamp).ThenBy(sample => sample.Id).ToArray();
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
            .Take(Math.Clamp(maxStatistics, 1, GetRetainedSamplesPerResource()))
            .ToArray();
    }

    public void AddSamples(IEnumerable<UsageSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var validSamples = samples
            .Where(sample =>
                !string.IsNullOrWhiteSpace(sample.Name) &&
                !string.IsNullOrWhiteSpace(sample.ResourceId))
            .ToArray();
        if (validSamples.Length == 0)
        {
            return;
        }

        using var context = contextFactory.CreateDbContext();
        context.UsageSamples.AddRange(validSamples.Select(ToEntity));
        context.SaveChanges();

        foreach (var resourceId in validSamples
            .Select(sample => sample.ResourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            PruneSamples(context, resourceId, GetRetainedSamplesPerResource());
        }
    }

    private static IQueryable<UsageSampleEntity> QuerySamples(
        CloudShellDbContext context,
        string? resourceId,
        string? usageName,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        IQueryable<UsageSampleEntity> query = context.UsageSamples.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            query = query.Where(sample => sample.ResourceId == resourceId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(usageName))
        {
            query = query.Where(sample => sample.Name == usageName.Trim());
        }

        if (from is not null)
        {
            query = query.Where(sample => sample.Timestamp >= from);
        }

        if (to is not null)
        {
            query = query.Where(sample => sample.Timestamp <= to);
        }

        return query;
    }

    private void PruneSamples(
        CloudShellDbContext context,
        string resourceId,
        int retainedSamples)
    {
        var staleIds = context.UsageSamples
            .AsNoTracking()
            .Where(sample => sample.ResourceId == resourceId.Trim())
            .OrderByDescending(sample => sample.Timestamp)
            .ThenByDescending(sample => sample.Id)
            .Skip(retainedSamples)
            .Select(sample => sample.Id)
            .ToArray();

        if (staleIds.Length == 0)
        {
            return;
        }

        context.UsageSamples
            .Where(sample => staleIds.Contains(sample.Id))
            .ExecuteDelete();
    }

    private int GetRetainedSamplesPerResource() =>
        Math.Clamp(
            options.Value.RetainedSamplesPerResource <= 0
                ? 1
                : options.Value.RetainedSamplesPerResource,
            1,
            MaximumRetainedSamplesPerResource);

    private static UsageSampleEntity ToEntity(UsageSample sample) =>
        new()
        {
            Name = sample.Name,
            ResourceId = sample.ResourceId,
            Value = sample.Value,
            Timestamp = sample.Timestamp,
            Unit = sample.Unit,
            AttributesJson = JsonSerializer.Serialize(sample.UsageAttributes, SerializerOptions)
        };

    private static UsageSample ToSample(UsageSampleEntity entity) =>
        new(
            entity.Name,
            entity.ResourceId,
            entity.Value,
            entity.Timestamp,
            entity.Unit,
            DeserializeAttributes(entity.AttributesJson));

    private static IReadOnlyDictionary<string, string> DeserializeAttributes(string json)
    {
        var attributes = JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(
                json,
                SerializerOptions) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record UsageStatisticKey(
        string ResourceId,
        string Name,
        string? Unit);
}
