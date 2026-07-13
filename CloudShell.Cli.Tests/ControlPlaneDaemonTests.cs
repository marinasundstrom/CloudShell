using CloudShell.Cli;

namespace CloudShell.Cli.Tests;

public sealed class ControlPlaneDaemonTests
{
    [Fact]
    public void CreateWindowsHostStartInfo_UsesArgumentListForHostArguments()
    {
        var startInfo = ControlPlaneDaemon.CreateWindowsHostStartInfo(
            "/repo/out/CloudShell Host.dll",
            "/repo/CloudShell Host",
            "runtime data",
            "host settings.json",
            new Uri("http://127.0.0.1:5097"));

        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal("/repo/CloudShell Host", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.RedirectStandardOutput);
        Assert.False(startInfo.RedirectStandardError);
        Assert.Equal(
            [
                "/repo/out/CloudShell Host.dll",
                "--urls",
                "http://127.0.0.1:5097/",
                "--CloudShell:DataDirectory",
                Path.GetFullPath("runtime data"),
                "--CloudShell:HostSettingsPath",
                Path.GetFullPath("host settings.json")
            ],
            startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void CreateUnixHostLauncherStartInfo_QuotesDetachedShellCommand()
    {
        var startInfo = ControlPlaneDaemon.CreateUnixHostLauncherStartInfo(
            "/repo/out/CloudShell Host.dll",
            "/repo/CloudShell Host's App",
            "runtime data",
            "host settings.json",
            new Uri("http://127.0.0.1:5097"),
            "daemon state");

        Assert.Equal("/bin/sh", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(["-c", ExpectedUnixCommand()], startInfo.ArgumentList.ToArray());
    }

    private static string ExpectedUnixCommand()
    {
        var logFile = Path.Combine(Path.GetFullPath("daemon state"), "control-plane.log");
        return string.Join(
            " ",
            [
                "cd",
                "'/repo/CloudShell Host'\"'\"'s App'",
                "||",
                "exit",
                "1;",
                "nohup",
                "dotnet",
                "'/repo/out/CloudShell Host.dll'",
                "'--urls'",
                "'http://127.0.0.1:5097/'",
                "'--CloudShell:DataDirectory'",
                Quote(Path.GetFullPath("runtime data")),
                "'--CloudShell:HostSettingsPath'",
                Quote(Path.GetFullPath("host settings.json")),
                ">",
                Quote(logFile),
                "2>&1",
                "<",
                "/dev/null",
                "&",
                "echo",
                "$!"
            ]);
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
