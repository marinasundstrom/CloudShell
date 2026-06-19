using CloudShell.Abstractions.ResourceManager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CloudShell.Persistence;

public sealed class EfCoreResourceHealthStore(
    IDbContextFactory<CloudShellDbContext> contextFactory,
    IOptions<ResourceHealthOptions> options) : IResourceHealthStore
{
    private const int MaximumRetainedSnapshotsPerResource = 10_000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyDictionary<string, ResourceHealthSummary> GetLatest(
        IEnumerable<string>? resourceIds = null)
    {
        var requested = resourceIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var context = contextFactory.CreateDbContext();
        IQueryable<ResourceHealthSnapshotEntity> query = context.ResourceHealthSnapshots.AsNoTracking();
        if (requested is not null)
        {
            query = query.Where(snapshot => requested.Contains(snapshot.ResourceId));
        }

        return query
            .OrderByDescending(snapshot => snapshot.CheckedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .AsEnumerable()
            .GroupBy(snapshot => snapshot.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => ToSummary(group.First()))
            .ToDictionary(
                summary => summary.ResourceId,
                StringComparer.OrdinalIgnoreCase);
    }

    public ResourceHealthSummary? GetLatest(string resourceId)
    {
        using var context = contextFactory.CreateDbContext();
        var entity = context.ResourceHealthSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ResourceId == resourceId)
            .OrderByDescending(snapshot => snapshot.CheckedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .FirstOrDefault();

        return entity is null ? null : ToSummary(entity);
    }

    public IReadOnlyList<ResourceHealthSummary> GetSnapshots(
        string resourceId,
        int maxSnapshots = 100)
    {
        using var context = contextFactory.CreateDbContext();
        return context.ResourceHealthSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ResourceId == resourceId)
            .OrderByDescending(snapshot => snapshot.CheckedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .Take(Math.Clamp(maxSnapshots, 1, GetRetainedSnapshotsPerResource()))
            .AsEnumerable()
            .Select(ToSummary)
            .ToArray();
    }

    public void Add(ResourceHealthSummary summary)
    {
        var retainedSnapshots = GetRetainedSnapshotsPerResource();
        if (retainedSnapshots == 0)
        {
            return;
        }

        using var context = contextFactory.CreateDbContext();
        context.ResourceHealthSnapshots.Add(new ResourceHealthSnapshotEntity
        {
            ResourceId = summary.ResourceId,
            Status = summary.Status.ToString(),
            CheckedAt = summary.CheckedAt,
            ChecksJson = JsonSerializer.Serialize(summary.Checks, SerializerOptions)
        });
        context.SaveChanges();
        PruneSnapshots(context, summary.ResourceId, retainedSnapshots);
    }

    public void AddRange(IEnumerable<ResourceHealthSummary> summaries)
    {
        foreach (var summary in summaries)
        {
            Add(summary);
        }
    }

    private void PruneSnapshots(
        CloudShellDbContext context,
        string resourceId,
        int retainedSnapshots)
    {
        var staleIds = context.ResourceHealthSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ResourceId == resourceId)
            .OrderByDescending(snapshot => snapshot.CheckedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .Skip(retainedSnapshots)
            .Select(snapshot => snapshot.Id)
            .ToArray();

        if (staleIds.Length == 0)
        {
            return;
        }

        context.ResourceHealthSnapshots
            .Where(snapshot => staleIds.Contains(snapshot.Id))
            .ExecuteDelete();
    }

    private int GetRetainedSnapshotsPerResource() =>
        Math.Clamp(
            options.Value.RetainedSnapshotsPerResource <= 0
                ? 1
                : options.Value.RetainedSnapshotsPerResource,
            0,
            MaximumRetainedSnapshotsPerResource);

    private static ResourceHealthSummary ToSummary(ResourceHealthSnapshotEntity entity) =>
        new(
            entity.ResourceId,
            Enum.TryParse<ResourceHealthStatus>(entity.Status, ignoreCase: true, out var status)
                ? status
                : ResourceHealthStatus.Unknown,
            entity.CheckedAt,
            JsonSerializer.Deserialize<IReadOnlyList<ResourceHealthCheckResult>>(
                entity.ChecksJson,
                SerializerOptions) ?? []);
}
