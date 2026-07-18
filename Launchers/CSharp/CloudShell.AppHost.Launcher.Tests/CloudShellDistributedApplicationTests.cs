using System.Text.Json;
using System.Text.Json.Nodes;
using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ResourceModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                .WithSeed(seed => seed.Setting("Sample--Message", "Hello from C#"));

            var secrets = resources
                .AddSecretsVault("secrets")
                .WithEndpoint("http://localhost:6101")
                .WithSeed(seed => seed.Secret("Sample--ApiKey", "csharp-secret", "v1"));

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
        var entry = Assert.Single(settings.ResourceAttributeValues.GetObject<ConfigurationStoreSeedSetting[]>(
            ConfigurationStoreResourceTypeProvider.Attributes.Settings)!);
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
    public void BuildTemplate_MatchesJavaScriptAppParityFixture()
    {
        var app = CloudShellDistributedApplication
            .CreateBuilder("launcher-parity-javascript")
            .WithMetadata("cloudshell.parity", "javascript-app");

        app.DefineResources(resources =>
        {
            var settings = resources
                .AddConfigurationStore("settings")
                .WithDisplayName("Settings")
                .WithEndpoint("http://localhost:5101")
                .WithSeed(seed => seed.Setting("Sample--Message", "Hello from launcher parity"));

            var secrets = resources
                .AddSecretsVault("secrets")
                .WithDisplayName("Secrets")
                .WithEndpoint("http://localhost:6101")
                .WithSeed(seed => seed.Secret("Sample--ApiKey", "parity-secret", "v1"));

            resources
                .AddJavaScriptApp("frontend", "samples/LauncherParity/App")
                .WithDisplayName("Frontend")
                .WithServiceDiscovery()
                .WithReference(settings)
                .WithReference(secrets)
                .DependsOn(settings)
                .DependsOn(secrets)
                .WithEnvironmentVariable("PORT", "5173")
                .WithEnvironmentVariable("Sample__Message", settings.Setting("Sample--Message"))
                .WithEnvironmentVariable("Sample__ApiKey", secrets.Secret("Sample--ApiKey"))
                .WithHttpEndpoint(host: "localhost", port: 5173, targetPort: 5173)
                .WithHttpHealthCheck("/healthz", endpointName: "http")
                .WithHttpLivenessCheck("/alive", endpointName: "http");
        });

        var templateJson = ResourceTemplateSerializer.SerializeTemplate(
            app.BuildTemplate(),
            ResourceTemplateFormat.Json,
            new ResourceTemplateSerializerOptions(
                app.Resources.ResourceTypeDefinitions.Values,
                app.Resources.ResourceCapabilityAttributeProviders.Values));

        var expected = CanonicalizeTemplateJson(ReadLauncherParityFixture("javascript-app-parity.json"));
        var actual = CanonicalizeTemplateJson(templateJson);

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Expected:{Environment.NewLine}{FormatJson(expected)}{Environment.NewLine}" +
            $"Actual:{Environment.NewLine}{FormatJson(actual)}");
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

    [Fact]
    public async Task BuildTemplate_WritesTemplateThatAppliesToInMemoryControlPlane()
    {
        using var directory = new TemporaryDirectory();
        var app = CloudShellDistributedApplication
            .CreateBuilder("launcher-smoke")
            .WithMetadata("cloudshell.smoke", "template-apply");

        app.DefineResources(resources =>
        {
            var settings = resources
                .AddConfigurationStore("settings")
                .WithEndpoint("http://localhost:5101/api/configuration/stores/settings/settings")
                .WithSeed(seed => seed.Setting("Sample--Message", "Hello from launcher smoke"));

            resources
                .AddJavaScriptApp("frontend", "samples/LauncherParity/App")
                .WithPackageManager("npm")
                .WithScript("dev")
                .WithReference(settings)
                .WithEnvironmentVariable("Sample__Message", settings.Setting("Sample--Message"))
                .WithHttpEndpoint(host: "localhost", port: 5173, targetPort: 5173);
        });

        var templatePath = Path.Combine(directory.Path, "resources.yaml");
        await CloudShellHostLauncher.WriteTemplateAsync(
            app.BuildTemplate(environmentId: "local"),
            templatePath,
            ResourceTemplateFormat.Yaml,
            new ResourceTemplateSerializerOptions(
                app.Resources.ResourceTypeDefinitions.Values,
                app.Resources.ResourceCapabilityAttributeProviders.Values));

        var document = await File.ReadAllTextAsync(templatePath);
        var roundTripped = ResourceTemplateSerializer.DeserializeTemplate(document);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddJavaScriptAppResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                roundTripped,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();

        Assert.Contains(snapshot.Resources, resource =>
            resource.EffectiveResourceId == "configuration.store:settings");
        Assert.Contains(snapshot.Resources, resource =>
            resource.EffectiveResourceId == "application.javascript-app:frontend");
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

    private static string ReadLauncherParityFixture(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "Launchers", "testdata", name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate launcher parity fixture '{name}'.");
    }

    private static JsonNode? CanonicalizeTemplateJson(string json)
    {
        var node = JsonNode.Parse(json) ??
            throw new InvalidOperationException("Could not parse template JSON.");
        return CanonicalizeNode(node);
    }

    private static string FormatJson(JsonNode? node) =>
        node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ??
        string.Empty;

    private static JsonNode? CanonicalizeNode(JsonNode? node, string? propertyName = null) =>
        node switch
        {
            JsonObject value => CanonicalizeObject(value),
            JsonArray value => CanonicalizeArray(value, propertyName),
            null => null,
            _ => JsonNode.Parse(node.ToJsonString())
        };

    private static JsonObject CanonicalizeObject(JsonObject value)
    {
        if (value.ContainsKey("resourceId") &&
            !value.ContainsKey("name") &&
            !value.ContainsKey("type"))
        {
            return new JsonObject
            {
                ["resourceId"] = CanonicalizeNode(value["resourceId"])
            };
        }

        var result = new JsonObject();
        foreach (var property in value.OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            result[property.Key] = CanonicalizeNode(property.Value, property.Key);
        }

        return result;
    }

    private static JsonArray CanonicalizeArray(JsonArray value, string? propertyName)
    {
        var items = value
            .Select(item => CanonicalizeNode(item))
            .ToList();
        if (string.Equals(propertyName, "resources", StringComparison.Ordinal))
        {
            items = items
                .OrderBy(item => item?["resourceId"]?.GetValue<string>(), StringComparer.Ordinal)
                .ToList();
        }

        var result = new JsonArray();
        foreach (var item in items)
        {
            result.Add(item);
        }

        return result;
    }
}
