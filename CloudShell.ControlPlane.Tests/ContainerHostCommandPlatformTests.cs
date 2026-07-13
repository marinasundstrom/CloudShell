using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;

namespace CloudShell.ControlPlane.Tests;

public sealed class ContainerHostCommandPlatformTests
{
    [Fact]
    public void CreatePlan_UsesDockerExecutableWhenNoHostIsConfigured()
    {
        var platform = new ContainerHostCommandPlatform(
            [],
            new TestHostToolResolver("docker"));

        var plan = platform.CreatePlan();
        var startInfo = plan.CreateStartInfo(["ps"]);

        Assert.True(plan.IsAvailable);
        Assert.Equal("docker", plan.Executable);
        Assert.Equal("docker", startInfo.FileName);
        Assert.Equal(["ps"], startInfo.ArgumentList);
        Assert.False(startInfo.Environment.ContainsKey("DOCKER_HOST"));
        Assert.False(startInfo.Environment.ContainsKey("CONTAINER_HOST"));
    }

    [Fact]
    public void CreatePlan_UsesPodmanExecutableAndContainerHostEnvironment()
    {
        var host = new ContainerHostDescriptor(
            "podman:default",
            "Default Podman",
            ContainerHostKind.Podman,
            "unix:///run/user/501/podman/podman.sock");
        var platform = new ContainerHostCommandPlatform(
            [new StaticContainerHostProvider(host)],
            new TestHostToolResolver("podman"));

        var plan = platform.CreatePlan();
        var startInfo = plan.CreateStartInfo(["ps"]);

        Assert.True(plan.IsAvailable);
        Assert.Equal("podman", startInfo.FileName);
        Assert.Equal("unix:///run/user/501/podman/podman.sock", startInfo.Environment["CONTAINER_HOST"]);
        Assert.False(startInfo.Environment.ContainsKey("DOCKER_HOST"));
    }

    [Fact]
    public void CreatePlan_UsesDockerHostEnvironmentForDockerCompatibleHost()
    {
        var host = new ContainerHostDescriptor(
            "docker:remote",
            "Remote Docker",
            ContainerHostKind.Docker,
            "tcp://docker.example.test:2376");
        var platform = new ContainerHostCommandPlatform(
            [new StaticContainerHostProvider(host)],
            new TestHostToolResolver("docker"));

        var plan = platform.CreatePlan();
        var startInfo = plan.CreateStartInfo(["ps"]);

        Assert.True(plan.IsAvailable);
        Assert.Equal("docker", startInfo.FileName);
        Assert.Equal("tcp://docker.example.test:2376", startInfo.Environment["DOCKER_HOST"]);
        Assert.False(startInfo.Environment.ContainsKey("CONTAINER_HOST"));
    }

    [Fact]
    public void CreatePlan_ReportsCustomExecutableUnavailableReason()
    {
        var host = new ContainerHostDescriptor(
            "docker:custom",
            "Custom Docker",
            ContainerHostKind.Docker,
            "tcp://docker.example.test:2376",
            Metadata: new Dictionary<string, string>
            {
                [ContainerHostCommandPlatform.ExecutableMetadataKey] = "/opt/docker/bin/docker"
            });
        var platform = new ContainerHostCommandPlatform(
            [new StaticContainerHostProvider(host)],
            new TestHostToolResolver());

        var plan = platform.CreatePlan();

        Assert.False(plan.IsAvailable);
        Assert.Equal("/opt/docker/bin/docker", plan.Executable);
        Assert.Contains("Configured Docker executable '/opt/docker/bin/docker' is unavailable", plan.UnavailableReason);
        Assert.Contains(ContainerHostCommandPlatform.ExecutableMetadataKey, plan.UnavailableReason);
    }

    [Fact]
    public void CreatePlan_UsesCustomExecutableMetadataWhenAvailable()
    {
        var host = new ContainerHostDescriptor(
            "docker:custom",
            "Custom Docker",
            ContainerHostKind.Docker,
            "tcp://docker.example.test:2376",
            Metadata: new Dictionary<string, string>
            {
                [ContainerHostCommandPlatform.ExecutableMetadataKey] = "docker-custom"
            });
        var platform = new ContainerHostCommandPlatform(
            [new StaticContainerHostProvider(host)],
            new TestHostToolResolver("docker-custom"));

        var plan = platform.CreatePlan();
        var startInfo = plan.CreateStartInfo(["ps"]);

        Assert.True(plan.IsAvailable);
        Assert.Equal("docker-custom", startInfo.FileName);
        Assert.Equal("tcp://docker.example.test:2376", startInfo.Environment["DOCKER_HOST"]);
    }

