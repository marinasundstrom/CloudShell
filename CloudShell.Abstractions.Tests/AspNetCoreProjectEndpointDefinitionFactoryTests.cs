using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class AspNetCoreProjectEndpointDefinitionFactoryTests
{
    [Fact]
    public void CreateEndpointPorts_UsesManualLocalEndpointWhenEndpointUriHasPort()
    {
        var port = Assert.Single(
            AspNetCoreProjectEndpointDefinitionFactory.CreateEndpointPorts("https://127.0.0.2:7123"));

        Assert.Equal("http", port.Name);
        Assert.Equal(7123, port.TargetPort);
        Assert.Equal(7123, port.Port);
        Assert.Equal("https", port.Protocol);
        Assert.Equal(ResourceExposureScope.Local, port.Exposure);
        Assert.Equal(ResourceEndpointAssignment.Manual, port.Assignment);
        Assert.Equal("127.0.0.2", port.Host);
    }

    [Fact]
    public void CreateEndpointPorts_DefaultsToProviderAssignedLocalHttpEndpoint()
    {
        var port = Assert.Single(AspNetCoreProjectEndpointDefinitionFactory.CreateEndpointPorts(null));

        Assert.Equal("http", port.Name);
        Assert.Equal(80, port.TargetPort);
        Assert.Null(port.Port);
        Assert.Equal("http", port.Protocol);
        Assert.Equal(ResourceExposureScope.Local, port.Exposure);
        Assert.Equal(ResourceEndpointAssignment.ProviderDefault, port.Assignment);
    }

    [Fact]
    public void TryReadLaunchSettingsEndpointPorts_PrefersProjectProfile()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            WriteLaunchSettings(
                contentRoot,
                "src/API/API.csproj",
                """
                {
                  "profiles": {
                    "docker": {
                      "commandName": "Docker",
                      "applicationUrl": "http://localhost:6000"
                    },
                    "https": {
                      "commandName": "Project",
                      "applicationUrl": "https://localhost:7123;http://localhost:5123"
                    }
                  }
                }
                """);
            var factory = new AspNetCoreProjectEndpointDefinitionFactory(contentRoot);

            var ports = factory.TryReadLaunchSettingsEndpointPorts("src/API/API.csproj")
                .OrderBy(port => port.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.Collection(
                ports,
                port =>
                {
                    Assert.Equal("http", port.Name);
                    Assert.Equal(5123, port.TargetPort);
                    Assert.Equal("http", port.Protocol);
                },
                port =>
                {
                    Assert.Equal("https", port.Name);
                    Assert.Equal(7123, port.TargetPort);
                    Assert.Equal("https", port.Protocol);
                });
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryReadLaunchSettingsEndpointPorts_ReturnsEmptyWhenFileIsMissing()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var factory = new AspNetCoreProjectEndpointDefinitionFactory(contentRoot);

        Assert.Empty(factory.TryReadLaunchSettingsEndpointPorts("src/API/API.csproj"));
    }

    private static void WriteLaunchSettings(
        string contentRoot,
        string projectPath,
        string json)
    {
        var resolvedProjectPath = Path.GetFullPath(projectPath, contentRoot);
        var launchSettingsDirectory = Path.Combine(
            Path.GetDirectoryName(resolvedProjectPath)!,
            "Properties");
        Directory.CreateDirectory(launchSettingsDirectory);
        File.WriteAllText(Path.Combine(launchSettingsDirectory, "launchSettings.json"), json);
    }
}
