using System.Text.Json;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ResourceModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.Sample.Tests;

public sealed class LocalSqlServerDockerRuntimeHandlerTests
{
    [Fact]
    public async Task ExecuteStart_CreatesSqlServerContainerWithStorageBackedBindMount()
    {
        using var fixture = new SqlServerRuntimeFixture(
            "application.sql-server:sql-server",
            "sql-server",
            15434);
        var runner = new RecordingSqlServerDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var handler = fixture.CreateHandler(
            runner,
            "cloudshell-container-host-sql-server",
            passwordConfigurationKey: "ContainerHost:SqlServer:Password",
            configuration:
                new Dictionary<string, string?>
                {
                    ["ContainerHost:SqlServer:Password"] = "Configured-Passw0rd!"
                });

        var diagnostics = await handler.ExecuteLifecycleAsync(
            await fixture.ResolveSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Equal(
            SqlServerRuntimeStatus.Running,
            handler.GetStatus(await fixture.ResolveSqlServerAsync()));
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
    public async Task ExecuteStart_WaitsUntilReadyWhenConfigured()
    {
        const string resourceId = "application.sql-server:application-topology-sql-server";
        using var fixture = new SqlServerRuntimeFixture(
            resourceId,
            "application-topology-sql-server",
            15433);
        var runner = new RecordingSqlServerDockerCommandRunner();
        var readiness = new RecordingSqlServerReadinessProbe();
        runner.Enqueue(new(0, "exited", string.Empty));
        var handler = fixture.CreateHandler(
            runner,
            "cloudshell-application-topology-sql-server",
            waitUntilReady: true,
            readinessProbe: readiness);

        var diagnostics = await handler.ExecuteLifecycleAsync(
            await fixture.ResolveSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-application-topology-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "start cloudshell-application-topology-sql-server",
                command.JoinedArguments));
        Assert.Equal([resourceId], readiness.ResourceIds);
    }

    [Fact]
    public async Task ExecuteStart_RemovesFailedCreatedContainerAndRetriesTransientMountFailure()
    {
        using var fixture = new SqlServerRuntimeFixture(
            "application.sql-server:sql-server",
            "sql-server",
            15434);
        var runner = new RecordingSqlServerDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        runner.Enqueue(new(
            127,
            string.Empty,
            "error while creating mount source path '/host_mnt/tmp/cloudshell/sql-server': no such file or directory"));
        runner.Enqueue(new(0, string.Empty, string.Empty));
        runner.Enqueue(new(1, string.Empty, "No such container"));
        runner.Enqueue(new(0, "container-id", string.Empty));
        var handler = fixture.CreateHandler(
            runner,
            "cloudshell-container-host-sql-server");

        var diagnostics = await handler.ExecuteLifecycleAsync(
            await fixture.ResolveSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.StartsWith(
                "run -d --name cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "rm -f cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.StartsWith(
                "run -d --name cloudshell-container-host-sql-server",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteRestart_WaitsForDockerRemovalBeforeStartingContainerAgain()
    {
        using var fixture = new SqlServerRuntimeFixture(
            "application.sql-server:sql-server",
            "sql-server",
            15434);
        var runner = new RecordingSqlServerDockerCommandRunner();
        runner.Enqueue(new(0, string.Empty, string.Empty));
        runner.Enqueue(new(0, "removing", string.Empty));
        runner.Enqueue(new(1, string.Empty, "No such container"));
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var handler = fixture.CreateHandler(
            runner,
            "cloudshell-container-host-sql-server");

        var diagnostics = await handler.ExecuteLifecycleAsync(
            await fixture.ResolveSqlServerAsync(),
            SqlServerResourceTypeProvider.Operations.Restart);

        Assert.Empty(diagnostics);
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "rm -f cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-host-sql-server",
                command.JoinedArguments),
            command => Assert.StartsWith(
                "run -d --name cloudshell-container-host-sql-server",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteLifecycle_IgnoresUnmappedSqlServerResource()
    {
        using var fixture = new SqlServerRuntimeFixture(
            "application.sql-server:sql-server",
            "sql-server",
            15434);
        var runner = new RecordingSqlServerDockerCommandRunner();
        var handler = fixture.CreateHandler(
            runner,
            "cloudshell-container-host-sql-server");
        var resource = fixture.ResolveUnmappedSqlServer();

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            SqlServerResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Empty(runner.Commands);
        Assert.Equal(SqlServerRuntimeStatus.Unknown, handler.GetStatus(resource));
    }

    private sealed class SqlServerRuntimeFixture : IDisposable
    {
        private const string StorageResourceId = "cloudshell.storage:local";
        private const string VolumeResourceId = "cloudshell.volume:sql-data";
        private readonly ServiceProvider serviceProvider;
        private readonly string resourceId;

        public SqlServerRuntimeFixture(
            string resourceId,
            string name,
            int port)
        {
            this.resourceId = resourceId;
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
                        name,
                        SqlServerResourceTypeProvider.ResourceTypeId,
                        ResourceId: resourceId,
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
                                    new(VolumeResourceId, "/var/opt/mssql")
                                ]))
                        })
                ])
                .AddStorageResourceType()
                .AddCloudShellVolumeResourceType()
                .AddSqlServerResourceType()
                .AddResourceModelGraphServices();
            serviceProvider = services.BuildServiceProvider();
        }

