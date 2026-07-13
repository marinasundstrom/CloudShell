using CloudShell.ControlPlane.Providers;
using ResourceGraphState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.ResourceModel.Tests;

public sealed class GoAppProcessCommandFactoryTests
{
    [Fact]
    public void CreateStartInfo_CreatesGoRunCommand()
    {
        var resource = CreateResource(packagePath: "./cmd/api", arguments: "--port 8080");

        var startInfo = new GoAppProcessCommandFactory()
            .CreateStartInfo(resource, "/repo/api");

        Assert.Equal("go", startInfo.FileName);
        Assert.Equal(["run", "./cmd/api", "--port", "8080"], startInfo.ArgumentList.ToArray());
        Assert.Equal("/repo/api", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void CreateStartInfo_ResolvesRelativeBinaryPathAgainstProjectPath()
    {
        var resource = CreateResource(binaryPath: "bin/api", arguments: "--verbose");

        var startInfo = new GoAppProcessCommandFactory(
                new GoAppProcessCommandPlatform(IsWindows: false))
            .CreateStartInfo(resource, "/repo/api");

        Assert.Equal(Path.GetFullPath("bin/api", "/repo/api"), startInfo.FileName);
        Assert.Equal(["--verbose"], startInfo.ArgumentList.ToArray());
        Assert.Equal("/repo/api", startInfo.WorkingDirectory);
    }

    [Fact]
    public void CreateStartInfo_PreservesUnixRootedBinaryPath()
    {
        var resource = CreateResource(binaryPath: "/opt/cloudshell/api");

        var startInfo = new GoAppProcessCommandFactory(
                new GoAppProcessCommandPlatform(IsWindows: false))
            .CreateStartInfo(resource, "/repo/api");

        Assert.Equal("/opt/cloudshell/api", startInfo.FileName);
    }

    [Fact]
    public void CreateStartInfo_PreservesWindowsRootedBinaryPathWhenSimulated()
    {
        var resource = CreateResource(binaryPath: @"C:\cloudshell\api.exe");

        var startInfo = new GoAppProcessCommandFactory(
                new GoAppProcessCommandPlatform(IsWindows: true))
            .CreateStartInfo(resource, "/repo/api");

        Assert.Equal(@"C:\cloudshell\api.exe", startInfo.FileName);
    }

    [Fact]
    public void CreateStartInfo_TreatsWindowsRootedBinaryPathAsRelativeOnUnix()
    {
        var resource = CreateResource(binaryPath: @"C:\cloudshell\api.exe");

        var startInfo = new GoAppProcessCommandFactory(
                new GoAppProcessCommandPlatform(IsWindows: false))
            .CreateStartInfo(resource, "/repo/api");

        Assert.Equal(
            Path.GetFullPath(@"C:\cloudshell\api.exe", "/repo/api"),
            startInfo.FileName);
    }

    private static Resource CreateResource(
        string? packagePath = null,
        string? binaryPath = null,
        string? arguments = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [GoAppResourceTypeProvider.Attributes.ProjectPath] = "src/api"
        };

        if (packagePath is not null)
        {
            attributes[GoAppResourceTypeProvider.Attributes.PackagePath] = packagePath;
        }

        if (binaryPath is not null)
        {
            attributes[GoAppResourceTypeProvider.Attributes.BinaryPath] = binaryPath;
        }

        if (arguments is not null)
        {
            attributes[GoAppResourceTypeProvider.Attributes.Arguments] = arguments;
        }

        var resolver = new ResourceResolver(
            [GoAppResourceTypeProvider.ClassDefinition],
            [new GoAppResourceTypeProvider().TypeDefinition]);

        return resolver.Resolve(new ResourceGraphState(
            "api",
            GoAppResourceTypeProvider.ResourceTypeId,
            ProviderId: GoAppResourceTypeProvider.ProviderId,
            Attributes: attributes));
    }
}
