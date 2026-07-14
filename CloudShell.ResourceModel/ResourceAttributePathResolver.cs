namespace CloudShell.ResourceModel;

public sealed class ResourceAttributePathResolver
{
    private readonly IReadOnlyDictionary<string, ResourceAttributeId> _paths;

    private ResourceAttributePathResolver(
        IReadOnlyDictionary<string, ResourceAttributeId> paths,
        IReadOnlyList<ResourceAttributePathConflict> conflicts)
    {
        _paths = paths;
        Conflicts = conflicts;
    }

    public static ResourceAttributePathResolver Empty { get; } =
        new(
            new Dictionary<string, ResourceAttributeId>(StringComparer.OrdinalIgnoreCase),
            []);

    public IReadOnlyList<ResourceAttributePathConflict> Conflicts { get; }

    public static ResourceAttributePathResolver FromDefinitions(
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? definitions)
    {
        if (definitions is null || definitions.Count == 0)
        {
            return Empty;
        }

        var candidates = new Dictionary<string, List<ResourceAttributeId>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (attributeId, definition) in definitions)
        {
            AddCandidate(candidates, attributeId.ToString(), attributeId);
            AddCandidate(candidates, definition.Path, attributeId);

            foreach (var alias in definition.Aliases ?? [])
            {
                AddCandidate(candidates, alias, attributeId);
            }
        }

        var paths = new Dictionary<string, ResourceAttributeId>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<ResourceAttributePathConflict>();

        foreach (var (path, attributeIds) in candidates)
        {
            var distinctAttributeIds = attributeIds
                .Distinct()
                .OrderBy(attributeId => attributeId.ToString(), StringComparer.Ordinal)
                .ToArray();

            if (distinctAttributeIds.Length == 1)
            {
                paths[path] = distinctAttributeIds[0];
            }
            else
            {
                conflicts.Add(new(path, distinctAttributeIds));
            }
        }

        return new ResourceAttributePathResolver(
            paths,
            conflicts
                .OrderBy(conflict => conflict.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public bool TryResolve(
        string path,
        out ResourceAttributeId attributeId)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            attributeId = default;
            return false;
        }

        return _paths.TryGetValue(path.Trim(), out attributeId);
    }

    public bool TryGetConflict(
        string path,
        out ResourceAttributePathConflict conflict)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            conflict = default!;
            return false;
        }

        var normalizedPath = path.Trim();
        foreach (var candidate in Conflicts)
        {
            if (string.Equals(candidate.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                conflict = candidate;
                return true;
            }
        }

        conflict = default!;
        return false;
    }

    public ResourceAttributeId ResolveOrCreate(string path) =>
        TryResolve(path, out var attributeId)
            ? attributeId
            : ResourceAttributeId.Create(path);

    private static void AddCandidate(
        Dictionary<string, List<ResourceAttributeId>> candidates,
        string? path,
        ResourceAttributeId attributeId)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalizedPath = path.Trim();
        if (!candidates.TryGetValue(normalizedPath, out var attributeIds))
        {
            attributeIds = [];
            candidates[normalizedPath] = attributeIds;
        }

        if (!attributeIds.Contains(attributeId))
        {
            attributeIds.Add(attributeId);
        }
    }
}

public sealed record ResourceAttributePathConflict(
    string Path,
    IReadOnlyList<ResourceAttributeId> AttributeIds);
