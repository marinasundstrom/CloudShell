using System.Text.Json;
using CloudShell.ContainerHost;
using CloudShell.ResourceModel;
using CloudShell.ResourceModel.ReferenceProviders;
using CloudShell.ResourceModel.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.Sample.Tests;

public sealed class ContainerHostSqlServerDockerBridgeTests
{
    [Fact]
    public async Task ExecuteStart_CreatesSqlServerContainerWithStorageBackedBindMount()
    {
        using var fixture = new ContainerHostFixture();
        var runner = new RecordingContainerHostDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var bridge = fixture.CreateBridge(
            runner,
            new Dictionary<string, string?>
            {
                ["ContainerHost:SqlServer:Password"] = "Configured-Passw0rd!"
            });

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await fixture.ResolveSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Equal(SqlServerRuntimeStatus.Running, bridge.GetStatus(await fixture.ResolveSqlServerAsync()));
        var expectedVolumePath = Path.Combine(fixture.ContentRootPath, "Data", "storage", "sql-server");
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                $"run -d --name cloudshell-container-host-sql-server -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Configured-Passw0rd! -p 127.0.0.1:15434:1433 -v {expectedVolumePath}:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest",
                command.JoinedArguments));
        Assert.True(Directory.Exists(expectedVolumePath));
    }

    [Fact]
    public async Task ExecuteRestart_RemovesAndRecreatesSqlServerContainerWithBindMount()
    {
        using var fixture = new ContainerHostFixture();
        var runner = new RecordingContainerHostDockerCommandRunner();
        runner.Enqueue(new(0, string.Empty, string.Empty));
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var bridge = fixture.CreateBridge(runner);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await fixture.ResolveSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Restart);

        Assert.Empty(diagnostics);
        Assert.Equal(SqlServerRuntimeStatus.Running, bridge.GetStatus(await fixture.ResolveSqlServerAsync()));
        var expectedVolumePath = Path.Combine(fixture.ContentRootPath, "Data", "storage", "sql-server");
        Assert.Collection(
            runner.Commands,
            command =>
            {
                Assert.Equal(
                    "rm -f cloudshell-container-host-sql-server",
                    command.JoinedArguments);
                Assert.Equal(TimeSpan.FromSeconds(5), command.CommandTimeout);
            },
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                $"run -d --name cloudshell-container-host-sql-server -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=CloudShell-Passw0rd! -p 127.0.0.1:15434:1433 -v {expectedVolumePath}:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteStart_RemovesFailedCreatedContainerAndRetriesTransientMountFailure()
    {
        using var fixture = new ContainerHostFixture();
        var runner = new RecordingContainerHostDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        runner.Enqueue(new(
            127,
            string.Empty,
            "error while creating mount source path '/host_mnt/tmp/cloudshell/sql-server': no such file or directory"));
        runner.Enqueue(new(0, string.Empty, string.Empty));
        runner.Enqueue(new(0, "container-id", string.Empty));
        var bridge = fixture.CreateBridge(runner);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await fixture.ResolveSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        var expectedVolumePath = Path.Combine(fixture.ContentRootPath, "Data", "storage", "sql-server");
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                $"run -d --name cloudshell-container-host-sql-server -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=CloudShell-Passw0rd! -p 127.0.0.1:15434:1433 -v {expectedVolumePath}:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest",
                command.JoinedArguments),
            command => Assert.Equal(
                "rm -f cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                $"run -d --name cloudshell-container-host-sql-server -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=CloudShell-Passw0rd! -p 127.0.0.1:15434:1433 -v {expectedVolumePath}:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest",
                command.JoinedArguments));
    }

    [Fact]
    public async Task RuntimeHandler_IgnoresUnmappedSqlServerResource()
    {
        using var fixture = new ContainerHostFixture();
        var runner = new RecordingContainerHostDockerCommandRunner();
        var handler = new ContainerHostSqlServerRuntimeHandler(
            fixture.CreateBridge(runner));
        var resource = fixture.ResolveUnmappedSqlServer();

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Empty(runner.Commands);
        Assert.Equal(SqlServerRuntimeStatus.Unknown, handler.GetStatus(resource));
    }

    private sealed class ContainerHostFixture : IDisposable
    {
        private const string StorageResourceId = "cloudshell.storage:local";
        private const string VolumeResourceId = "cloudshell.volume:sql-data";
        private const string SqlServerResourceId = ContainerHostSqlServerRuntimeHandler.SqlServerResourceId;
        private readonly ServiceProvider _serviceProvider;

        public ContainerHostFixture()
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ContentRootPath);
            var services = new ServiceCollection();
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(ContentRootPath));
            services
                .AddInMemoryResourceModelGraph(
                [
                    new ResourceState(
                        "local",
                        StorageResourceTypeProvider.ResourceTypeId,
                        ResourceId: StorageResourceId,
                        ProviderId: StorageResourceTypeProvider.ProviderId,
                        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                        {
                            [StorageResourceTypeProvider.Attributes.Provider] = "local",
                            [StorageResourceTypeProvider.Attributes.Medium] = "FileSystem",
                            [StorageResourceTypeProvider.Attributes.Location] = "./Data/storage"
                        }),
                    new ResourceState(
                        "sql-data",
                        CloudShellVolumeResourceTypeProvider.ResourceTypeId,
                        ResourceId: VolumeResourceId,
                        ProviderId: CloudShellVolumeResourceTypeProvider.ProviderId,
                        DisplayName: "SQL Server Data",
                        DependsOn:
                        [
                            ResourceReference.DependsOnResourceId(
                                StorageResourceId,
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
                        "sql-server",
                        SqlServerResourceTypeProvider.ResourceTypeId,
                        ResourceId: SqlServerResourceId,
                        ProviderId: SqlServerResourceTypeProvider.ProviderId,
                        DisplayName: "SQL Server",
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
                                        Port: 15434,
                                        Exposure: "Local")
                                })
                        },
                        Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
                        {
                            [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                                ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                                [
                                    new(VolumeResourceId, "/var/opt/mssql")
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

        public ContainerHostSqlServerDockerBridge CreateBridge(
            RecordingContainerHostDockerCommandRunner runner,
            IReadOnlyDictionary<string, string?>? configuration = null) =>
            new(
                runner,
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                _serviceProvider.GetRequiredService<IHostEnvironment>(),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
                    .Build());

        public async ValueTask<ResourceModelResource> ResolveSqlServerAsync()
        {
            var resolution = await _serviceProvider
                .GetRequiredService<ResourceModelGraphResourceResolver>()
                .ResolveAsync(SqlServerResourceId);
            return resolution.Target ?? throw new InvalidOperationException("SQL Server was not resolved.");
        }

        public ResourceModelResource ResolveUnmappedSqlServer()
        {
            var resolver = new ResourceResolver(
                [SqlServerResourceTypeProvider.ClassDefinition],
                [new SqlServerResourceTypeProvider().TypeDefinition]);

            return resolver.Resolve(new ResourceState(
                "other-sql",
                SqlServerResourceTypeProvider.ResourceTypeId,
                ResourceId: "application.sql-server:other-sql",
                ProviderId: SqlServerResourceTypeProvider.ProviderId));
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

    private sealed class RecordingContainerHostDockerCommandRunner :
        IContainerHostDockerCommandRunner
    {
        private readonly Queue<ContainerHostDockerCommandResult> _results = [];

        public List<RecordedDockerCommand> Commands { get; } = [];

        public void Enqueue(ContainerHostDockerCommandResult result) =>
            _results.Enqueue(result);

        public ContainerHostDockerCommandResult Run(
            IReadOnlyList<string> arguments,
            bool throwOnError = true) =>
            RunCore(arguments, throwOnError);

        public Task<ContainerHostDockerCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool throwOnError = true,
            TimeSpan? commandTimeout = null) =>
            Task.FromResult(RunCore(arguments, throwOnError, commandTimeout));

        private ContainerHostDockerCommandResult RunCore(
            IReadOnlyList<string> arguments,
            bool throwOnError,
            TimeSpan? commandTimeout = null)
        {
            Commands.Add(new(arguments.ToArray(), throwOnError, commandTimeout));
            var result = _results.Count == 0
                ? new ContainerHostDockerCommandResult(0, string.Empty, string.Empty)
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
        bool ThrowOnError,
        TimeSpan? CommandTimeout)
    {
        public string JoinedArguments => string.Join(' ', Arguments);
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ContainerHost.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
