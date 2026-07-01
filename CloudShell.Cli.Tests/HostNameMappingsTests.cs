using CloudShell.Cli;

namespace CloudShell.Cli.Tests;

public sealed class HostNameMappingsTests
{
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
