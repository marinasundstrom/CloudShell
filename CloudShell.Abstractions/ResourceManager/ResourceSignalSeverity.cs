namespace CloudShell.Abstractions.ResourceManager;

public enum ResourceSignalSeverity
{
    Success,
    Info,
    Warning,
    Error
}

public static class ResourceSignalSeverityParser
{
    public static ResourceSignalSeverity FromName(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "success" => ResourceSignalSeverity.Success,
            "information" or "info" => ResourceSignalSeverity.Info,
            "error" => ResourceSignalSeverity.Error,
            _ => ResourceSignalSeverity.Warning
        };

    public static string ToLevel(ResourceSignalSeverity severity) =>
        severity switch
        {
            ResourceSignalSeverity.Success => "Success",
            ResourceSignalSeverity.Info => "Information",
            ResourceSignalSeverity.Error => "Error",
            _ => "Warning"
        };
}
