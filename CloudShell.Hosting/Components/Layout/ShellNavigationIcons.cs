using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace CloudShell.Hosting.Components.Layout;

public static class ShellNavigationIcons
{
    public static Icon FromName(string icon) =>
        icon.Trim().ToLowerInvariant() switch
        {
            "grid" or "overview" or "home" => new Icons.Regular.Size20.HomeMore(),
            "server" or "resources" or "resource" => new Icons.Regular.Size20.AppFolder(),
            "environment" => new Icons.Regular.Size20.CubeTree(),
            "health" or "health-checks" => new Icons.Regular.Size20.HeartPulseCheckmark(),
            "pulse" or "observability" or "usage" => new Icons.Regular.Size20.DataUsage(),
            "document" or "logs" or "log" => new Icons.Regular.Size20.SlideTextSparkle(),
            "dependencies" => new Icons.Regular.Size20.BranchFork(),
            "service-map" => new Icons.Regular.Size20.FlowchartCircle(),
            "traces" or "trace" => new Icons.Regular.Size20.GanttChart(),
            "metrics" or "metric" => new Icons.Regular.Size20.ChartMultiple(),
            "users" or "people" or "identity" => new Icons.Regular.Size20.People(),
            "settings" or "configuration" => new Icons.Regular.Size20.SettingsCogMultiple(),
            "plug" or "extensions" or "extension" => new Icons.Regular.Size20.PuzzlePiece(),
            "docker" or "container" => new Icons.Regular.Size20.CubeTree(),
            "network" => new Icons.Regular.Size20.Connector(),
            "storage" => new Icons.Regular.Size20.Storage(),
            "database" => new Icons.Regular.Size20.DatabaseMultiple(),
            _ => new Icons.Regular.Size20.WindowApps()
        };
}
