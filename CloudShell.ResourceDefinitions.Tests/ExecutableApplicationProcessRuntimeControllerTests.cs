using CloudShell.ResourceDefinitions.ReferenceProviders;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ExecutableApplicationProcessRuntimeControllerTests
{
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

    private static Resource CreateResource(string executablePath)
    {
        var resolver = new ResourceResolver(
            [ExecutableApplicationResourceTypeProvider.ClassDefinition],
            [new ExecutableApplicationResourceTypeProvider().TypeDefinition]);

        return resolver.Resolve(new ResourceGraphState(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = executablePath
            }));
    }
}
