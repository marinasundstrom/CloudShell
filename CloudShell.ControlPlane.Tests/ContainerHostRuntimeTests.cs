using CloudShell.ControlPlane.Providers;

namespace CloudShell.ControlPlane.Tests;

public sealed class ContainerHostRuntimeTests
{
    [Fact]
    public async Task EnsureNetwork_InspectsBeforeCreatingAndRemoveUsesPortableOperation()
    {
        var runner = new RecordingCommandRunner();
        runner.Enqueue(new(1, string.Empty, "missing"));
        runner.Enqueue(new(0, "cloudshell", string.Empty));
        runner.Enqueue(new(0, "cloudshell", string.Empty));
        var runtime = new CommandContainerHostRuntime(runner);

        var ensure = await runtime.EnsureNetworkAsync(new("application:api", "cloudshell"));
        var remove = await runtime.RemoveNetworkAsync(new("application:api", "cloudshell"));

        Assert.True(ensure.Succeeded);
        Assert.True(remove.Succeeded);
        Assert.Collection(
            runner.Commands,
            command => Assert.Equal(["network", "inspect", "cloudshell"], command.Arguments),
            command => Assert.Equal(["network", "create", "cloudshell"], command.Arguments),
            command => Assert.Equal(["network", "rm", "cloudshell"], command.Arguments));
    }

    [Fact]
    public async Task RunContainer_ProjectsTypedIntentIntoTheCommandBoundary()
    {
        var runner = new RecordingCommandRunner();
        runner.Enqueue(new(0, "container-id", string.Empty));
        var runtime = new CommandContainerHostRuntime(runner);

        var result = await runtime.RunContainerAsync(
            new(
                "application:api",
                "api",
                "team/api:dev",
                NetworkName: "cloudshell",
                NetworkAliases: ["api"],
                PublishedPorts: [new("127.0.0.1", 5080, 8080)],
                EnvironmentVariables: new Dictionary<string, string> { ["MODE"] = "test" },
                Labels: new Dictionary<string, string> { ["cloudshell.owner"] = "application:api" },
                Mounts: [new("/tmp/config", "/app/config", ReadOnly: true)],
                Arguments: ["serve"]));

        Assert.True(result.Succeeded);
        var command = Assert.Single(runner.Commands);
        Assert.Equal("docker", command.FileName);
        Assert.Equal(
            [
                "run",
                "--detach",
                "--name",
                "api",
                "--rm",
                "--network",
                "cloudshell",
                "--network-alias",
                "api",
                "--publish",
                "127.0.0.1:5080:8080/tcp",
                "--env",
                "MODE=test",
                "--label",
                "cloudshell.owner-resource-id=application:api",
                "--label",
                "cloudshell.owner=application:api",
                "--volume",
                "/tmp/config:/app/config:ro",
                "team/api:dev",
                "serve"
            ],
            command.Arguments);
    }

    [Fact]
    public async Task InspectContainer_ParsesDockerAndPodmanCompatiblePayload()
    {
        const string payload =
            """
            [{"Id":"abc","Name":"/api","State":{"Status":"running"},"Config":{"Hostname":"api","Labels":{"cloudshell.owner-resource-id":"application:api","cloudshell.owner":"application:api"}},"NetworkSettings":{"Networks":{"cloudshell":{"IPAddress":"172.18.0.2","GlobalIPv6Address":""}}}}]
            """;
        var runner = new RecordingCommandRunner();
        runner.Enqueue(new(0, payload, string.Empty));
        var runtime = new CommandContainerHostRuntime(runner);

        var result = await runtime.InspectContainerAsync(new("application:api", "api"));

        var container = Assert.IsType<ContainerHostContainerObservation>(result.Container);
        Assert.Equal("api", container.Id);
        Assert.Equal(ContainerHostContainerState.Running, container.State);
        Assert.Equal("application:api", container.Labels["cloudshell.owner"]);
        Assert.Equal("172.18.0.2", container.Networks["cloudshell"].IPv4Address);
    }

    [Fact]
    public async Task InspectContainer_ParsesAppleContainerPayloadAndRemovesPrefixLength()
    {
        const string payload =
            """
            [{"configuration":{"id":"api","labels":{"cloudshell.owner-resource-id":"application:api","cloudshell.owner":"application:api"}},"status":{"state":"running","networks":[{"network":"cloudshell","hostname":"api.test.","ipv4Address":"192.168.65.2/24","ipv6Address":"fd00::2/64"}]}}]
            """;
        var runner = new RecordingCommandRunner();
        runner.Enqueue(new(0, payload, string.Empty));
        var runtime = new CommandContainerHostRuntime(runner);

        var result = await runtime.InspectContainerAsync(new("application:api", "api"));

        var container = Assert.IsType<ContainerHostContainerObservation>(result.Container);
        Assert.Equal("api", container.Id);
        Assert.Equal(ContainerHostContainerState.Running, container.State);
        Assert.Equal("application:api", container.Labels["cloudshell.owner"]);
        var network = container.Networks["cloudshell"];
        Assert.Equal("192.168.65.2", network.IPv4Address);
        Assert.Equal("fd00::2", network.IPv6Address);
        Assert.Equal("api.test.", network.HostName);
    }

    private sealed class RecordingCommandRunner : ILocalContainerApplicationCommandRunner
    {
        private readonly Queue<LocalContainerApplicationCommandResult> results = new();

        public List<RecordedCommand> Commands { get; } = [];

        public void Enqueue(LocalContainerApplicationCommandResult result) => results.Enqueue(result);

        public LocalContainerApplicationCommandResult Run(
            string fileName,
            IReadOnlyList<string> arguments,
            bool throwOnError = true,
            TimeSpan? timeout = null,
            string? workingDirectory = null) =>
            Record(fileName, arguments, throwOnError, timeout, workingDirectory);

        public Task<LocalContainerApplicationCommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool throwOnError = true,
            TimeSpan? timeout = null,
            string? workingDirectory = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Record(fileName, arguments, throwOnError, timeout, workingDirectory));
        }

        private LocalContainerApplicationCommandResult Record(
            string fileName,
            IReadOnlyList<string> arguments,
            bool throwOnError,
            TimeSpan? timeout,
            string? workingDirectory)
        {
            Commands.Add(new(fileName, arguments.ToArray(), throwOnError, timeout, workingDirectory));
            return results.Count > 0
                ? results.Dequeue()
                : new(0, string.Empty, string.Empty);
        }
    }

    private sealed record RecordedCommand(
        string FileName,
        IReadOnlyList<string> Arguments,
        bool ThrowOnError,
        TimeSpan? Timeout,
        string? WorkingDirectory);
}
