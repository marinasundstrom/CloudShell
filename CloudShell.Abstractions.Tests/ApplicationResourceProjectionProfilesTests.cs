using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationResourceProjectionProfilesTests
{
    [Fact]
    public void CreateInfrastructureProjection_ProjectsAspNetCoreProjectAsProjectResource()
    {
        var application = new ApplicationResourceDefinition(
            "application:web",
            "web",
            string.Empty,
            projectPath: "samples/Web/Web.csproj",
            resourceType: ApplicationResourceTypes.AspNetCoreProject);

        var projection = ApplicationResourceProjectionProfiles.CreateInfrastructureProjection(application);

        Assert.Equal("ASP.NET Core project", projection.GetResourceKind(application));
        Assert.Equal("Web.csproj", projection.GetResourceVersion(application));
        Assert.Equal(ResourceWorkloadKind.AspNetCoreProject.ToString(), projection.GetWorkloadKind(application));
        Assert.Equal(ResourceClass.Project, projection.GetResourceClass(application));
    }

    [Fact]
    public void CreateInfrastructureProjection_ProjectsContainerAppAsContainerResource()
    {
        var application = new ApplicationResourceDefinition(
            "application:api",
            "api",
            string.Empty,
            containerImage: "example/api:1.0",
            resourceType: ApplicationResourceTypes.ContainerApp);

        var projection = ApplicationResourceProjectionProfiles.CreateInfrastructureProjection(application);

        Assert.Equal("Container app", projection.GetResourceKind(application));
        Assert.Equal("unrevisioned", projection.GetResourceVersion(application));
        Assert.Equal(ResourceWorkloadKind.ContainerImage.ToString(), projection.GetWorkloadKind(application));
        Assert.Equal(ResourceClass.Container, projection.GetResourceClass(application));
    }

    [Fact]
    public void CreateInfrastructureProjection_ProjectsExecutableAsExecutableResource()
    {
        var application = new ApplicationResourceDefinition(
            "application:worker",
            "worker",
            "/opt/acme/worker.exe",
            resourceType: ApplicationResourceTypes.ExecutableApplication);

        var projection = ApplicationResourceProjectionProfiles.CreateInfrastructureProjection(application);

        Assert.Equal("Executable application", projection.GetResourceKind(application));
        Assert.Equal("worker.exe", projection.GetResourceVersion(application));
        Assert.Equal(ResourceWorkloadKind.LocalExecutable.ToString(), projection.GetWorkloadKind(application));
        Assert.Equal(ResourceClass.Executable, projection.GetResourceClass(application));
    }
}
