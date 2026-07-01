using CloudShell.ControlPlane.ResourceManager.Platform;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.Tests;

public sealed class PlatformResourceProviderTests
{
    [Fact]
    public void GetResources_DoesNotCreateImplicitHostNetworkFromEmptyPlatformStore()
    {
        var provider = new PlatformResourceProvider(
            CreatePlatformStore(),
            new PlatformResourceOptions());

        var resources = provider.GetResources();

        Assert.DoesNotContain(resources, resource =>
            string.Equals(resource.Id, PlatformResourceProvider.HostNetworkResourceId, StringComparison.OrdinalIgnoreCase));
    }

    private static PlatformResourceStore CreatePlatformStore()
    {
        var contentRoot = Directory.CreateTempSubdirectory("cloudshell-platform-provider-tests-").FullName;
        return new PlatformResourceStore(
            new PlatformResourceOptions
            {
                DefinitionsPath = "platform-resources.json"
            },
            new TestHostEnvironment(contentRoot));
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CloudShell.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
