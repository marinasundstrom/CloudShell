using CloudShell.ResourceDefinitions.ReferenceProviders;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class AspNetCoreProjectProcessRuntimeControllerTests
{
    [Fact]
    public void CommandFactory_CreatesRunCommandFromGraphAttributes()
    {
        var resource = CreateResource(
            "src/Api/Api.csproj",
            arguments: "--urls http://localhost:5229",
            hotReload: false,
            useLaunchSettings: false);
        var command = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(resource, "/repo/src/Api/Api.csproj");

        Assert.Equal("dotnet", command.FileName);
        Assert.Equal(
            "run --project \"/repo/src/Api/Api.csproj\" --no-launch-profile -- --urls http://localhost:5229",
            command.Arguments);
        Assert.Equal("/repo/src/Api", command.WorkingDirectory);
        Assert.False(command.UseShellExecute);
        Assert.True(command.RedirectStandardOutput);
        Assert.True(command.RedirectStandardError);
        Assert.Equal(resource.EffectiveResourceId, command.Environment[AspNetCoreProjectEnvironmentNames.ResourceId]);
        Assert.Equal(resource.Name, command.Environment[AspNetCoreProjectEnvironmentNames.ResourceName]);
    }

    [Fact]
    public void CommandFactory_CreatesWatchCommandWhenHotReloadIsEnabled()
    {
        var resource = CreateResource(
            "src/Api/Api.csproj",
            hotReload: true,
            useLaunchSettings: false);
        var command = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(resource, "/repo/src/Api/Api.csproj");

        Assert.Equal(
            "watch --project \"/repo/src/Api/Api.csproj\" run --no-launch-profile",
            command.Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsDiagnosticWhenProjectFileIsMissing()
    {
        var resource = CreateResource("missing/CloudShell.Missing.csproj");
        var controller = new AspNetCoreProjectProcessRuntimeController();

        var diagnostics = await controller.ExecuteAsync(
            resource,
            AspNetCoreProjectResourceTypeProvider.Operations.Start);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("application.aspNetCoreProject.projectFileMissing", diagnostic.Code);
        Assert.Equal(resource.EffectiveResourceId, diagnostic.Target);
    }

    private static Resource CreateResource(
        string projectPath,
        string? arguments = null,
        bool? hotReload = null,
        bool? useLaunchSettings = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, string>
        {
            [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] = projectPath
        };

        if (arguments is not null)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] =
                arguments;
        }

        if (hotReload.HasValue)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] =
                hotReload.Value.ToString().ToLowerInvariant();
        }

        if (useLaunchSettings.HasValue)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                useLaunchSettings.Value.ToString().ToLowerInvariant();
        }

        var resolver = new ResourceResolver(
            [AspNetCoreProjectResourceTypeProvider.ClassDefinition],
            [new AspNetCoreProjectResourceTypeProvider().TypeDefinition]);

        return resolver.Resolve(new ResourceGraphState(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: attributes));
    }
}
