using CloudShell.Cli;

namespace CloudShell.Cli.Tests;

public sealed class HostNameMappingsTests
{
    [Fact]
    public void PlanAdd_UsesUnixHostsFileWhenNoOverride()
    {
        var mappings = new HostNameMappings(
            new HostNameMappingPlatform(IsWindows: false, SystemDirectory: null));

        var plan = mappings.PlanAdd("api.local.test", "127.0.0.1", hostsFile: null);

        Assert.Equal("/etc/hosts", plan.HostsFile);
    }

    [Fact]
    public void PlanAdd_UsesWindowsHostsFileWhenNoOverride()
    {
        var systemDirectory = Path.Combine("C:", "Windows", "System32");
        var mappings = new HostNameMappings(
            new HostNameMappingPlatform(IsWindows: true, systemDirectory));

        var plan = mappings.PlanAdd("api.local.test", "127.0.0.1", hostsFile: null);

        Assert.Equal(
            Path.Combine(systemDirectory, "drivers", "etc", "hosts"),
            plan.HostsFile);
    }

    [Fact]
    public void PlanAdd_RequiresWindowsSystemDirectoryWhenNoOverride()
    {
        var mappings = new HostNameMappings(
            new HostNameMappingPlatform(IsWindows: true, SystemDirectory: null));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            mappings.PlanAdd("api.local.test", "127.0.0.1", hostsFile: null));

        Assert.Equal(
            "The Windows system directory could not be resolved.",
            exception.Message);
    }

    [Fact]
    public async Task ApplyAsync_AddsAndRemovesCloudShellManagedHostName()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(path, "127.0.0.1 localhost" + Environment.NewLine);
            var mappings = new HostNameMappings();

            await mappings.ApplyAsync(mappings.PlanAdd("Api.Local.Test", "127.0.0.1", path));

            var added = await File.ReadAllTextAsync(path);
            Assert.Contains("# BEGIN CloudShell local hostnames", added);
            Assert.Contains("127.0.0.1 api.local.test", added);
            Assert.Contains("# END CloudShell local hostnames", added);

            await mappings.ApplyAsync(mappings.PlanRemove("api.local.test", path));

            var removed = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("api.local.test", removed);
            Assert.DoesNotContain("# BEGIN CloudShell local hostnames", removed);
            Assert.Contains("127.0.0.1 localhost", removed);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
