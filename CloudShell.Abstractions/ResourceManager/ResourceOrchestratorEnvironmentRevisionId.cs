using System.Globalization;
using System.Text;

namespace CloudShell.Abstractions.ResourceManager;

public readonly record struct ResourceOrchestratorEnvironmentRevisionId
{
    public ResourceOrchestratorEnvironmentRevisionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim();
        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Environment revision IDs cannot contain control characters.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; } = string.Empty;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static ResourceOrchestratorEnvironmentRevisionId Parse(string value) => new(value);

    public static bool TryParse(
        string? value,
        out ResourceOrchestratorEnvironmentRevisionId revisionId)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            revisionId = default;
            return false;
        }

        revisionId = new ResourceOrchestratorEnvironmentRevisionId(value);
        return true;
    }

    public static ResourceOrchestratorEnvironmentRevisionId FromScope(
        string sourceResourceId,
        string serviceId,
        int revisionNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        var scope = NormalizePart($"{sourceResourceId}-{serviceId}");
        return new ResourceOrchestratorEnvironmentRevisionId(
            string.Create(
                CultureInfo.InvariantCulture,
                $"env-{scope}-{Math.Max(1, revisionNumber)}"));
    }

    public override string ToString() => Value ?? string.Empty;

    private static string NormalizePart(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized)
            ? "resource"
            : normalized;
    }
}
