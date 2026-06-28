using CloudShell.ApplicationTopologyHost;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;
using GraphResourceState = CloudShell.ResourceDefinitions.ResourceState;

namespace CloudShell.Sample.Tests;

public sealed class ApplicationTopologyGraphSqlServerRuntimeHandlerTests
{
    [Fact]
    public async Task ExecuteLifecycle_DelegatesMappedSqlServerResourceToBridge()
    {
        var bridge = new RecordingGraphSqlServerRuntimeBridge(SqlServerRuntimeStatus.Running);
        var handler = new ApplicationTopologyGraphSqlServerRuntimeHandler(bridge);
        var resource = CreateGraphSqlServerResource();

        Assert.Equal(SqlServerRuntimeStatus.Running, handler.GetStatus(resource));

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        var command = Assert.Single(bridge.LifecycleCommands);
        Assert.Equal(
            "application.sql-server:application-topology-sql-server",
            command.Resource.EffectiveResourceId);
        Assert.Equal(SqlServerResourceTypeProvider.Operations.Start, command.OperationId);
    }

    [Fact]
    public async Task ExecuteLifecycle_IgnoresUnmappedSqlServerResourceWithoutCallingBridge()
    {
        var bridge = new RecordingGraphSqlServerRuntimeBridge(SqlServerRuntimeStatus.Running);
        var handler = new ApplicationTopologyGraphSqlServerRuntimeHandler(bridge);
        var resource = CreateGraphSqlServerResource(
            "other-sql",
            "application.sql-server:other-sql");

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Empty(bridge.LifecycleCommands);
        Assert.Equal(SqlServerRuntimeStatus.Unknown, handler.GetStatus(resource));
    }

    private static GraphResource CreateGraphSqlServerResource(
        string name = "application-topology-sql-server",
        string resourceId = "application.sql-server:application-topology-sql-server")
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

    private sealed class RecordingGraphSqlServerRuntimeBridge(
        SqlServerRuntimeStatus status) : IApplicationTopologyGraphSqlServerRuntimeBridge
    {
        public List<LifecycleCommand> LifecycleCommands { get; } = [];

        public SqlServerRuntimeStatus GetStatus(GraphResource resource) => status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleCommands.Add(new(resource, operationId));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed record LifecycleCommand(
        GraphResource Resource,
        ResourceOperationId OperationId);
}
