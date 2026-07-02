using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;

namespace CloudShell.AppHost.Launcher.Tests;

public sealed class CloudShellDistributedApplicationTests
{
    [Fact]
    public void BuildTemplate_UsesResourceGraphBuilder()
    {
        var app = CloudShellDistributedApplication.CreateBuilder(
            "csharp-launcher-host",
            ["--apply"]);

        app.DefineResources(resources =>
        {
            var settings = resources
                .AddConfigurationStore("settings")
                .WithEndpoint("http://localhost:5101");

            resources
                .AddJavaScriptApp("frontend", "App")
                .WithReference(settings)
                .WithHttpEndpoint(host: "localhost", port: 5173, targetPort: 5173);
        });

        var template = app.BuildTemplate(
            environmentId: "local",
            metadata: new Dictionary<string, string>
            {
                ["cloudshell.source"] = "csharp"
            });

        Assert.Equal("csharp-launcher-host", template.Name);
        Assert.Equal("local", template.EnvironmentId);
        Assert.Equal("csharp", template.Metadata?["cloudshell.source"]);
        Assert.Contains(template.Resources, resource =>
            resource.EffectiveResourceId == "configuration.store:settings");
        Assert.Contains(template.Resources, resource =>
            resource.EffectiveResourceId == "application.javascript-app:frontend");
    }

    [Fact]
    public void BuildTemplateApplyArguments_MapsLauncherOptionsToCliArguments()
    {
        var arguments = CloudShellHostLauncher.BuildTemplateApplyArguments(
            "resources.yaml",
            new CloudShellHostLauncherOptions
            {
                ControlPlaneUrl = new Uri("http://127.0.0.1:5200"),
                StateDirectory = ".cloudshell",
                StartHost = true,
                HostProjectPath = "Host/CloudShell.Host.csproj",
                HostUrl = new Uri("http://127.0.0.1:5200"),
                NoBuild = true,
                TimeoutSeconds = 30,
                Mode = ResourceDefinitionApplyMode.UpdateExisting,
                BearerToken = "token"
            });

        Assert.Equal(
            [
                "template",
                "apply",
                "resources.yaml",
                "--control-plane",
                "http://127.0.0.1:5200",
                "--state-dir",
                ".cloudshell",
                "--host-project",
                "Host/CloudShell.Host.csproj",
                "--url",
                "http://127.0.0.1:5200",
                "--timeout-seconds",
                "30",
                "--mode",
                "update-existing",
                "--bearer-token",
                "token",
                "--start",
                "--no-build"
            ],
            arguments);
    }

    [Fact]
    public async Task ApplyAsync_WritesTemplateAndRunsCliProject()
    {
        using var directory = new TemporaryDirectory();
        var runner = new RecordingCommandRunner();
        var template = new ResourceGraphBuilder()
            .BuildTemplate("empty");

        var result = await CloudShellHostLauncher.ApplyAsync(
            template,
            new CloudShellHostLauncherOptions
            {
                CliProjectPath = "CloudShell.Cli/CloudShell.Cli.csproj",
                TemplatePath = Path.Combine(directory.Path, "resources.yaml"),
                StateDirectory = Path.Combine(directory.Path, ".cloudshell"),
                StartHost = true
            },
            runner);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("dotnet", result.Command);
        Assert.Equal("run", result.Arguments[0]);
        Assert.Equal("--project", result.Arguments[1]);
        Assert.Equal("CloudShell.Cli/CloudShell.Cli.csproj", result.Arguments[2]);
        Assert.Equal("--", result.Arguments[3]);
        Assert.Equal("template", result.Arguments[4]);
        Assert.Equal("apply", result.Arguments[5]);
        Assert.True(File.Exists(result.TemplatePath));
        Assert.Contains("name: empty", await File.ReadAllTextAsync(result.TemplatePath));
        Assert.Equal("dotnet", runner.Command);
    }

    private sealed class RecordingCommandRunner : ICloudShellHostLauncherCommandRunner
    {
        public string? Command { get; private set; }

        public IReadOnlyList<string>? Arguments { get; private set; }

        public Task<int> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            CloudShellHostLauncherOptions options,
            CancellationToken cancellationToken)
        {
            Command = command;
            Arguments = arguments;
            return Task.FromResult(0);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"cloudshell-launcher-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Test cleanup should not hide assertion failures.
            }
        }
    }
}
