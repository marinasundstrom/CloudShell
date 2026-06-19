namespace CloudShell.Hosting.ResourceManager;

public enum ResourceCalloutSeverity
{
    Success,
    Info,
    Warning,
    Error
}

public static class ResourceCalloutSeverityParser
{
    public static ResourceCalloutSeverity FromName(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "success" => ResourceCalloutSeverity.Success,
            "information" or "info" => ResourceCalloutSeverity.Info,
            "error" => ResourceCalloutSeverity.Error,
            _ => ResourceCalloutSeverity.Warning
        };
}
