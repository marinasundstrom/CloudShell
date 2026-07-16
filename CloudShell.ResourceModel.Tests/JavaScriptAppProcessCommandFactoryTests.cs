using CloudShell.ControlPlane.Providers;
using ResourceGraphState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.ResourceModel.Tests;

public sealed class JavaScriptAppProcessCommandFactoryTests
{
    [Fact]
    public void CreateStartInfo_UsesNpmCmdOnWindows()
    {
        var resource = CreateResource(packageManager: "npm");

        var startInfo = new JavaScriptAppProcessCommandFactory(
                new JavaScriptAppProcessCommandPlatform(IsWindows: true))
            .CreateStartInfo(resource, "/repo/frontend");

        Assert.Equal("npm.cmd", startInfo.FileName);
        Assert.Equal(["run", "dev"], startInfo.ArgumentList.ToArray());
        Assert.Equal("/repo/frontend", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void CreateStartInfo_UsesNpmOnUnix()
    {
        var resource = CreateResource(packageManager: "npm");

        var startInfo = new JavaScriptAppProcessCommandFactory(
                new JavaScriptAppProcessCommandPlatform(IsWindows: false))
            .CreateStartInfo(resource, "/repo/frontend");

        Assert.Equal("npm", startInfo.FileName);
        Assert.Equal(["run", "dev"], startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void CreateStartInfo_DoesNotRewriteNonNpmPackageManagerOnWindows()
    {
        var resource = CreateResource(packageManager: "pnpm", script: "start", arguments: "--host 0.0.0.0");

        var startInfo = new JavaScriptAppProcessCommandFactory(
                new JavaScriptAppProcessCommandPlatform(IsWindows: true))
            .CreateStartInfo(resource, "/repo/frontend");

        Assert.Equal("pnpm", startInfo.FileName);
        Assert.Equal(["run", "start", "--", "--host", "0.0.0.0"], startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void CreateStartInfo_UsesBunPackageManager()
    {
        var resource = CreateResource(packageManager: "bun", script: "dev", arguments: "--hot");

        var startInfo = new JavaScriptAppProcessCommandFactory(
                new JavaScriptAppProcessCommandPlatform(IsWindows: false))
            .CreateStartInfo(resource, "/repo/frontend");

        Assert.Equal("bun", startInfo.FileName);
        Assert.Equal(["run", "dev", "--", "--hot"], startInfo.ArgumentList.ToArray());
    }

    private static Resource CreateResource(
        string? packageManager = null,
        string? script = null,
        string? arguments = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [JavaScriptAppResourceTypeProvider.Attributes.ProjectPath] = "src/frontend"
        };

        if (packageManager is not null)
        {
            attributes[JavaScriptAppResourceTypeProvider.Attributes.PackageManager] = packageManager;
        }

        if (script is not null)
        {
            attributes[JavaScriptAppResourceTypeProvider.Attributes.Script] = script;
        }

        if (arguments is not null)
        {
            attributes[JavaScriptAppResourceTypeProvider.Attributes.Arguments] = arguments;
        }

        var resolver = new ResourceResolver(
            [JavaScriptAppResourceTypeProvider.ClassDefinition],
            [new JavaScriptAppResourceTypeProvider().TypeDefinition]);

        return resolver.Resolve(new ResourceGraphState(
            "frontend",
            JavaScriptAppResourceTypeProvider.ResourceTypeId,
            ProviderId: JavaScriptAppResourceTypeProvider.ProviderId,
            Attributes: attributes));
    }
}
