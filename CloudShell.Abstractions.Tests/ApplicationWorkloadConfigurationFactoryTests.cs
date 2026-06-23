using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationWorkloadConfigurationFactoryTests
{
    private readonly ApplicationWorkloadConfigurationFactory factory = new();

    [Fact]
    public void Create_MapsContainerImageWorkloadAndCommonSettings()
    {
        var variables = new[]
        {
            new EnvironmentVariableAssignment("API_URL", "http://api")
        };
        var ports = new[]
        {
            new ServicePort("http", 8080, 5080, "http")
        };
        var volumes = new[]
        {
            new ResourceVolumeMount("storage:data", "/data")
        };
        var observability = new ResourceObservability(Logs: true);
        var application = CreateApplication() with
        {
            ContainerImage = "example/api:v1",
            ContainerRegistry = " registry.local ",
            ContainerHostId = "docker:local",
            Replicas = 0,
            ReplicasEnabled = true,
            AppSettings = [new AppSetting("Setting", "value")],
            EndpointPorts = ports,
            VolumeMounts = volumes,
            Observability = observability,
            Lifetime = ApplicationLifetime.ControlPlaneScoped
        };

        var workload = factory.Create(application, variables, observability);

        Assert.Equal(ResourceWorkloadKind.ContainerImage, workload.Kind);
        Assert.Equal("example/api:v1", workload.Image);
        Assert.Equal("registry.local", workload.Registry);
        Assert.Equal("docker:local", workload.ContainerHostId);
        Assert.Equal(1, workload.Replicas);
        Assert.True(workload.ReplicasEnabled);
        Assert.Equal(ResourceLifetime.ControlPlaneScoped, workload.Lifetime);
        Assert.Equal(variables, workload.WorkloadEnvironmentVariables);
        Assert.Equal(ports, workload.WorkloadPorts);
        Assert.Equal(volumes, workload.WorkloadVolumeMounts);
        Assert.Equal(observability, workload.EffectiveObservability);
    }

    [Fact]
    public void Create_MapsContainerBuildContextWorkload()
    {
        var application = CreateApplication() with
        {
            ContainerBuildContext = "/src",
            ContainerDockerfile = "Dockerfile.api",
            ProjectPath = "api.csproj",
            ProjectArguments = "--watch",
            ContainerRegistry = null,
            ContainerHostId = "docker:local"
        };

        var workload = factory.Create(application, [], ResourceObservability.None);

        Assert.Equal(ResourceWorkloadKind.ContainerBuild, workload.Kind);
        Assert.Equal("/src", workload.BuildContext);
        Assert.Equal("Dockerfile.api", workload.Dockerfile);
        Assert.Equal("api.csproj", workload.ProjectPath);
        Assert.Equal("--watch", workload.ProjectArguments);
        Assert.Equal(ContainerRegistryDefaults.Default, workload.Registry);
        Assert.Equal("docker:local", workload.ContainerHostId);
    }

    [Fact]
    public void Create_MapsProjectContainerBuildWorkload()
    {
        var application = CreateApplication() with
        {
            ProjectContainerBuild = true,
            ContainerDockerfile = "Dockerfile",
            ProjectPath = "api.csproj",
            ProjectArguments = "--configuration Release"
        };

        var workload = factory.Create(application, [], ResourceObservability.None);

        Assert.Equal(ResourceWorkloadKind.ContainerBuild, workload.Kind);
        Assert.Null(workload.BuildContext);
        Assert.Equal("Dockerfile", workload.Dockerfile);
        Assert.Equal("api.csproj", workload.ProjectPath);
        Assert.Equal("--configuration Release", workload.ProjectArguments);
    }

    [Fact]
    public void Create_MapsAspNetCoreProjectWorkload()
    {
        var application = CreateApplication(ApplicationResourceTypes.AspNetCoreProject) with
        {
            WorkingDirectory = "/src/api",
            ProjectPath = "api.csproj",
            ProjectArguments = "--urls http://localhost:5011",
            AspNetCoreHotReload = true,
            Replicas = 3,
            ReplicasEnabled = true
        };

        var workload = factory.Create(application, [], ResourceObservability.None);

        Assert.Equal(ResourceWorkloadKind.AspNetCoreProject, workload.Kind);
        Assert.Equal("/src/api", workload.WorkingDirectory);
        Assert.Equal("api.csproj", workload.ProjectPath);
        Assert.Equal("--urls http://localhost:5011", workload.ProjectArguments);
        Assert.True(workload.AspNetCoreHotReload);
        Assert.Equal(3, workload.Replicas);
        Assert.False(workload.ReplicasEnabled);
    }

    [Fact]
    public void Create_MapsLocalExecutableWorkload()
    {
        var application = CreateApplication(ApplicationResourceTypes.ExecutableApplication) with
        {
            ExecutablePath = "/bin/api",
            Arguments = "--run",
            WorkingDirectory = "/tmp",
            Replicas = 4,
            ReplicasEnabled = true,
            Lifetime = ApplicationLifetime.Detached
        };

        var workload = factory.Create(application, [], ResourceObservability.None);

        Assert.Equal(ResourceWorkloadKind.LocalExecutable, workload.Kind);
        Assert.Equal("/bin/api", workload.ExecutablePath);
        Assert.Equal("--run", workload.Arguments);
        Assert.Equal("/tmp", workload.WorkingDirectory);
        Assert.Equal(4, workload.Replicas);
        Assert.False(workload.ReplicasEnabled);
        Assert.Equal(ResourceLifetime.Detached, workload.Lifetime);
    }

    [Fact]
    public void Create_PrefersContainerImageOverOtherWorkloadInputs()
    {
        var application = CreateApplication() with
        {
            ContainerImage = "example/api:v1",
            ContainerBuildContext = "/src",
            ProjectContainerBuild = true,
            ProjectPath = "api.csproj"
        };

        var workload = factory.Create(application, [], ResourceObservability.None);

        Assert.Equal(ResourceWorkloadKind.ContainerImage, workload.Kind);
        Assert.Equal("example/api:v1", workload.Image);
        Assert.Null(workload.BuildContext);
        Assert.Null(workload.ProjectPath);
    }

    private static ApplicationResourceDefinition CreateApplication(
        string resourceType = ApplicationResourceTypes.ContainerApp) =>
        new(
            "application:api",
            "api",
            executablePath: "api",
            resourceType: resourceType);
}
