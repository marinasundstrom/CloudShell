using System.Text.Json;
using CloudShell.ApplicationTopologyHost;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.Sample.Tests;

public sealed class ApplicationTopologyGraphSqlServerDockerBridgeTests
{
    [Fact]
    public void GetStatus_ReturnsUnknownBeforeLifecycleOperation()
    {
        using var fixture = new ApplicationTopologyGraphFixture();
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        var bridge = fixture.CreateBridge(runner);

        var status = bridge.GetStatus(fixture.ResolveUnresolvedGraphSqlServer());

        Assert.Equal(SqlServerRuntimeStatus.Unknown, status);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task ExecuteStart_CreatesSqlServerContainerWhenMissing()
    {
        using var fixture = new ApplicationTopologyGraphFixture(port: 15433);
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var bridge = fixture.CreateBridge(
            runner,
            new Dictionary<string, string?>
            {
                ["ApplicationTopology:SqlServer:Password"] = "Configured-Passw0rd!"
            });

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await fixture.ResolveGraphSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Equal(SqlServerRuntimeStatus.Running, bridge.GetStatus(await fixture.ResolveGraphSqlServerAsync()));
        var expectedVolumePath = Path.Combine(fixture.ContentRootPath, "Data", "storage", "sql-server");
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-application-topology-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                $"run -d --name cloudshell-application-topology-sql-server -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Configured-Passw0rd! -p 127.0.0.1:15433:1433 -v {expectedVolumePath}:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest",
                command.JoinedArguments));
        Assert.True(Directory.Exists(expectedVolumePath));
    }

    [Fact]
    public async Task ExecuteStart_StartsExistingStoppedContainer()
    {
        using var fixture = new ApplicationTopologyGraphFixture();
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        runner.Enqueue(new(0, "exited", string.Empty));
        var bridge = fixture.CreateBridge(runner);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await fixture.ResolveGraphSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Equal(SqlServerRuntimeStatus.Running, bridge.GetStatus(await fixture.ResolveGraphSqlServerAsync()));
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-application-topology-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "start cloudshell-application-topology-sql-server",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteRestart_RemovesAndRecreatesContainer()
    {
        using var fixture = new ApplicationTopologyGraphFixture(port: 16433);
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        runner.Enqueue(new(0, string.Empty, string.Empty));
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var bridge = fixture.CreateBridge(runner);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await fixture.ResolveGraphSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Restart);

        Assert.Empty(diagnostics);
        Assert.Equal(SqlServerRuntimeStatus.Running, bridge.GetStatus(await fixture.ResolveGraphSqlServerAsync()));
        var expectedVolumePath = Path.Combine(fixture.ContentRootPath, "Data", "storage", "sql-server");
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "rm -f cloudshell-application-topology-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-application-topology-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                $"run -d --name cloudshell-application-topology-sql-server -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=CloudShell-Passw0rd! -p 127.0.0.1:16433:1433 -v {expectedVolumePath}:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteStart_RemovesFailedCreatedContainerAndRetriesTransientMountFailure()
    {
        using var fixture = new ApplicationTopologyGraphFixture();
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        runner.Enqueue(new(
            127,
            string.Empty,
            "error while creating mount source path '/host_mnt/tmp/cloudshell/sql-server': no such file or directory"));
        runner.Enqueue(new(0, string.Empty, string.Empty));
        runner.Enqueue(new(0, "container-id", string.Empty));
        var bridge = fixture.CreateBridge(runner);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await fixture.ResolveGraphSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        var expectedVolumePath = Path.Combine(fixture.ContentRootPath, "Data", "storage", "sql-server");
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-application-topology-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                $"run -d --name cloudshell-application-topology-sql-server -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=CloudShell-Passw0rd! -p 127.0.0.1:14334:1433 -v {expectedVolumePath}:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest",
                command.JoinedArguments),
            command => Assert.Equal(
                "rm -f cloudshell-application-topology-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                $"run -d --name cloudshell-application-topology-sql-server -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=CloudShell-Passw0rd! -p 127.0.0.1:14334:1433 -v {expectedVolumePath}:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteStop_RemovesContainer()
    {
        using var fixture = new ApplicationTopologyGraphFixture();
        var runner = new RecordingApplicationTopologyDockerCommandRunner();
        var bridge = fixture.CreateBridge(runner);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await fixture.ResolveGraphSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Stop);

        Assert.Empty(diagnostics);
        Assert.Equal(SqlServerRuntimeStatus.Stopped, bridge.GetStatus(await fixture.ResolveGraphSqlServerAsync()));
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "rm -f cloudshell-application-topology-sql-server",
                command.JoinedArguments));
    }

    private sealed class ApplicationTopologyGraphFixture : IDisposable
    {
        private const string GraphStorageResourceId = "cloudshell.storage:application-topology-local";
        private const string GraphVolumeResourceId = "cloudshell.volume:application-topology-sql-data";
        private const string GraphSqlServerResourceId = ApplicationTopologyGraphSqlServerRuntimeHandler.GraphSqlServerResourceId;
        private readonly ServiceProvider _serviceProvider;

        public ApplicationTopologyGraphFixture(int port = 14334)
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ContentRootPath);
            var services = new ServiceCollection();
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(ContentRootPath));
            services
                .AddInMemoryResourceModelGraph(
                [
                    new ResourceState(
                        "application-topology-local",
                        StorageResourceTypeProvider.ResourceTypeId,
                        ResourceId: GraphStorageResourceId,
                        ProviderId: StorageResourceTypeProvider.ProviderId,
                        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                        {
                            [StorageResourceTypeProvider.Attributes.Provider] = "local",
                            [StorageResourceTypeProvider.Attributes.Medium] = "FileSystem",
                            [StorageResourceTypeProvider.Attributes.Location] = "./Data/storage"
                        }),
                    new ResourceState(
                        "application-topology-sql-data",
                        CloudShellVolumeResourceTypeProvider.ResourceTypeId,
                        ResourceId: GraphVolumeResourceId,
                        ProviderId: CloudShellVolumeResourceTypeProvider.ProviderId,
                        DisplayName: "Application Topology SQL Data",
                        DependsOn:
                        [
                            ResourceReference.DependsOnResourceId(
                                GraphStorageResourceId,
                                typeId: StorageResourceTypeProvider.ResourceTypeId)
                        ],
                        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                        {
                            [CloudShellVolumeResourceTypeProvider.Attributes.Provider] = "local",
                            [CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium] = "FileSystem",
                            [CloudShellVolumeResourceTypeProvider.Attributes.SubPath] = "sql-server",
                            [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteOnce",
                            [CloudShellVolumeResourceTypeProvider.Attributes.Persistent] = true
                        }),
                    new ResourceState(
                        "application-topology-sql-server",
                        SqlServerResourceTypeProvider.ResourceTypeId,
                        ResourceId: GraphSqlServerResourceId,
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
                        },
                        Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
                        {
                            [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                                ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                                [
                                    new(GraphVolumeResourceId, "/var/opt/mssql")
                                ]))
                        })
                ])
                .AddStorageResourceType()
                .AddCloudShellVolumeResourceType()
                .AddSqlServerResourceType()
                .AddResourceModelGraphServices();
            _serviceProvider = services.BuildServiceProvider();
        }

        public string ContentRootPath { get; }

        public ApplicationTopologyGraphSqlServerDockerBridge CreateBridge(
            RecordingApplicationTopologyDockerCommandRunner runner,
            IReadOnlyDictionary<string, string?>? configuration = null) =>
            new(
                runner,
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                _serviceProvider.GetRequiredService<IHostEnvironment>(),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
                    .Build());

        public async ValueTask<GraphResource> ResolveGraphSqlServerAsync()
        {
            var resolution = await _serviceProvider
                .GetRequiredService<ResourceModelGraphResourceResolver>()
                .ResolveAsync(GraphSqlServerResourceId);
            return resolution.Target ?? throw new InvalidOperationException("Graph SQL Server was not resolved.");
        }

        public GraphResource ResolveUnresolvedGraphSqlServer()
        {
            var resolver = new ResourceResolver(
                [SqlServerResourceTypeProvider.ClassDefinition],
                [new SqlServerResourceTypeProvider().TypeDefinition]);

            return resolver.Resolve(new ResourceState(
                "application-topology-sql-server",
                SqlServerResourceTypeProvider.ResourceTypeId,
                ResourceId: GraphSqlServerResourceId,
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
                                Port: 14334,
                                Exposure: "Local")
                        })
                }));
        }

        public void Dispose()
        {
            _serviceProvider.Dispose();
            if (Directory.Exists(ContentRootPath))
            {
                Directory.Delete(ContentRootPath, recursive: true);
            }
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ApplicationTopology.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
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
