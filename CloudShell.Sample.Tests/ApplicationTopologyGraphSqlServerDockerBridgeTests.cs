using CloudShell.ApplicationTopologyHost;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.Configuration;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.Sample.Tests;

public sealed class ApplicationTopologyGraphSqlServerDockerBridgeTests
{
    [Fact]
    public void GetStatus_ReturnsUnknownBeforeLifecycleOperation()
    {
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        var bridge = CreateBridge(runner);

        var status = bridge.GetStatus(CreateGraphSqlServerResource());

        Assert.Equal(SqlServerRuntimeStatus.Unknown, status);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task ExecuteStart_CreatesSqlServerContainerWhenMissing()
    {
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var bridge = CreateBridge(
            runner,
            new Dictionary<string, string?>
            {
                ["ApplicationTopology:SqlServer:Password"] = "Configured-Passw0rd!"
            });

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            CreateGraphSqlServerResource(port: 15433),
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Equal(SqlServerRuntimeStatus.Running, bridge.GetStatus(CreateGraphSqlServerResource()));
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-application-topology-graph-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "run -d --name cloudshell-application-topology-graph-sql-server -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Configured-Passw0rd! -p 127.0.0.1:15433:1433 mcr.microsoft.com/mssql/server:2022-latest",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteStart_StartsExistingStoppedContainer()
    {
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        runner.Enqueue(new(0, "exited", string.Empty));
        var bridge = CreateBridge(runner);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            CreateGraphSqlServerResource(),
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Equal(SqlServerRuntimeStatus.Running, bridge.GetStatus(CreateGraphSqlServerResource()));
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-application-topology-graph-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "start cloudshell-application-topology-graph-sql-server",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteRestart_RemovesAndRecreatesContainer()
    {
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        runner.Enqueue(new(0, string.Empty, string.Empty));
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var bridge = CreateBridge(runner);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            CreateGraphSqlServerResource(port: 16433),
            SqlServerResourceTypeProvider.Operations.Restart);

        Assert.Empty(diagnostics);
        Assert.Equal(SqlServerRuntimeStatus.Running, bridge.GetStatus(CreateGraphSqlServerResource()));
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "rm -f cloudshell-application-topology-graph-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-application-topology-graph-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "run -d --name cloudshell-application-topology-graph-sql-server -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=CloudShell-Passw0rd! -p 127.0.0.1:16433:1433 mcr.microsoft.com/mssql/server:2022-latest",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteStop_RemovesContainer()
    {
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        var bridge = CreateBridge(runner);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            CreateGraphSqlServerResource(),
            SqlServerResourceTypeProvider.Operations.Stop);

        Assert.Empty(diagnostics);
        Assert.Equal(SqlServerRuntimeStatus.Stopped, bridge.GetStatus(CreateGraphSqlServerResource()));
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "rm -f cloudshell-application-topology-graph-sql-server",
                command.JoinedArguments));
    }

    private static ApplicationTopologyGraphSqlServerDockerBridge CreateBridge(
        RecordingApplicationTopologyDockerCommandRunner runner,
        IReadOnlyDictionary<string, string?>? configuration = null) =>
        new(
            runner,
            new ConfigurationBuilder()
                .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
                .Build());

    private static GraphResource CreateGraphSqlServerResource(
        int port = 14334)
    {
        var resolver = new ResourceResolver(
            [SqlServerResourceTypeProvider.ClassDefinition],
            [new SqlServerResourceTypeProvider().TypeDefinition]);

        return resolver.Resolve(new ResourceState(
            "graph-application-topology-sql-server",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ResourceId: "application.sql-server:graph-application-topology-sql-server",
            ProviderId: SqlServerResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SqlServerResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "tds",
                            "tcp",
                            TargetPort: 1433,
                            Host: "localhost",
                            Port: port,
                            Exposure: "Local")
                    })
            }));
    }

    private sealed class RecordingApplicationTopologyDockerCommandRunner :
        IApplicationTopologyDockerCommandRunner
    {
        private readonly Queue<ApplicationTopologyDockerCommandResult> _results = [];

        public List<RecordedDockerCommand> Commands { get; } = [];

        public void Enqueue(ApplicationTopologyDockerCommandResult result) =>
            _results.Enqueue(result);

        public ApplicationTopologyDockerCommandResult Run(
            IReadOnlyList<string> arguments,
            bool throwOnError = true) =>
            RunCore(arguments, throwOnError);

        public Task<ApplicationTopologyDockerCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool throwOnError = true) =>
            Task.FromResult(RunCore(arguments, throwOnError));

        private ApplicationTopologyDockerCommandResult RunCore(
            IReadOnlyList<string> arguments,
            bool throwOnError)
        {
            Commands.Add(new(arguments.ToArray(), throwOnError));
            var result = _results.Count == 0
                ? new ApplicationTopologyDockerCommandResult(0, string.Empty, string.Empty)
                : _results.Dequeue();
            if (throwOnError && result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.Error);
            }

            return result;
        }
    }

    private sealed record RecordedDockerCommand(
        IReadOnlyList<string> Arguments,
        bool ThrowOnError)
    {
        public string JoinedArguments => string.Join(' ', Arguments);
    }
}
