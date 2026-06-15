namespace CloudShell.Components;

public static class ResourceDisplayNameDefaults
{
    public static string Resolve(
        bool enableDisplayNames,
        string? displayName,
        string resourceId) =>
        enableDisplayNames && !string.IsNullOrWhiteSpace(displayName)
            ? displayName.Trim()
            : resourceId.Trim();
}
