using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;
using Microsoft.Extensions.Configuration;

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
                .WithEndpoint("http://localhost:5101")
                .WithSetting("Sample--Message", "Hello from C#");

            var secrets = resources
                .AddSecretsVault("secrets")
                .WithEndpoint("http://localhost:6101")
                .WithSecret("Sample--ApiKey", "csharp-secret", "v1");

            resources
                .AddJavaScriptApp("frontend", "App")
                .WithReference(settings)
                .WithReference(secrets)
                .WithHttpEndpoint(host: "localhost", port: 5173, targetPort: 5173);

            resources
                .AddJavaApp("api", "JavaApp", "target/api.jar")
                .WithHttpEndpoint(host: "localhost", port: 5185, targetPort: 5185);
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
            resource.EffectiveResourceId == "secrets.vault:secrets");
        Assert.Contains(template.Resources, resource =>
            resource.EffectiveResourceId == "application.javascript-app:frontend");
        Assert.Contains(template.Resources, resource =>
            resource.EffectiveResourceId == "application.java-app:api");

        var settings = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == "configuration.store:settings");
        var entry = Assert.Single(settings.ResourceAttributeValues.GetObject<ConfigurationStoreSettingEntry[]>(
            ConfigurationStoreResourceTypeProvider.Attributes.Entries)!);
        Assert.Equal("Sample--Message", entry.Name);
        Assert.Equal("Hello from C#", entry.Value);

        var secrets = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == "secrets.vault:secrets");
        var secret = Assert.Single(secrets.ResourceAttributeValues.GetObject<SecretsVaultSeedSecret[]>(
            SecretsVaultResourceTypeProvider.Attributes.Secrets)!);
        Assert.Equal("Sample--ApiKey", secret.Name);
        Assert.Equal("csharp-secret", secret.Value);
        Assert.Equal("v1", secret.Version);
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
                DataDirectory = ".cloudshell/data",
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
                "--data-dir",
                ".cloudshell/data",
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
    public void BuildHostRunArguments_MapsLauncherOptionsToForegroundHostCommand()
    {
        var arguments = CloudShellHostLauncher.BuildHostRunArguments(
            new CloudShellHostLauncherOptions
            {
                HostProjectPath = "Host/CloudShell.Host.csproj",
                HostUrl = new Uri("http://127.0.0.1:5200"),
                DataDirectory = ".cloudshell/data",
                NoBuild = true
            },
            new Uri("http://127.0.0.1:5200"));

        Assert.Equal(
            [
                "run",
                "--project",
                "Host/CloudShell.Host.csproj",
                "--no-build",
                "--",
                "--urls",
                "http://127.0.0.1:5200",
                "--CloudShell:DataDirectory",
                ".cloudshell/data"
            ],
            arguments);
    }

    [Fact]
    public void FormatHostUrlMessage_PrintsUiUrl()
    {
        var message = CloudShellHostLauncher.FormatHostUrlMessage(
            new Uri("http://127.0.0.1:5200/"));

        Assert.Equal("CloudShell UI: http://127.0.0.1:5200", message);
    }

    [Fact]
    public void FromArguments_ForwardsAppHostSettingsWithoutOverridingConfiguredDataDirectory()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "appsettings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "CloudShell": {
                "DataDirectory": "Data"
              }
            }
            """);
        var configuration = new ConfigurationBuilder()
            .SetBasePath(directory.Path)
            .AddJsonFile("appsettings.json")
            .Build();

        var options = CloudShellHostLauncherOptions.FromArguments(
            [],
            directory.Path,
            configuration);

        Assert.Equal(settingsPath, options.HostSettingsPath);
        Assert.Null(options.DataDirectory);
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