        public string ContentRootPath { get; }

        public LocalSqlServerDockerRuntimeHandler CreateHandler(
            RecordingSqlServerDockerCommandRunner runner,
            string containerName,
            string? passwordConfigurationKey = null,
            bool waitUntilReady = false,
            ILocalSqlServerReadinessProbe? readinessProbe = null,
            IReadOnlyDictionary<string, string?>? configuration = null)
        {
            var options = new LocalSqlServerDockerRuntimeOptions();
            options.AddServer(
                resourceId,
                containerName,
                runtime =>
                {
                    runtime.PasswordConfigurationKey = passwordConfigurationKey;
                    runtime.WaitUntilReady = waitUntilReady;
                });

            return new(
                runner,
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<IHostEnvironment>(),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
                    .Build(),
                readinessProbe ?? new NoopLocalSqlServerReadinessProbe(),
                Options.Create(options));
        }

        public async ValueTask<ResourceModelResource> ResolveSqlServerAsync()
        {
            var resolution = await serviceProvider
                .GetRequiredService<ResourceModelGraphResourceResolver>()
                .ResolveAsync(resourceId);
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
            serviceProvider.Dispose();
            if (Directory.Exists(ContentRootPath))
            {
                Directory.Delete(ContentRootPath, recursive: true);
            }
        }
    }

    private sealed class RecordingSqlServerDockerCommandRunner :
        ILocalSqlServerDockerCommandRunner
    {
        private readonly Queue<LocalSqlServerDockerCommandResult> results = [];

        public List<RecordedDockerCommand> Commands { get; } = [];

        public void Enqueue(LocalSqlServerDockerCommandResult result) =>
            results.Enqueue(result);

        public LocalSqlServerDockerCommandResult Run(
            IReadOnlyList<string> arguments,
            bool throwOnError = true) =>
            RunCore(arguments, throwOnError);

        public Task<LocalSqlServerDockerCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool throwOnError = true,
            TimeSpan? commandTimeout = null) =>
            Task.FromResult(RunCore(arguments, throwOnError, commandTimeout));

        private LocalSqlServerDockerCommandResult RunCore(
            IReadOnlyList<string> arguments,
            bool throwOnError,
            TimeSpan? commandTimeout = null)
        {
            Commands.Add(new(arguments.ToArray(), throwOnError, commandTimeout));
            var result = results.Count == 0
                ? new LocalSqlServerDockerCommandResult(0, string.Empty, string.Empty)
                : results.Dequeue();
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

    private sealed class RecordingSqlServerReadinessProbe : ILocalSqlServerReadinessProbe
    {
        public List<string> ResourceIds { get; } = [];

        public Task WaitUntilReadyAsync(
            ResourceModelResource resource,
            CancellationToken cancellationToken)
        {
            ResourceIds.Add(resource.EffectiveResourceId);
            return Task.CompletedTask;
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.SqlServer.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
