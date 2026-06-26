using CloudShell.Abstractions.ControlPlane;
using CloudShell.ApplicationTopologyHost;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.DependencyInjection;
using GraphResource = CloudShell.ResourceDefinitions.Resource;
using GraphResourceState = CloudShell.ResourceDefinitions.ResourceState;

namespace CloudShell.Sample.Tests;

public sealed class ApplicationTopologyGraphSqlServerRuntimeHandlerTests
{
    [Theory]
    [InlineData("start", "start", true, false, SqlServerRuntimeStatus.Running)]
    [InlineData("stop", "stop", false, true, SqlServerRuntimeStatus.Stopped)]
    [InlineData("restart", "restart", true, true, SqlServerRuntimeStatus.Running)]
    public async Task ExecuteLifecycle_DelegatesToRuntimeSqlServerResource(
        string graphOperationId,
        string expectedActionId,
        bool expectedStartDependencies,
        bool expectedIgnoreDependentWarning,
        SqlServerRuntimeStatus expectedStatus)
    {
        var resourceManager = new RecordingResourceManager();
        var handler = CreateHandler(resourceManager);
        var resource = CreateGraphSqlServerResource();

        Assert.Equal(SqlServerRuntimeStatus.Unknown, handler.GetStatus(resource));

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            graphOperationId);

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ActionCommands);
        Assert.Equal("application:application-topology-sql-server", command.ResourceId);
        Assert.Equal(expectedActionId, command.ActionId);
        Assert.Equal(expectedStartDependencies, command.StartDependencies);
        Assert.Equal(expectedIgnoreDependentWarning, command.IgnoreDependentWarning);
        Assert.Equal(expectedStatus, handler.GetStatus(resource));
    }

    [Fact]
    public async Task ExecuteLifecycle_IgnoresUnmappedSqlServerResource()
    {
        var resourceManager = new RecordingResourceManager();
        var handler = CreateHandler(resourceManager);
        var resource = CreateGraphSqlServerResource(
            "other-sql",
            "application.sql-server:other-sql");

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Empty(resourceManager.ActionCommands);
        Assert.Equal(SqlServerRuntimeStatus.Unknown, handler.GetStatus(resource));
    }

    private static ApplicationTopologyGraphSqlServerRuntimeHandler CreateHandler(
        IResourceManager resourceManager)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resourceManager);
        var serviceProvider = services.BuildServiceProvider();
        return new(serviceProvider.GetRequiredService<IServiceScopeFactory>());
    }

    private static GraphResource CreateGraphSqlServerResource(
        string name = "graph-application-topology-sql-server",
        string resourceId = "application.sql-server:graph-application-topology-sql-server")
    {
        var resolver = new ResourceResolver(
            [SqlServerResourceTypeProvider.ClassDefinition],
            [new SqlServerResourceTypeProvider().TypeDefinition]);

        return resolver.Resolve(new GraphResourceState(
            name,
            SqlServerResourceTypeProvider.ResourceTypeId,
            ResourceId: resourceId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId));
    }
}
