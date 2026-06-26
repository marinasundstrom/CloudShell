using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.Sample.Tests;

public sealed class ContainerAppDeploymentGraphDockerContainerRuntimeHandlerTests
{
    [Theory]
    [InlineData("running", DockerContainerRuntimeStatus.Running)]
    [InlineData("paused", DockerContainerRuntimeStatus.Paused)]
    [InlineData("exited", DockerContainerRuntimeStatus.Stopped)]
    [InlineData("dead", DockerContainerRuntimeStatus.Stopped)]
    [InlineData("restarting", DockerContainerRuntimeStatus.Unknown)]
    public async Task GetStatus_MapsDockerStatus(
        string dockerStatus,
        DockerContainerRuntimeStatus expectedStatus)
    {
        var runner = new RecordingDockerCommandRunner();
        runner.Enqueue(new(0, dockerStatus, string.Empty));
        var handler = new ContainerAppDeploymentGraphDockerContainerRuntimeHandler(runner);

        var status = handler.GetStatus(await CreateGraphRegistryResourceAsync());

        Assert.Equal(expectedStatus, status);
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-app-deployment-graph-registry",
                command.JoinedArguments));
    }

    [Fact]
    public async Task GetStatus_ReturnsStoppedWhenContainerIsMissing()
    {
        var runner = new RecordingDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var handler = new ContainerAppDeploymentGraphDockerContainerRuntimeHandler(runner);

        var status = handler.GetStatus(await CreateGraphRegistryResourceAsync());

        Assert.Equal(DockerContainerRuntimeStatus.Stopped, status);
    }

    [Fact]
    public async Task ExecuteStart_CreatesRegistryContainerWhenMissing()
    {
        var runner = new RecordingDockerCommandRunner();
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var handler = new ContainerAppDeploymentGraphDockerContainerRuntimeHandler(runner);

        var result = await handler.ExecuteLifecycleAsync(
            await CreateGraphRegistryResourceAsync(registry: "localhost:18023"),
            DockerContainerResourceTypeProvider.Operations.Start);

        Assert.Empty(result);
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-app-deployment-graph-registry",
                command.JoinedArguments),
            command => Assert.Equal(
                "run -d --name cloudshell-container-app-deployment-graph-registry -p 127.0.0.1:18023:5000 registry:2",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteStart_StartsExistingStoppedRegistryContainer()
    {
        var runner = new RecordingDockerCommandRunner();
        runner.Enqueue(new(0, "exited", string.Empty));
        var handler = new ContainerAppDeploymentGraphDockerContainerRuntimeHandler(runner);

        var result = await handler.ExecuteLifecycleAsync(
            await CreateGraphRegistryResourceAsync(),
            DockerContainerResourceTypeProvider.Operations.Start);

        Assert.Empty(result);
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-app-deployment-graph-registry",
                command.JoinedArguments),
            command => Assert.Equal(
                "start cloudshell-container-app-deployment-graph-registry",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteRestart_RemovesAndRecreatesRegistryContainer()
    {
        var runner = new RecordingDockerCommandRunner();
        runner.Enqueue(new(0, string.Empty, string.Empty));
        runner.Enqueue(new(1, string.Empty, "No such container"));
        var handler = new ContainerAppDeploymentGraphDockerContainerRuntimeHandler(runner);

        var result = await handler.ExecuteLifecycleAsync(
            await CreateGraphRegistryResourceAsync(registry: "http://localhost:18024"),
            DockerContainerResourceTypeProvider.Operations.Restart);

        Assert.Empty(result);
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(
                "rm -f cloudshell-container-app-deployment-graph-registry",
                command.JoinedArguments),
            command => Assert.Equal(
                "container inspect --format {{.State.Status}} cloudshell-container-app-deployment-graph-registry",
                command.JoinedArguments),
            command => Assert.Equal(
                "run -d --name cloudshell-container-app-deployment-graph-registry -p 127.0.0.1:18024:5000 registry:2",
                command.JoinedArguments));
    }

    [Fact]
    public async Task ExecuteLifecycle_IgnoresNonGraphRegistryResources()
    {
        var runner = new RecordingDockerCommandRunner();
        var handler = new ContainerAppDeploymentGraphDockerContainerRuntimeHandler(runner);

        var result = await handler.ExecuteLifecycleAsync(
            await CreateGraphRegistryResourceAsync(resourceId: "docker.container:other"),
            DockerContainerResourceTypeProvider.Operations.Start);

        Assert.Empty(result);
        Assert.Empty(runner.Commands);
    }

    private static async Task<GraphResource> CreateGraphRegistryResourceAsync(
        string? resourceId = null,
        string registry = "localhost:5023")
    {
        IResourceOperationProvider[] operationProviders =
        [
            new DockerContainerStartOperationProvider(),
            new DockerContainerStopOperationProvider(),
            new DockerContainerRestartOperationProvider(),
            new DockerContainerPauseOperationProvider(),
            new DockerContainerUnpauseOperationProvider()
        ];
        var pipeline = new ResourceDefinitionValidationPipeline(
            [DockerContainerResourceTypeProvider.ClassDefinition],
            [new DockerContainerResourceTypeProvider()],
            operationProviders: operationProviders,
            operationProjectors: operationProviders.OfType<IResourceOperationProjector>());
        var result = await pipeline.ValidateAsync(
            new ResourceDefinition(
                "graph-sample-registry",
                DockerContainerResourceTypeProvider.ResourceTypeId,
                ResourceId: resourceId ??
                    ContainerAppDeploymentGraphDockerContainerRuntimeHandler.GraphRegistryResourceId,
                Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                {
                    [DockerContainerResourceTypeProvider.Attributes.ContainerImage] =
                        "registry:2",
                    [DockerContainerResourceTypeProvider.Attributes.ContainerRegistry] =
                        registry
                }),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(
            result.HasErrors,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}")));
        return result.Resource;
    }

    private sealed class RecordingDockerCommandRunner :
        IContainerAppDeploymentDockerCommandRunner
    {
        private readonly Queue<ContainerAppDeploymentDockerCommandResult> _results = [];

        public List<RecordedDockerCommand> Commands { get; } = [];

        public void Enqueue(ContainerAppDeploymentDockerCommandResult result) =>
            _results.Enqueue(result);

        public ContainerAppDeploymentDockerCommandResult Run(
            IReadOnlyList<string> arguments,
            bool throwOnError = true) =>
            RunCore(arguments, throwOnError);

        public Task<ContainerAppDeploymentDockerCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool throwOnError = true) =>
            Task.FromResult(RunCore(arguments, throwOnError));

        private ContainerAppDeploymentDockerCommandResult RunCore(
            IReadOnlyList<string> arguments,
            bool throwOnError)
        {
            Commands.Add(new(arguments.ToArray(), throwOnError));
            var result = _results.Count == 0
                ? new ContainerAppDeploymentDockerCommandResult(0, string.Empty, string.Empty)
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
