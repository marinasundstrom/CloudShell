using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceTypeIcons
{
    public static Icon FromName(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            "application" => new Icons.Regular.Size20.AppGeneric(),
            "web" => new Icons.Regular.Size20.GlobeSync(),
            "container" => new Icons.Regular.Size20.CubeTree(),
            "docker" => new Icons.Regular.Size20.Server(),
            "network" => new Icons.Regular.Size20.Connector(),
            "service" => new Icons.Regular.Size20.Server(),
            "storage" => new Icons.Regular.Size20.Storage(),
            "database" => new Icons.Regular.Size20.DatabaseMultiple(),
            "key" => new Icons.Regular.Size20.KeyMultiple(),
            "lock-closed" => new Icons.Regular.Size20.LockClosed(),
            _ => new Icons.Regular.Size20.WindowApps()
        };
}
