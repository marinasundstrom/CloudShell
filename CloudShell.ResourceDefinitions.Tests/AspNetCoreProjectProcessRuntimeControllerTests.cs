using CloudShell.ResourceDefinitions.ReferenceProviders;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class AspNetCoreProjectProcessRuntimeControllerTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsDiagnosticWhenProjectFileIsMissing()
    {
        var resolver = new ResourceResolver(
            [AspNetCoreProjectResourceTypeProvider.ClassDefinition],
            [new AspNetCoreProjectResourceTypeProvider().TypeDefinition]);
        var resource = resolver.Resolve(new ResourceGraphState(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    "missing/CloudShell.Missing.csproj"
            }));
        var controller = new AspNetCoreProjectProcessRuntimeController();

        var diagnostics = await controller.ExecuteAsync(
            resource,
            AspNetCoreProjectResourceTypeProvider.Operations.Start);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("application.aspNetCoreProject.projectFileMissing", diagnostic.Code);
        Assert.Equal(resource.EffectiveResourceId, diagnostic.Target);
    }
}
