using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ResourceModel;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Sample.Tests;

public sealed class YamlAppHostSampleTests
{
    [Fact]
    public async Task Template_DeclaresApplicableDotnetProjectFromSampleDirectory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sampleDirectory = Path.Combine(repositoryRoot, "samples", "YamlAppHost");
        var templatePath = Path.Combine(sampleDirectory, "cloudshell.yaml");
        var projectPath = Path.Combine(
            sampleDirectory,
            "App",
            "CloudShell.YamlSampleApp.csproj");
        Assert.True(File.Exists(projectPath));

        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddBuiltInResourceModelProviderTypes();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var document = await File.ReadAllTextAsync(templatePath);
        var template = ResourceTemplateSerializer.DeserializeTemplate(
            document,
            ResourceTemplateFormat.Yaml,
            new ResourceTemplateSerializerOptions(
                serviceProvider.GetRequiredService<ResourceDefinitionSchemaCatalog>()));

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 8, 13, 18, 0, 0, TimeSpan.Zero)));

        Assert.False(
            result.HasErrors,
            string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        Assert.Contains(snapshot.Resources, resource =>
            resource.EffectiveResourceId == "application.dotnet-app:yaml-sample-api");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CloudShell.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CloudShell repository root.");
    }
}
