namespace CloudShell.Components;

public static class ResourceDisplayNameDefaults
{
    public static string Resolve(
        bool enableDisplayNames,
        string? displayName,
        string resourceName) =>
        resourceName.Trim();
}