    [Fact]
    public void CreatePlan_UsesCustomExecutableMetadataForPodmanHostWhenAvailable()
    {
        var host = new ContainerHostDescriptor(
            "podman:custom",
            "Custom Podman",
            ContainerHostKind.Podman,
            "unix:///run/user/501/podman/podman.sock",
            Metadata: new Dictionary<string, string>
            {
                [ContainerHostCommandPlatform.ExecutableMetadataKey] = "podman-custom"
            });
        var platform = new ContainerHostCommandPlatform(
            [new StaticContainerHostProvider(host)],
            new TestHostToolResolver("podman-custom"));

        var plan = platform.CreatePlan();
        var startInfo = plan.CreateStartInfo(["ps"]);

        Assert.True(plan.IsAvailable);
        Assert.Equal("podman-custom", startInfo.FileName);
        Assert.Equal("unix:///run/user/501/podman/podman.sock", startInfo.Environment["CONTAINER_HOST"]);
    }

    [Fact]
    public void DockerCommandRunner_ReturnsUnavailableResultWithoutStartingProcess()
    {
        var runner = new ProcessLocalDockerContainerCommandRunner(
            new ContainerHostCommandPlatform([], new TestHostToolResolver()));

        var result = runner.Run(["ps"], throwOnError: false);

        Assert.Equal(LocalDockerContainerCommandResult.UnavailableExitCode, result.ExitCode);
        Assert.Contains("Docker executable 'docker' is unavailable", result.Error);
    }

    [Fact]
    public async Task DockerCommandRunner_ThrowsStableUnavailableReasonWhenRequested()
    {
        var runner = new ProcessLocalDockerContainerCommandRunner(
            new ContainerHostCommandPlatform([], new TestHostToolResolver()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(["ps"], CancellationToken.None));

        Assert.Contains("Docker executable 'docker' is unavailable", exception.Message);
    }

    [Fact]
    public void SqlServerDockerCommandRunner_ReturnsUnavailableResultWithoutStartingProcess()
    {
        var runner = new ProcessLocalSqlServerDockerCommandRunner(
            new ContainerHostCommandPlatform([], new TestHostToolResolver()));

        var result = runner.Run(["ps"], throwOnError: false);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("Docker executable 'docker' is unavailable", result.Error);
    }

    [Fact]
    public void RabbitMQDockerCommandRunner_ReturnsUnavailableResultWithoutStartingProcess()
    {
        var runner = new ProcessLocalRabbitMQDockerCommandRunner(
            new ContainerHostCommandPlatform([], new TestHostToolResolver()));

        var result = runner.Run(["ps"], throwOnError: false);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("Docker executable 'docker' is unavailable", result.Error);
    }

    [Fact]
    public void ContainerApplicationCommandRunner_ReturnsUnavailableResultForDockerCommand()
    {
        var runner = new ProcessLocalContainerApplicationCommandRunner(
            new ContainerHostCommandPlatform([], new TestHostToolResolver()));

        var result = runner.Run("docker", ["ps"], throwOnError: false);

        Assert.Equal(LocalContainerApplicationCommandResult.UnavailableExitCode, result.ExitCode);
        Assert.Contains("Docker executable 'docker' is unavailable", result.Error);
    }

    [Fact]
    public async Task ContainerApplicationCommandRunner_ThrowsStableUnavailableReasonForDockerCommand()
    {
        var runner = new ProcessLocalContainerApplicationCommandRunner(
            new ContainerHostCommandPlatform([], new TestHostToolResolver()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync("docker", ["ps"], CancellationToken.None));

        Assert.Contains("Docker executable 'docker' is unavailable", exception.Message);
    }

    [Fact]
    public void ContainerApplicationCommandRunner_UsesPodmanUnavailableReasonForPodmanHost()
    {
        var host = new ContainerHostDescriptor(
            "podman:default",
            "Default Podman",
            ContainerHostKind.Podman,
            "unix:///run/user/501/podman/podman.sock");
        var runner = new ProcessLocalContainerApplicationCommandRunner(
            new ContainerHostCommandPlatform(
                [new StaticContainerHostProvider(host)],
                new TestHostToolResolver()));

        var result = runner.Run("docker", ["ps"], throwOnError: false);

        Assert.Equal(LocalContainerApplicationCommandResult.UnavailableExitCode, result.ExitCode);
        Assert.Contains("Podman executable 'podman' is unavailable", result.Error);
    }

    private sealed class TestHostToolResolver(params string[] availableTools) : IHostToolResolver
    {
        private readonly HashSet<string> availableTools = new(
            availableTools,
            StringComparer.OrdinalIgnoreCase);

        public bool IsAvailable(string fileName) => availableTools.Contains(fileName);
    }

    private sealed class StaticContainerHostProvider(ContainerHostDescriptor host) : IContainerHostProvider
    {
        public ContainerHostDescriptor GetDefaultHost() => host;
    }
}
