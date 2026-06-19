using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceTypeIcons
{
    public static Icon FromName(string? name)
    {
        var normalized = name?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new Icons.Regular.Size20.WindowApps();
        }

        return normalized switch
        {
            "application" => new Icons.Regular.Size20.AppGeneric(),
            "web" => new Icons.Regular.Size20.GlobeSync(),
            "container" => new Icons.Regular.Size20.CubeTree(),
            "docker" => new Icons.Regular.Size20.Server(),
            "network" => new Icons.Regular.Size20.Connector(),
            "service" => new Icons.Regular.Size20.Server(),
            "storage" => new Icons.Regular.Size20.Storage(),
            "database" => new Icons.Regular.Size20.DatabaseMultiple(),
            "database-server" => new Icons.Regular.Size20.CloudDatabase(),
            "database-item" => new Icons.Regular.Size20.Database(),
            "configuration-store" => new Icons.Regular.Size20.AppsSettings(),
            "key" => new Icons.Regular.Size20.KeyMultiple(),
            "lock-closed" => new Icons.Regular.Size20.LockClosed(),
            _ => FromResourceDescriptor(normalized)
        };
    }

    private static Icon FromResourceDescriptor(string normalized) =>
        normalized switch
        {
            _ when normalized.Contains("secret", StringComparison.Ordinal) ||
                normalized.Contains("vault", StringComparison.Ordinal) ||
                normalized.Contains("key", StringComparison.Ordinal) => new Icons.Regular.Size20.KeyMultiple(),
            _ when normalized.Contains("lock", StringComparison.Ordinal) => new Icons.Regular.Size20.LockClosed(),
            "application.sql-server" => new Icons.Regular.Size20.CloudDatabase(),
            "application.sql-database" => new Icons.Regular.Size20.Database(),
            _ when normalized.Contains("sql-database", StringComparison.Ordinal) => new Icons.Regular.Size20.Database(),
            _ when normalized.Contains("sql-server", StringComparison.Ordinal) => new Icons.Regular.Size20.CloudDatabase(),
            _ when normalized.Contains("database", StringComparison.Ordinal) => new Icons.Regular.Size20.DatabaseMultiple(),
            _ when normalized.Contains("sql", StringComparison.Ordinal) => new Icons.Regular.Size20.CloudDatabase(),
            _ when normalized.Contains("storage", StringComparison.Ordinal) ||
                normalized.Contains("volume", StringComparison.Ordinal) => new Icons.Regular.Size20.Storage(),
            _ when normalized.Contains("docker", StringComparison.Ordinal) => new Icons.Regular.Size20.Server(),
            _ when normalized.Contains("container", StringComparison.Ordinal) ||
                normalized.Contains("replica", StringComparison.Ordinal) ||
                normalized.Contains("runtime", StringComparison.Ordinal) => new Icons.Regular.Size20.CubeTree(),
            _ when normalized.Contains("network", StringComparison.Ordinal) ||
                normalized.Contains("dns", StringComparison.Ordinal) ||
                normalized.Contains("mapping", StringComparison.Ordinal) ||
                normalized.Contains("loadbalancer", StringComparison.Ordinal) ||
                normalized.Contains("load-balancer", StringComparison.Ordinal) ||
                normalized.Contains("ingress", StringComparison.Ordinal) => new Icons.Regular.Size20.Connector(),
            _ when normalized.Contains("configuration", StringComparison.Ordinal) ||
                normalized.Contains("settings", StringComparison.Ordinal) => new Icons.Regular.Size20.SettingsCogMultiple(),
            _ when normalized.Contains("web", StringComparison.Ordinal) => new Icons.Regular.Size20.GlobeSync(),
            _ when normalized.Contains("application", StringComparison.Ordinal) ||
                normalized.Contains("project", StringComparison.Ordinal) ||
                normalized.Contains("executable", StringComparison.Ordinal) ||
                normalized.Contains("app", StringComparison.Ordinal) => new Icons.Regular.Size20.AppGeneric(),
            _ when normalized.Contains("service", StringComparison.Ordinal) => new Icons.Regular.Size20.Server(),
            _ => new Icons.Regular.Size20.WindowApps()
        };
}
