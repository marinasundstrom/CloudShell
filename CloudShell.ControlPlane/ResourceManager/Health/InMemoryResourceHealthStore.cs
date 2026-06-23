using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.ResourceManager.Health;

public sealed class InMemoryResourceHealthStore(IOptions<ResourceHealthOptions> options) : IResourceHealthStore
{
    private const int MaximumRetainedSnapshotsPerResource = 10_000;

    private readonly ConcurrentDictionary<string, ResourceHealthSummary> latest =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, List<ResourceHealthSummary>> snapshots =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ResourceHealthSummary> GetLatest(
        IEnumerable<string>? resourceIds = null)
    {
        if (resourceIds is null)
        {
            return latest.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        var requested = resourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return latest
            .Where(item => requested.Contains(item.Key))
            .ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    public ResourceHealthSummary? GetLatest(string resourceId) =>
        latest.TryGetValue(resourceId, out var summary)
            ? summary
            : null;

    public IReadOnlyList<ResourceHealthSummary> GetSnapshots(
        string resourceId,
        int maxSnapshots = 100)
    {
        if (!snapshots.TryGetValue(resourceId, out var resourceSnapshots))
        {
            return [];
        }

        var retainedSnapshots = GetRetainedSnapshotsPerResource();
        if (retainedSnapshots == 0)
        {
            return [];
        }

        lock (resourceSnapshots)
        {
            return resourceSnapshots
                .OrderByDescending(summary => summary.CheckedAt)
                .Take(Math.Clamp(maxSnapshots, 1, retainedSnapshots))
                .ToArray();
        }
    }

    public void Add(ResourceHealthSummary summary)
    {
        latest[summary.ResourceId] = summary;
        var retainedSnapshots = GetRetainedSnapshotsPerResource();
        if (retainedSnapshots == 0)
        {
            return;
        }

        var resourceSnapshots = snapshots.GetOrAdd(summary.ResourceId, _ => []);
        lock (resourceSnapshots)
        {
            resourceSnapshots.Add(summary);
            resourceSnapshots.Sort((left, right) => left.CheckedAt.CompareTo(right.CheckedAt));
            if (resourceSnapshots.Count > retainedSnapshots)
            {
                resourceSnapshots.RemoveRange(0, resourceSnapshots.Count - retainedSnapshots);
            }
        }
    }

    public void AddRange(IEnumerable<ResourceHealthSummary> summaries)
    {
        foreach (var summary in summaries)
        {
            Add(summary);
        }
    }

    private int GetRetainedSnapshotsPerResource() =>
        Math.Clamp(
            options.Value.RetainedSnapshotsPerResource,
            0,
            MaximumRetainedSnapshotsPerResource);
}
