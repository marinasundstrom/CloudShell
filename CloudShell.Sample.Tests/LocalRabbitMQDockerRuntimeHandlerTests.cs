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

public sealed class LocalRabbitMQDockerRuntimeHandlerTests
{
    [Fact]
    public async Task ExecuteStart_CreatesRabbitMQContainerWithManagementUiAndStorageBackedBindMount()
    {
        using var fixture = new RabbitMQRuntimeFixture(
            "application.rabbitmq:rabbitmq",
            "rabbitmq",
            amqpPort: 5673,
            managementPort: 15673);
        var runner = new RecordingRabbitMQDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var handler = fixture.CreateHandler(
            runner,
            "cloudshell-rabbitmq",
            usernameConfigurationKey: "RabbitMQ:Username",
            passwordConfigurationKey: "RabbitMQ:Password",
            configuration:
                new Dictionary<string, string?>
                {
                    ["RabbitMQ:Username"] = "developer",
                    ["RabbitMQ:Password"] = "local-password"
                });

        var diagnostics = await handler.ExecuteLifecycleAsync(
            await fixture.ResolveRabbitMQAsync(),
            RabbitMQResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Equal(
            RabbitMQRuntimeStatus.Running,
            handler.GetStatus(await fixture.ResolveRabbitMQAsync()));
        var expectedVolumePath = Path.Combine(fixture.ContentRootPath, "Data", "storage", "rabbitmq");
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-rabbitmq",
                command.JoinedArguments),
            command => Assert.Equal(
                $"run -d --name cloudshell-rabbitmq -e RABBITMQ_DEFAULT_USER=developer -e RABBITMQ_DEFAULT_PASS=local-password -p 127.0.0.1:5673:5672 -p 127.0.0.1:15673:15672 -v {expectedVolumePath}:/var/lib/rabbitmq rabbitmq:3-management",
                command.JoinedArguments));
        Assert.True(Directory.Exists(expectedVolumePath));
    }

    [Fact]
    public async Task ExecuteStart_UsesConventionContainerNameWhenBrokerIsNotMapped()
    {
        using var fixture = new RabbitMQRuntimeFixture(
            "application.rabbitmq:rabbitmq",
            "rabbitmq",
            amqpPort: 5674,
            managementPort: 15674);
        var runner = new RecordingRabbitMQDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var handler = fixture.CreateHandlerWithoutMapping(runner);

        var diagnostics = await handler.ExecuteLifecycleAsync(
            await fixture.ResolveRabbitMQAsync(),
            RabbitMQResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        var inspectCommand = Assert.Single(
            runner.Commands,
            command => command.Arguments.Count >= 5 &&
                command.Arguments[0] == "container" &&
                command.Arguments[1] == "inspect");
        var containerName = inspectCommand.Arguments[^1];
        Assert.StartsWith("cloudshell-", containerName, StringComparison.Ordinal);
        Assert.Contains("-rabbitmq-rabbitmq", containerName, StringComparison.Ordinal);
        Assert.Contains(
            runner.Commands,
            command => command.JoinedArguments.Contains(
                $"run -d --name {containerName}",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteStart_ReturnsDiagnosticWhenVolumeConsumerMountsAreMissing()
    {
        using var fixture = new RabbitMQRuntimeFixture(
            "application.rabbitmq:rabbitmq",
            "rabbitmq",
            amqpPort: 5675,
            managementPort: 15675,
            volumeConsumerPayload: ResourceDefinitionJson.FromValue(new { }));
        var runner = new RecordingRabbitMQDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var handler = fixture.CreateHandlerWithoutMapping(runner);

        var diagnostics = await handler.ExecuteLifecycleAsync(
            await fixture.ResolveRabbitMQAsync(),
            RabbitMQResourceTypeProvider.Operations.Start);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("volume consumer capability", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Parameter 'source'", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runner.Commands, command => command.Arguments.FirstOrDefault() == "run");
    }


    private sealed class RabbitMQRuntimeFixture : IDisposable
    {
        private const string StorageResourceId = "cloudshell.storage:local";
        private const string VolumeResourceId = "cloudshell.volume:rabbitmq-data";
        private readonly ServiceProvider serviceProvider;
        private readonly string resourceId;

        public RabbitMQRuntimeFixture(
            string resourceId,
            string name,
            int amqpPort,
            int managementPort,
            JsonElement? volumeConsumerPayload = null)
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
                        "rabbitmq-data",
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
                            [CloudShellVolumeResourceTypeProvider.Attributes.SubPath] = "rabbitmq",
                            [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteOnce",
                            [CloudShellVolumeResourceTypeProvider.Attributes.Persistent] = true
                        }),
                    new ResourceState(
                        name,
                        RabbitMQResourceTypeProvider.ResourceTypeId,
                        ResourceId: resourceId,
                        ProviderId: RabbitMQResourceTypeProvider.ProviderId,
                        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                        {
                            [RabbitMQResourceTypeProvider.Attributes.EndpointRequests] =
                                ResourceAttributeValue.FromObject(new[]
                                {
                                    new NetworkingEndpointRequestValue(
                                        "amqp",
                                        "tcp",
                                        TargetPort: 5672,
                                        Host: "localhost",
                                        Port: amqpPort,
                                        Exposure: "Local"),
                                    new NetworkingEndpointRequestValue(
                                        "management",
                                        "http",
                                        TargetPort: 15672,
                                        Host: "localhost",
                                        Port: managementPort,
                                        Exposure: "Local")
                                })
                        },
                        Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
                        {
                            [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                                volumeConsumerPayload ??
                                ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                                [
                                    new(VolumeResourceId, "/var/lib/rabbitmq")
                                ]))
                        })
                ])
                .AddStorageResourceType()
                .AddCloudShellVolumeResourceType()
                .AddRabbitMQResourceType()
                .AddResourceModelGraphServices();
            serviceProvider = services.BuildServiceProvider();
        }

        public string ContentRootPath { get; }

        public LocalRabbitMQDockerRuntimeHandler CreateHandler(
            RecordingRabbitMQDockerCommandRunner runner,
            string containerName,
            string? usernameConfigurationKey = null,
            string? passwordConfigurationKey = null,
            IReadOnlyDictionary<string, string?>? configuration = null)
        {
            var options = new LocalRabbitMQDockerRuntimeOptions();
            options.AddBroker(
                resourceId,
                containerName,
                runtime =>
                {
                    runtime.UsernameConfigurationKey = usernameConfigurationKey;
                    runtime.PasswordConfigurationKey = passwordConfigurationKey;
                });

            return new(
                runner,
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<IHostEnvironment>(),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
                .Build(),
                Options.Create(options));
        }

        public LocalRabbitMQDockerRuntimeHandler CreateHandlerWithoutMapping(
            RecordingRabbitMQDockerCommandRunner runner,
            IReadOnlyDictionary<string, string?>? configuration = null) =>
            new(
                runner,
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<IHostEnvironment>(),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
                    .Build(),
                Options.Create(new LocalRabbitMQDockerRuntimeOptions()));

        public async ValueTask<ResourceModelResource> ResolveRabbitMQAsync()
        {
            var resolution = await serviceProvider
                .GetRequiredService<ResourceModelGraphResourceResolver>()
                .ResolveAsync(resourceId);
            return resolution.Target ?? throw new InvalidOperationException("RabbitMQ was not resolved.");
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

    private sealed class RecordingRabbitMQDockerCommandRunner :
        ILocalRabbitMQDockerCommandRunner
    {
        private readonly Queue<LocalRabbitMQDockerCommandResult> results = [];

        public List<RecordedDockerCommand> Commands { get; } = [];

        public void Enqueue(LocalRabbitMQDockerCommandResult result) =>
            results.Enqueue(result);

        public LocalRabbitMQDockerCommandResult Run(
            IReadOnlyList<string> arguments,
            bool throwOnError = true) =>
            RunCore(arguments, throwOnError);

        public Task<LocalRabbitMQDockerCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool throwOnError = true,
            TimeSpan? commandTimeout = null) =>
            Task.FromResult(RunCore(arguments, throwOnError, commandTimeout));

        private LocalRabbitMQDockerCommandResult RunCore(
            IReadOnlyList<string> arguments,
            bool throwOnError,
            TimeSpan? commandTimeout = null)
        {
            Commands.Add(new(arguments.ToArray(), throwOnError, commandTimeout));
            var result = results.Count == 0
                ? new LocalRabbitMQDockerCommandResult(0, string.Empty, string.Empty)
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

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.RabbitMQ.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
