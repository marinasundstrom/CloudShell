using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class AspNetCoreProjectRuntimeTests
{
    [Fact]
    public void BuildDotNetRunArguments_UsesDotNetRunByDefault()
    {
        var arguments = AspNetCoreProjectProcessDefinitions.BuildDotNetRunArguments(
            "src/API/API.csproj",
            hotReload: false,
            applicationArguments: "--seed");

        Assert.Equal("run --project src/API/API.csproj --no-build --no-launch-profile -- --seed", arguments);
    }

    [Fact]
    public void BuildDotNetRunArguments_UsesNonInteractiveWatchWhenHotReloadIsEnabled()
    {
        var arguments = AspNetCoreProjectProcessDefinitions.BuildDotNetRunArguments(
            "src/API/API.csproj",
            hotReload: true,
            applicationArguments: "--seed");

        Assert.Equal(
            "watch --non-interactive --project src/API/API.csproj run --no-launch-profile -- --seed",
            arguments);
    }

    [Fact]
    public void Create_AddsUrlsAndRudeEditRestartEnvironmentWhenHotReloadIsEnabled()
    {
        var application = CreateApplication(aspNetCoreHotReload: true);
        var factory = new AspNetCoreProjectEnvironmentFactory(
            _ =>
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    application.Id,
                    "http",
                    "http://localhost:5127",
                    ResourceExposureScope.Public,
                    sourceEndpointName: "http")
            ]);

        var environment = factory.Create(application)
            .ToDictionary(variable => variable.Name, variable => variable.Value);

        Assert.Equal(
            "http://localhost:5127",
            environment[AspNetCoreProjectEnvironmentFactory.AspNetCoreUrlsEnvironmentVariable]);
        Assert.Equal(
            "true",
            environment[AspNetCoreProjectEnvironmentFactory.DotNetWatchRestartOnRudeEditEnvironmentVariable]);
    }

    [Fact]
    public void Create_UsesProjectedEndpointNetworkMappingsForUrls()
    {
        var application = CreateApplication();
        var resource = new Resource(
            application.Id,
            "api",
            "ASP.NET Core project",
            "Applications",
            "local",
            ResourceState.Stopped,
            [ResourceEndpoint.FromAddress("http", "http://localhost:5127", "http", ResourceExposureScope.Local, 80)],
            "project",
            DateTimeOffset.UtcNow,
            [],
            TypeId: ApplicationResourceTypes.AspNetCoreProject,
            ResourceClass: ResourceClass.Project,
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    application.Id,
                    "http",
                    "http://127.0.0.2:6000",
                    ResourceExposureScope.Private,
                    sourceEndpointName: "http")
            ]);
        var resourceManager = new StaticResourceManagerStore([resource]);
        var factory = new AspNetCoreProjectEnvironmentFactory(_ => []);

        var environment = factory.Create(application, resourceManager)
            .ToDictionary(variable => variable.Name, variable => variable.Value);

        Assert.Equal(
            "http://127.0.0.2:6000",
            environment[AspNetCoreProjectEnvironmentFactory.AspNetCoreUrlsEnvironmentVariable]);
    }

    [Fact]
    public void Create_DoesNotProjectUrlsForNonAspNetCoreProjectResources()
    {
        var application = new ApplicationResourceDefinition(
            "application:worker",
            "worker",
            "/bin/worker");
        var factory = new AspNetCoreProjectEnvironmentFactory(
            _ =>
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    application.Id,
                    "http",
                    "http://localhost:5127",
                    ResourceExposureScope.Public,
                    sourceEndpointName: "http")
            ]);

        var environment = factory.Create(application);

        Assert.DoesNotContain(
            environment,
            variable => string.Equals(
                variable.Name,
                AspNetCoreProjectEnvironmentFactory.AspNetCoreUrlsEnvironmentVariable,
                StringComparison.OrdinalIgnoreCase));
    }

    private static ApplicationResourceDefinition CreateApplication(bool aspNetCoreHotReload = false) =>
        new(
            "application:api",
            "API",
            string.Empty,
            endpointPorts:
            [
                new ServicePort("http", 80, 5127, "http")
            ],
            resourceType: ApplicationResourceTypes.AspNetCoreProject,
            projectPath: "src/API/API.csproj",
            aspNetCoreHotReload: aspNetCoreHotReload);

    private sealed class StaticResourceManagerStore(IReadOnlyList<Resource> resources) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<Resource> GetAvailableResources() => resources;

        public IReadOnlyList<Resource> GetResources() => resources;

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string id) =>
            resources.FirstOrDefault(resource =>
                string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) => GetResource(resourceId) is not null;
    }
}
