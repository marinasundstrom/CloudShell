using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.Hosting;

public static class CloudShellDataDirectory
{
    public const string ConfigurationKey = "CloudShell:DataDirectory";

    public static string ResolveRoot(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configuredPath = configuration[ConfigurationKey];
        var root = string.IsNullOrWhiteSpace(configuredPath)
            ? environment.ContentRootPath
            : ResolvePath(configuredPath, environment.ContentRootPath);
        Directory.CreateDirectory(root);
        return root;
    }

    public static string ResolvePath(
        string path,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, ResolveRoot(configuration, environment));
    }

    private static string ResolvePath(string path, string basePath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, basePath);
}
