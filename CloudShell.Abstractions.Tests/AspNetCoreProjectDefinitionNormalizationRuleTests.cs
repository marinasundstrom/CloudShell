using CloudShell.Providers.Applications;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Abstractions.Tests;

public sealed class AspNetCoreProjectDefinitionNormalizationRuleTests
{
    [Fact]
    public void Normalize_ExtractsLegacyDotNetProjectArgumentsForAspNetCoreProjects()
    {
        var normalizer = CreateNormalizer();
        var definition = new ApplicationResourceDefinition(
            "application:api",
            "API",
            "dotnet",
            arguments: "watch --project src/API/API.csproj run --no-launch-profile -- --seed",
            resourceType: ApplicationResourceTypes.AspNetCoreProject);

        var normalized = normalizer.Normalize(definition);

        Assert.Empty(normalized.ExecutablePath);
        Assert.Null(normalized.Arguments);
        Assert.Equal("src/API/API.csproj", normalized.ProjectPath);
        Assert.Equal("--seed", normalized.ProjectArguments);
        Assert.True(normalized.AspNetCoreHotReload);
    }

    [Fact]
    public void Normalize_ClearsProjectFieldsForNonProjectBackedApplications()
    {
        var normalizer = CreateNormalizer();
        var definition = new ApplicationResourceDefinition(
            "application:worker",
            "Worker",
            "dotnet",
            arguments: "--project src/Worker/Worker.csproj -- --seed",
            projectPath: "src/Worker/Worker.csproj",
            projectArguments: "--seed",
            useLaunchSettingsEndpoints: true);

        var normalized = normalizer.Normalize(definition);

        Assert.Null(normalized.ProjectPath);
        Assert.Null(normalized.ProjectArguments);
        Assert.False(normalized.ProjectContainerBuild);
        Assert.False(normalized.UseLaunchSettingsEndpoints);
        Assert.Equal("dotnet", normalized.ExecutablePath);
        Assert.Equal("--project src/Worker/Worker.csproj -- --seed", normalized.Arguments);
    }

    [Fact]
    public void Normalize_PreservesProjectContainerBuildFieldsWithoutAspNetCoreLegacyParsing()
    {
        var normalizer = CreateNormalizer();
        var definition = new ApplicationResourceDefinition(
            "application:api",
            "API",
            "dotnet",
            arguments: "--project ignored.csproj -- --ignored",
            resourceType: ApplicationResourceTypes.ContainerApp,
            projectPath: "src/API/API.csproj",
            projectArguments: "--seed",
            projectContainerBuild: true,
            useLaunchSettingsEndpoints: true);

        var normalized = normalizer.Normalize(definition);

        Assert.Empty(normalized.ExecutablePath);
        Assert.Null(normalized.Arguments);
        Assert.Equal("src/API/API.csproj", normalized.ProjectPath);
        Assert.Equal("--seed", normalized.ProjectArguments);
        Assert.True(normalized.ProjectContainerBuild);
        Assert.False(normalized.UseLaunchSettingsEndpoints);
    }

    private static ApplicationResourceDefinitionNormalizer CreateNormalizer() =>
        new(
            new TestHostEnvironment(Path.GetTempPath()),
            [
                new ProjectBackedApplicationResourceDefinitionNormalizationRule(),
                new AspNetCoreProjectDefinitionNormalizationRule()
            ]);

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
