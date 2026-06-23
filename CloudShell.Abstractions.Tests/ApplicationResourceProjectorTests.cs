using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationResourceProjectorTests
{
    private static readonly ApplicationResourceProjection ExecutableProjection = new(
        _ => true,
        _ => "Executable application",
        application => Path.GetFileName(application.ExecutablePath),
        _ => ResourceWorkloadKind.LocalExecutable.ToString(),
        _ => ResourceClass.Executable);

    private static readonly ApplicationResourceProjection ContainerProjection = new(
        _ => true,
        _ => "Container app",
        ApplicationResourceProjectionSupport.GetContainerVersion,
        ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
        _ => ResourceClass.Container);

    private static readonly ApplicationResourceProjection SqlServerProjection = new(
        _ => true,
        _ => "SQL Server",
        ApplicationResourceProjectionSupport.GetContainerVersion,
        ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
        _ => ResourceClass.Service);

    [Fact]
    public void CreateResource_ProjectsEndpointAndNetworkMapping()
    {
        var projector = CreateProjector();
        var application = new ApplicationResourceDefinition(
            "application:api",
            "API",
            executablePath: "dotnet",
            endpointPorts:
            [
                new ServicePort("http", 8080, 5011, "http")
            ]);

        var resource = projector.CreateResource(application, ExecutableProjection, "Applications");

        Assert.Equal("application:api", resource.Id);
        Assert.Equal("api", resource.Name);
        Assert.Equal("API", resource.DisplayName);
        Assert.Equal("Applications", resource.Provider);
        var endpoint = Assert.Single(resource.Endpoints);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(8080, endpoint.TargetPort);
        var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);
        Assert.Equal("http://localhost:5011", mapping.Address);
    }

    [Fact]
    public void CreateResource_ProjectsSqlServerReconcileAction()
    {
        var projector = CreateProjector();
        var application = new ApplicationResourceDefinition(
            "application:sql",
            "SQL",
            executablePath: string.Empty,
            containerImage: "mssql/server:latest",
            resourceType: ApplicationResourceTypes.SqlServer);

        var resource = projector.CreateResource(application, SqlServerProjection, "Applications");

        Assert.Contains(
            resource.ResourceActions,
            action =>
                action.Id == ApplicationResourceService.ReconcileSqlServerAccessActionId &&
                action.RequiredPermission == DatabaseResourceOperationPermissions.ReconcileAccess);
    }

    [Fact]
    public void CreateResource_DoesNotProjectParentHealthChecksForReplicatedContainerApp()
    {
        var projector = CreateProjector();
        var application = new ApplicationResourceDefinition(
            "application:api",
            "API",
            executablePath: string.Empty,
            containerImage: "example/api:latest",
            replicas: 2,
            resourceType: ApplicationResourceTypes.ContainerApp,
            healthChecks:
            [
                new ResourceHealthCheck(
                    "/healthz",
                    ResourceProbeType.Health,
                    EndpointName: "http",
                    Name: "health",
                    Source: ResourceProbeSource.ForHttp("/healthz", "http"))
            ],
            replicasEnabled: true);

        var resource = projector.CreateResource(application, ContainerProjection, "Applications");

        Assert.Empty(resource.ResourceHealthChecks);
    }

    private static ApplicationResourceProjector CreateProjector()
    {
        var workloads = new ApplicationWorkloadConfigurationFactory();
        var deployments = new ApplicationContainerOrchestratorDeploymentFactory();
        return new ApplicationResourceProjector(
            new FakeRuntimeStateStore(),
            _ => ResourceState.Running,
            _ => ResourceObservability.None,
            (application, state, runtimeRevisionScoped) => deployments.CreateDeployment(
                application,
                state,
                workloads.Create(application, [], ResourceObservability.None),
                runtimeRevisionScoped),
            (_, port) => port.Port ?? 25000);
    }

    private sealed class FakeRuntimeStateStore : IApplicationRuntimeStateStore
    {
        public ApplicationRuntimeState? Get(string applicationId) => null;

        public void Save(ApplicationRuntimeState state)
        {
        }
    }
}
