using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public sealed record OperationsInventoryRow(Resource Resource, int Depth, int ChildCount, bool Expanded);

/// <summary>Projects visible resources into a parent/child inventory without exposing hidden parents.</summary>
public static class OperationsInventoryTree
{
    public static IReadOnlySet<string> Ancestors(IReadOnlyList<Resource> resources, string? resourceId)
    {
        var byId = resources.ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (resourceId is null) return ancestors;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { resourceId };
        while (byId.TryGetValue(resourceId, out var resource) && resource.ParentResourceId is { } parent && byId.ContainsKey(parent) && visited.Add(parent))
        {
            ancestors.Add(parent);
            resourceId = parent;
        }
        return ancestors;
    }

    public static IReadOnlyList<OperationsInventoryRow> Create(
        IReadOnlyList<Resource> visibleResources,
        IReadOnlySet<string> collapsed,
        string filter,
        Func<Resource, string> getLabel)
    {
        var resources = visibleResources.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var children = visibleResources.Where(r => r.ParentResourceId is not null && resources.ContainsKey(r.ParentResourceId))
            .GroupBy(r => r.ParentResourceId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(getLabel, StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
        var query = filter.Trim();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in visibleResources.Where(r => query.Length == 0 || getLabel(r).Contains(query, StringComparison.OrdinalIgnoreCase) || r.Kind.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            var current = resource;
            while (included.Add(current.Id) && current.ParentResourceId is not null && resources.TryGetValue(current.ParentResourceId, out var parent))
                current = parent;
        }
        var rows = new List<OperationsInventoryRow>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(Resource resource, int depth, bool ancestorsVisible)
        {
            if (!visited.Add(resource.Id)) return;
            var members = children.GetValueOrDefault(resource.Id) ?? [];
            var expanded = query.Length > 0 || !collapsed.Contains(resource.Id);
            var visible = ancestorsVisible && included.Contains(resource.Id);
            if (visible) rows.Add(new(resource, depth, members.Count(r => included.Contains(r.Id)), expanded));
            foreach (var child in members) Visit(child, depth + 1, visible && expanded);
        }
        foreach (var root in visibleResources.Where(r => r.ParentResourceId is null || !resources.ContainsKey(r.ParentResourceId)).OrderBy(getLabel, StringComparer.OrdinalIgnoreCase))
            Visit(root, 0, true);
        // Malformed provider cycles must not hang rendering or hide the entire component.
        foreach (var resource in visibleResources.OrderBy(getLabel, StringComparer.OrdinalIgnoreCase))
            if (!visited.Contains(resource.Id)) Visit(resource, 0, true);
        return rows;
    }
}
