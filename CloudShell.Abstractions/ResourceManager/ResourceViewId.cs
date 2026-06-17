namespace CloudShell.Abstractions.ResourceManager;

public readonly record struct ResourceViewId
{
    public ResourceViewId(string groupId, string identifier)
    {
        GroupId = NormalizePart(groupId, nameof(groupId));
        Identifier = NormalizePart(identifier, nameof(identifier));
        Value = $"{GroupId}:{Identifier}";
    }

    public string GroupId { get; }

    public string Identifier { get; }

    public string Value { get; }

    public override string ToString() => Value;

    public static bool TryParse(string? value, out ResourceViewId id)
    {
        id = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = value.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex != value.LastIndexOf(':'))
        {
            return false;
        }

        var groupId = value[..separatorIndex];
        var identifier = value[(separatorIndex + 1)..];
        if (string.IsNullOrWhiteSpace(groupId) ||
            string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        id = new ResourceViewId(groupId, identifier);
        return true;
    }

    public static ResourceViewId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!TryParse(value, out var id))
        {
            throw new InvalidOperationException(
                $"'{value}' is not a valid resource view ID. Expected '<group-id>:<identifier>'.");
        }

        return id;
    }

    private static string NormalizePart(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{parameterName}' must not contain ':'.",
                parameterName);
        }

        return normalized;
    }
}
