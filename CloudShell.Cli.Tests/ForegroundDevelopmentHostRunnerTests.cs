using CloudShell.Cli;

namespace CloudShell.Cli.Tests;

public sealed class ForegroundDevelopmentHostRunnerTests
{
    [Fact]
    public void ResolveDevelopmentHostAssembly_PrefersBundledHost()
    {
        using var directory = new TemporaryDirectory();
        var hostDirectory = Path.Combine(directory.Path, "host");
        Directory.CreateDirectory(hostDirectory);
        var assemblyPath = Path.Combine(
            hostDirectory,
            ForegroundDevelopmentHostRunner.DevelopmentHostAssemblyName);
        File.WriteAllText(assemblyPath, string.Empty);

        var resolved = ForegroundDevelopmentHostRunner.ResolveDevelopmentHostAssembly(
            directory.Path,
            directory.Path);

        Assert.Equal(assemblyPath, resolved);
    }

    [Fact]
    public void CreateStartInfo_BundledHostInheritsConsoleAndUsesProjectDirectory()
    {
        using var directory = new TemporaryDirectory();
        var hostDirectory = Path.Combine(directory.Path, "tool", "host");
        Directory.CreateDirectory(hostDirectory);
        var assemblyPath = Path.Combine(
            hostDirectory,
            ForegroundDevelopmentHostRunner.DevelopmentHostAssemblyName);
        File.WriteAllText(assemblyPath, string.Empty);
        var webRoot = Path.Combine(hostDirectory, "wwwroot");
        Directory.CreateDirectory(webRoot);
        var workingDirectory = Path.Combine(directory.Path, "sample");
        Directory.CreateDirectory(workingDirectory);
        var dataDirectory = Path.Combine(workingDirectory, ".cloudshell");

        var startInfo = ForegroundDevelopmentHostRunner.CreateStartInfo(
            hostProject: null,
            workingDirectory,
            dataDirectory,
            hostSettingsPath: null,
            new Uri("http://127.0.0.1:5112"),
            Path.Combine(directory.Path, "tool"));

        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal(workingDirectory, startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.RedirectStandardOutput);
        Assert.False(startInfo.RedirectStandardError);
        Assert.Equal(assemblyPath, startInfo.ArgumentList[0]);
        Assert.Equal("--webroot", startInfo.ArgumentList[1]);
        Assert.Equal(webRoot, startInfo.ArgumentList[2]);
        Assert.Contains(dataDirectory, startInfo.ArgumentList);
        Assert.Contains("--Authentication:Enabled", startInfo.ArgumentList);
        Assert.Contains("false", startInfo.ArgumentList);
    }

    [Fact]
    public void CreateStartInfo_CustomHostKeepsProjectOwnedWebRoot()
    {
        using var directory = new TemporaryDirectory();
        var workingDirectory = Path.Combine(directory.Path, "sample");
        Directory.CreateDirectory(workingDirectory);
        var hostProject = Path.Combine(workingDirectory, "CustomHost.csproj");
        File.WriteAllText(hostProject, string.Empty);

        var startInfo = ForegroundDevelopmentHostRunner.CreateStartInfo(
            hostProject,
            workingDirectory,
            Path.Combine(workingDirectory, ".cloudshell"),
            hostSettingsPath: null,
            new Uri("http://127.0.0.1:5112"));

        Assert.DoesNotContain("--webroot", startInfo.ArgumentList);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"cloudshell-cli-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
