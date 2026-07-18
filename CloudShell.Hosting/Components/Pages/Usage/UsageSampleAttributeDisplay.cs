using CloudShell.Abstractions.Usage;

namespace CloudShell.Hosting.Components.Pages.Usage;

public static class UsageSampleAttributeDisplay
{
    public static IReadOnlyList<UsageSampleAttributeDisplayItem> CreateVisibleAttributes(
        IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.Count == 0)
        {
            return [];
        }

        return attributes
            .Where(attribute => ShouldShow(attribute.Key, attribute.Value))
            .Select(attribute => new UsageSampleAttributeDisplayItem(
                attribute.Key,
                FormatLabel(attribute.Key),
                attribute.Value))
            .OrderBy(attribute => attribute.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(attribute => attribute.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldShow(string name, string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(name, UsageAttributeNames.DisplayName, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, UsageAttributeNames.Description, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, UsageAttributeNames.Source, StringComparison.OrdinalIgnoreCase);

    private static string FormatLabel(string name) =>
        name switch
        {
            UsageAttributeNames.MonitoringProvider => "Monitoring provider",
            UsageAttributeNames.MonitoringStatus => "Monitoring status",
            UsageAttributeNames.MonitoringMessage => "Monitoring message",
            _ => name
        };
}

public sealed record UsageSampleAttributeDisplayItem(
    string Name,
    string Label,
    string Value);
