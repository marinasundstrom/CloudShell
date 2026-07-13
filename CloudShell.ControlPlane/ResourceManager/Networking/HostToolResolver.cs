namespace CloudShell.ControlPlane.ResourceManager.Networking;

public interface IHostToolResolver
{
    bool IsAvailable(string fileName);
}

public sealed class PathHostToolResolver : IHostToolResolver
{
    public bool IsAvailable(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (Path.IsPathRooted(fileName) ||
            fileName.Contains(Path.DirectorySeparatorChar) ||
            fileName.Contains(Path.AltDirectorySeparatorChar) ||
            fileName.Contains('\\'))
        {
            return File.Exists(fileName);
        }

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = GetExecutableExtensions(fileName);
        foreach (var path in paths)
        {
            foreach (var extension in extensions)
            {
                if (File.Exists(Path.Combine(path, fileName + extension)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetExecutableExtensions(string fileName)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(fileName))
        {
            return [string.Empty];
        }

        var pathExtensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return pathExtensions.Length == 0
            ? [string.Empty]
            : pathExtensions.Prepend(string.Empty).ToArray();
    }
}
