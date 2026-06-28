using CloudShell.ApplicationTopologyHost;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;
using ResourceModelResourceState = CloudShell.ResourceDefinitions.ResourceState;

namespace CloudShell.Sample.Tests;

public sealed class ApplicationTopologyResourceModelSqlServerRuntimeHandlerTests
{
    [Fact]
    public async Task ExecuteLifecycle_DelegatesMappedSqlServerResourceToBridge()
    {
        var bridge = new RecordingResourceModelSqlServerRuntimeBridge(SqlServerRuntimeStatus.Running);
        var handler = new ApplicationTopologyResourceModelSqlServerRuntimeHandler(bridge);
        var resource = CreateResourceModelSqlServerResource();

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
        var bridge = new RecordingResourceModelSqlServerRuntimeBridge(SqlServerRuntimeStatus.Running);
        var handler = new ApplicationTopologyResourceModelSqlServerRuntimeHandler(bridge);
        var resource = CreateResourceModelSqlServerResource(
            "other-sql",
            "application.sql-server:other-sql");

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Empty(bridge.LifecycleCommands);
        Assert.Equal(SqlServerRuntimeStatus.Unknown, handler.GetStatus(resource));
    }

    private static ResourceModelResource CreateResourceModelSqlServerResource(
        string name = "application-topology-sql-server",
        string resourceId = "application.sql-server:application-topology-sql-server")
    {
        var resolver = new ResourceResolver(
            [SqlServerResourceTypeProvider.ClassDefinition],
            [new SqlServerResourceTypeProvider().TypeDefinition]);

        return resolver.Resolve(new ResourceModelResourceState(
            name,
            SqlServerResourceTypeProvider.ResourceTypeId,
            ResourceId: resourceId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId));
    }

    private sealed class RecordingResourceModelSqlServerRuntimeBridge(
        SqlServerRuntimeStatus status) : IApplicationTopologyResourceModelSqlServerRuntimeBridge
    {
        public List<LifecycleCommand> LifecycleCommands { get; } = [];

        public SqlServerRuntimeStatus GetStatus(ResourceModelResource resource) => status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            ResourceModelResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleCommands.Add(new(resource, operationId));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed record LifecycleCommand(
        ResourceModelResource Resource,
        ResourceOperationId OperationId);
}
