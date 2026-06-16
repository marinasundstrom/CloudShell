namespace CloudShell.Abstractions.ResourceManager;

public readonly record struct ResourceId
{
    public ResourceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim();
        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Resource IDs cannot contain control characters.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; } = string.Empty;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public bool IsQualified =>
        !IsEmpty && Value.Contains(':', StringComparison.Ordinal);

    public string? Scope
    {
        get
        {
            if (!IsQualified)
            {
                return null;
            }

            var separator = Value.LastIndexOf(':');
            return separator <= 0 ? null : Value[..separator];
        }
    }

    public string Name
    {
        get
        {
            if (!IsQualified)
            {
                return Value ?? string.Empty;
            }

            var separator = Value.LastIndexOf(':');
            return separator >= Value.Length - 1 ? string.Empty : Value[(separator + 1)..];
        }
    }

    public static ResourceId Parse(string value) => new(value);

    public static bool TryParse(string? value, out ResourceId resourceId)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            resourceId = default;
            return false;
        }

        resourceId = new ResourceId(value);
        return true;
    }

    public static ResourceId FromName(string scope, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalizedScope = scope.Trim();
        if (normalizedScope.Any(char.IsControl))
        {
            throw new ArgumentException("Resource ID scopes cannot contain control characters.", nameof(scope));
        }

        var normalized = name.Trim();
        if (normalized.Contains(':', StringComparison.Ordinal))
        {
            return new ResourceId(normalized);
        }

        var slug = string.Join(
                "-",
                normalized.ToLowerInvariant().Split(
                    [' ', '.', '_', ':', '/', '\\'],
                    StringSplitOptions.RemoveEmptyEntries))
            .Trim('-');

        return new ResourceId(
            string.IsNullOrWhiteSpace(slug)
                ? $"{normalizedScope}:{Guid.NewGuid():N}"
                : $"{normalizedScope}:{slug}");
    }

    public override string ToString() => Value ?? string.Empty;
}
