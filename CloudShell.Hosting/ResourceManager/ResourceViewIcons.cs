using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceViewIcons
{
    public static Icon FromName(string? name)
    {
        var normalized = name?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new Icons.Regular.Size20.AppsListDetail();
        }

        return normalized switch
        {
            "overview" or "home" => new Icons.Regular.Size20.HomeMore(),
            "configuration" or "settings" => new Icons.Regular.Size20.SettingsCogMultiple(),
            "endpoints" or "endpoint" or "networking" or "network" => new Icons.Regular.Size20.Connector(),
            "dns" or "name-mapping" or "name-mappings" => new Icons.Regular.Size20.GlobeSync(),
            "identity" or "permissions" => new Icons.Regular.Size20.LockClosed(),
            "storage" or "volumes" or "volume" => new Icons.Regular.Size20.Storage(),
            "activity" or "events" => new Icons.Regular.Size20.DataUsage(),
            "environment" => new Icons.Regular.Size20.WindowApps(),
            "document" or "logs" or "log" => new Icons.Regular.Size20.SlideTextSparkle(),
            "traces" or "trace" => new Icons.Regular.Size20.GanttChart(),
            "runtime" or "deployment" or "replicas" or "containers" => new Icons.Regular.Size20.CubeTree(),
            "scaling" or "scale" => new Icons.Regular.Size20.ChartMultiple(),
            "entries" or "secrets" => new Icons.Regular.Size20.KeyMultiple(),
            _ => ResourceTypeIcons.FromName(normalized)
        };
    }
}
