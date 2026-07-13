using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CloudShell.ControlPlane.Providers;
using ResourceGraphState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.ResourceModel.Tests;

public sealed class ExecutableApplicationProcessRuntimeControllerTests
{
    [Fact]
    public void CreateStartInfo_AppliesCommandConfiguration()
    {
        var resource = CreateResource(
            "dotnet",
            new ExecutableApplicationConfiguration(
                "dotnet",
                "run --project src/Api/Api.csproj",
                "/repo/src/Api"));

        var startInfo = CreateStartInfo(
            resource,
            "dotnet",
            resource.Attributes.GetObject<ExecutableApplicationConfiguration>(
                ExecutableApplicationResourceTypeProvider.Attributes.Command));

        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal("run --project src/Api/Api.csproj", startInfo.Arguments);
        Assert.Equal("/repo/src/Api", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(resource.EffectiveResourceId, startInfo.Environment[ExecutableApplicationEnvironmentNames.ResourceId]);
        Assert.Equal(resource.Name, startInfo.Environment[ExecutableApplicationEnvironmentNames.ResourceName]);
    }

    [Fact]
    public void CreateStartInfo_PreservesExecutablePathWithSpaces()
    {
        var resource = CreateResource(
            "/repo/tools/My Tool/run-app",
            new ExecutableApplicationConfiguration(
                "/repo/tools/My Tool/run-app",
                "--config appsettings.Development.json",
                "/repo/tools/My Tool"));

        var startInfo = CreateStartInfo(
            resource,
            "/repo/tools/My Tool/run-app",
            resource.Attributes.GetObject<ExecutableApplicationConfiguration>(
                ExecutableApplicationResourceTypeProvider.Attributes.Command));

        Assert.Equal("/repo/tools/My Tool/run-app", startInfo.FileName);
        Assert.Equal("--config appsettings.Development.json", startInfo.Arguments);
        Assert.Equal("/repo/tools/My Tool", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void CreateStartInfo_UsesEmptyArgumentsWhenNotConfigured()
    {
        var resource = CreateResource("/repo/tools/api");

        var startInfo = CreateStartInfo(resource, "/repo/tools/api", configuration: null);

        Assert.Equal("/repo/tools/api", startInfo.FileName);
        Assert.Equal(string.Empty, startInfo.Arguments);
        Assert.Equal(string.Empty, startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public async Task StartAsync_ReturnsDiagnosticWhenExecutablePathIsMissing()
    {
        var resource = CreateResource("");
        await using var controller = new ExecutableApplicationProcessRuntimeController();

        var diagnostics = await controller.StartAsync(resource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("application.executable.pathRequired", diagnostic.Code);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, diagnostic.Target);
    }

    [Fact]
    public async Task StartAsync_ReturnsDiagnosticWhenProcessCannotStart()
    {
        var missingExecutable = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-missing-executable-" + Guid.NewGuid().ToString("N"));
        var resource = CreateResource(missingExecutable);
        await using var controller = new ExecutableApplicationProcessRuntimeController();

        var diagnostics = await controller.StartAsync(resource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("application.executable.processStartFailed", diagnostic.Code);
        Assert.Equal(resource.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task StartAsync_UsesConfigurationPathWhenAttributeIsMissing()
    {
        var missingExecutable = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-missing-executable-" + Guid.NewGuid().ToString("N"));
        var resource = CreateResource(
            executablePath: null,
            new ExecutableApplicationConfiguration(missingExecutable, "--version"));
        await using var controller = new ExecutableApplicationProcessRuntimeController();

        var diagnostics = await controller.StartAsync(resource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("application.executable.processStartFailed", diagnostic.Code);
        Assert.Equal(resource.EffectiveResourceId, diagnostic.Target);
    }

    private static Resource CreateResource(
        string? executablePath,
        ExecutableApplicationConfiguration? configuration = null)
    {
        var resolver = new ResourceResolver(
            [ExecutableApplicationResourceTypeProvider.ClassDefinition],
            [new ExecutableApplicationResourceTypeProvider().TypeDefinition]);

        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>();
        if (executablePath is not null)
        {
            attributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] =
                executablePath;
        }

        if (configuration is not null)
        {
            attributes[ExecutableApplicationResourceTypeProvider.Attributes.Command] =
                ResourceAttributeValue.FromObject(configuration);
        }

        return resolver.Resolve(new ResourceGraphState(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            Attributes: attributes));
    }

    private static ProcessStartInfo CreateStartInfo(
        Resource resource,
        string executablePath,
        ExecutableApplicationConfiguration? configuration)
    {
        var method = typeof(ExecutableApplicationProcessRuntimeController)
            .GetMethod(
                "CreateStartInfo",
                BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return Assert.IsType<ProcessStartInfo>(method.Invoke(
            null,
            [resource, executablePath, configuration]));
    }
}
