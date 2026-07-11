using System.IO.Compression;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;

namespace CloudShell.ControlPlane.Tests;

public sealed class ApplicationArtifactValidationTests
{
    [Fact]
    public async Task ValidateDeploymentArtifactAsync_AcceptsDotNetPublishedOutput()
    {
        var provider = new ApplicationArtifactValidationProvider();
        await using var content = new MemoryStream(CreateZip(
            ("publish/Api.dll", "assembly"),
            ("publish/Api.runtimeconfig.json", "{}")));

        var result = await provider.ValidateDeploymentArtifactAsync(
            new DeploymentArtifactValidationContext(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
                "api",
                "deployment-artifact:api",
                "rev-1",
                "zip",
                "abc123",
                content.Length,
                "publish",
                "dotnetPublishedOutput"),
            content);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public async Task ValidateDeploymentArtifactAsync_RejectsDotNetPublishedOutputWithoutRuntimeAssembly()
    {
        var provider = new ApplicationArtifactValidationProvider();
        await using var content = new MemoryStream(CreateZip(
            ("publish/Dependency.dll", "assembly")));

        var result = await provider.ValidateDeploymentArtifactAsync(
            new DeploymentArtifactValidationContext(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
                "api",
                "deployment-artifact:api",
                "rev-1",
                "zip",
                "abc123",
                content.Length,
                "publish",
                "dotnetPublishedOutput"),
            content);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "application.aspNetCoreProject.publishedAssemblyMissing");
    }

    private static byte[] CreateZip(params (string Path, string Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return output.ToArray();
    }
}
