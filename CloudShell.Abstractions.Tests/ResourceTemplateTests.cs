using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Templates;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceTemplateTests
{
    [Fact]
    public void ApplicationResourceStore_LoadsPersistedHealthChecks()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var options = new ApplicationProviderOptions
            {
                DefinitionsPath = "application-resources.json"
            };
            var environment = new TestHostEnvironment(contentRoot);
            var store = new ApplicationResourceStore(options, environment);
            store.Save(
                new ApplicationResourceDefinition(
                    "application:api",
                    "API",
                    "dotnet",
                    "run",
                    "/workspace",
                    healthChecks:
                    [
                        new ResourceHealthCheck(
                            "/healthz",
                            ResourceProbeType.Health,
                            "http",
                            "health",
                            TimeSpan.FromSeconds(2),
                            IntervalSeconds: 15),
                        new ResourceHealthCheck(
                            new ResourceProbeSource(
                                "provider.process",
                                Metadata: new Dictionary<string, string>
                                {
                                    ["provider"] = "applications"
                                }),
                            ResourceProbeType.Liveness,
                            "process",
                            intervalSeconds: 30)
                    ]));

            var reloadedStore = new ApplicationResourceStore(options, environment);
            var application = reloadedStore.GetApplication("application:api");

            Assert.NotNull(application);
            Assert.Equal(2, application.HealthChecks.Count);
            Assert.Equal("/healthz", application.HealthChecks[0].Path);
            Assert.Equal(ResourceProbeType.Health, application.HealthChecks[0].Type);
            Assert.Equal("http", application.HealthChecks[0].EndpointName);
            Assert.Equal(TimeSpan.FromSeconds(2), application.HealthChecks[0].Timeout);
            Assert.Equal(15, application.HealthChecks[0].IntervalSeconds);
            Assert.Equal("provider.process", application.HealthChecks[1].EffectiveSource.Kind);
            Assert.Equal("applications", application.HealthChecks[1].EffectiveSource.Metadata?["provider"]);
            Assert.Equal(30, application.HealthChecks[1].IntervalSeconds);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApplicationProvider_ExportsProviderOwnedConfiguration()
    {
        using var fixture = new TemplateFixture();

        var resource = Assert.Single(fixture.ResourceManager.GetResources());
        var provider = Assert.IsAssignableFrom<IResourceTemplateProvider>(
            Assert.Single(fixture.ResourceManager.Providers));

        var template = await provider.ExportAsync(
            resource,
            new ResourceTemplateExportContext(
                fixture.Registrations.GetRegistration(resource.Id)!,
                fixture.Group));

        Assert.Equal(ApplicationResourceProviderIds.Executable, template.ProviderId);
        Assert.Equal("application:example-web-api", template.ResourceId);
        Assert.Equal("application.executable", template.ResourceType);
        Assert.Equal("1.0", template.ProviderConfigurationVersion);
        Assert.Equal("dotnet", template.Configuration.GetProperty("executablePath").GetString());
        Assert.Equal("run", template.Configuration.GetProperty("arguments").GetString());
    }

    [Fact]
    public async Task ApplicationProvider_ExportsConfiguredDependencies()
    {
        using var fixture = new TemplateFixture();

        await fixture.Provider.UpdateApplicationAsync(
            new ApplicationResourceDefinition(
                "application:example-web-api",
                "Example Web API",
                "dotnet",
                "run",
                "/workspace",
                "http://localhost:5127",
                [new("ASPNETCORE_URLS", "http://localhost:5127")],
                dependsOn: ["postgres-main"],
                references: ["postgres-main"],
                useServiceDiscovery: true,
                observability: new ResourceObservability(
                    Logs: true,
                    Traces: true,
                    Metrics: false,
                    OtlpEndpoint: "http://localhost:4317")),
            fixture.Group.Id,
            fixture.Registrations);

        var resource = Assert.Single(fixture.ResourceManager.GetResources());
        var provider = Assert.IsAssignableFrom<IResourceTemplateProvider>(
            Assert.Single(fixture.ResourceManager.Providers));

        var template = await provider.ExportAsync(
            resource,
            new ResourceTemplateExportContext(
                fixture.Registrations.GetRegistration(resource.Id)!,
                fixture.Group));

        Assert.Equal(["postgres-main"], template.DependsOn);
        Assert.Equal(
            ["postgres-main"],
            template.Configuration
                .GetProperty("references")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
        Assert.True(template.Configuration.GetProperty("useServiceDiscovery").GetBoolean());
        var observability = template.Configuration.GetProperty("observability");
        Assert.True(observability.GetProperty("logs").GetBoolean());
        Assert.True(observability.GetProperty("traces").GetBoolean());
        Assert.False(observability.GetProperty("metrics").GetBoolean());
        Assert.Equal("http://localhost:4317", observability.GetProperty("otlpEndpoint").GetString());
    }

    [Fact]
    public async Task ApplicationProvider_ExportsReferenceBackedSettings()
    {
        using var fixture = new TemplateFixture();

        await fixture.Provider.UpdateApplicationAsync(
            new ApplicationResourceDefinition(
                "application:example-web-api",
                "Example Web API",
                "dotnet",
                "run",
                "/workspace",
                "http://localhost:5127",
                [
                    new EnvironmentVariableAssignment("ASPNETCORE_URLS", "http://localhost:5127"),
                    EnvironmentVariableAssignment.FromConfiguration(
                        "ConnectionStrings__Default",
                        new ConfigurationEntryReference(
                            "configuration:app",
                            "ConnectionStrings:Default",
                            "v1")),
                    EnvironmentVariableAssignment.FromSecret(
                        "EXTERNAL_API_KEY",
                        new SecretReference(
                            "secrets-vault:app",
                            "ExternalApiKey",
                            "current"))
                ],
                dependsOn: ["configuration:app", "secrets-vault:app"],
                appSettings:
                [
                    AppSetting.FromConfiguration(
                        "FeatureFlags:Preview",
                        new ConfigurationEntryReference("configuration:app", "FeatureFlags:Preview")),
                    AppSetting.FromSecret(
                        "ConnectionStrings:Password",
                        new SecretReference("secrets-vault:app", "DatabasePassword"))
                ]),
            fixture.Group.Id,
            fixture.Registrations);

        var resource = Assert.Single(fixture.ResourceManager.GetResources());
        var provider = Assert.IsAssignableFrom<IResourceTemplateProvider>(
            Assert.Single(fixture.ResourceManager.Providers));

        var template = await provider.ExportAsync(
            resource,
            new ResourceTemplateExportContext(
                fixture.Registrations.GetRegistration(resource.Id)!,
                fixture.Group));
        var environmentVariables = template.Configuration
            .GetProperty("environmentVariables")
            .EnumerateArray()
            .ToArray();
        var appSettings = template.Configuration
            .GetProperty("appSettings")
            .EnumerateArray()
            .ToArray();

        var configurationVariable = Assert.Single(environmentVariables, variable =>
            variable.GetProperty("name").GetString() == "ConnectionStrings__Default");
        var configurationEntry = configurationVariable.GetProperty("configurationEntry");
        Assert.Equal("configuration:app", configurationEntry.GetProperty("storeResourceId").GetString());
        Assert.Equal("ConnectionStrings:Default", configurationEntry.GetProperty("entryName").GetString());
        Assert.Equal("v1", configurationEntry.GetProperty("version").GetString());

        var secretVariable = Assert.Single(environmentVariables, variable =>
            variable.GetProperty("name").GetString() == "EXTERNAL_API_KEY");
        var secret = secretVariable.GetProperty("secret");
        Assert.Equal("secrets-vault:app", secret.GetProperty("vaultResourceId").GetString());
        Assert.Equal("ExternalApiKey", secret.GetProperty("secretName").GetString());
        Assert.Equal("current", secret.GetProperty("version").GetString());

        var configurationSetting = Assert.Single(appSettings, setting =>
            setting.GetProperty("name").GetString() == "FeatureFlags:Preview");
        Assert.Equal(
            "configuration:app",
            configurationSetting
                .GetProperty("configurationEntry")
                .GetProperty("storeResourceId")
                .GetString());

        var secretSetting = Assert.Single(appSettings, setting =>
            setting.GetProperty("name").GetString() == "ConnectionStrings:Password");
        Assert.Equal(
            "DatabasePassword",
            secretSetting
                .GetProperty("secret")
                .GetProperty("secretName")
                .GetString());
    }

    [Fact]
    public async Task TemplateService_ExportsAndImportsAResourceGroup()
    {
        using var fixture = new TemplateFixture();
        var service = fixture.CreateTemplateService();

        var export = await service.ExportGroupAsync(fixture.Group.Id);
        var importTemplate = export.Template with
        {
            Resources = export.Template.Resources
                .Select(resource => resource with { ResourceId = $"{resource.ResourceId}-copy" })
                .ToArray()
        };
        var import = await service.ImportGroupAsync(importTemplate);

        Assert.Empty(export.Diagnostics);
        Assert.Empty(import.Diagnostics);
        Assert.NotNull(import.ResourceGroup);
        Assert.Equal("Local Development", import.ResourceGroup.Name);
        Assert.Single(import.ImportedResources);
        Assert.Equal(2, fixture.Provider.GetApplications().Count);
        Assert.Contains(
            fixture.Provider.GetApplications(),
            application => application.Id == "application:example-web-api-copy");
    }

    [Fact]
    public async Task TemplateService_RejectsDuplicateResourceIds()
    {
        using var fixture = new TemplateFixture();
        var service = fixture.CreateTemplateService();

        var export = await service.ExportGroupAsync(fixture.Group.Id);
        var import = await service.ImportGroupAsync(export.Template);

        Assert.Empty(import.ImportedResources);
        var diagnostic = Assert.Single(import.Diagnostics);
        Assert.Equal("Error", diagnostic.Severity);
        Assert.Contains("Resource id 'application:example-web-api' is already in use.", diagnostic.Message);
    }

    [Theory]
    [InlineData("application", "1.0", "Only resource group templates can be imported.")]
    [InlineData("resourceGroup", "2.0", "Template version '2.0' is not supported.")]
    public async Task TemplateService_ReturnsDiagnosticsForInvalidTemplateEnvelope(
        string kind,
        string templateVersion,
        string expectedMessage)
    {
        using var fixture = new TemplateFixture();
        var service = fixture.CreateTemplateService();
        var template = new ResourceGroupTemplate(
            templateVersion,
            kind,
            "Invalid group",
            null,
            []);

        var import = await service.ImportGroupAsync(template);

        Assert.Null(import.ResourceGroup);
        Assert.Empty(import.ImportedResources);
        var diagnostic = Assert.Single(import.Diagnostics);
        Assert.Equal("Error", diagnostic.Severity);
        Assert.Equal("Invalid group", diagnostic.ResourceName);
        Assert.Equal(expectedMessage, diagnostic.Message);
        Assert.DoesNotContain(
            fixture.ResourceGroups.GetResourceGroups(),
            group => string.Equals(group.Name, "Invalid group", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TemplateService_ImportsDependenciesIntoRegistration()
    {
        using var fixture = new TemplateFixture();
        var service = fixture.CreateTemplateService();
        var configuration = JsonSerializer.SerializeToElement(
            new
            {
                executablePath = "dotnet",
                arguments = "run",
                workingDirectory = "/workspace",
                endpoint = "http://localhost:5127",
                environmentVariables = Array.Empty<EnvironmentVariableAssignment>(),
                lifetime = ApplicationLifetime.Detached,
                references = new[] { "postgres-main" },
                useServiceDiscovery = true,
                observability = new ResourceObservability(
                    Logs: true,
                    Traces: false,
                    Metrics: true,
                    ServiceName: "imported-api")
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var template = new ResourceGroupTemplate(
            "1.0",
            "resourceGroup",
            "Imported group",
            null,
            [
                new ResourceTemplateDefinition(
                    "Imported API",
                    ApplicationResourceProviderIds.Executable,
                    "application.executable",
                    ["postgres-main"],
                    "1.0",
                    configuration)
            ]);

        var result = await service.ImportGroupAsync(template);

        var imported = Assert.Single(result.ImportedResources);
        var registration = fixture.Registrations.GetRegistration(imported.ResourceId);
        Assert.NotNull(registration);
        Assert.Equal(["postgres-main"], registration.DependsOn);
        var application = fixture.Provider.GetApplication(imported.ResourceId)!;
        Assert.Equal(["postgres-main"], application.References);
        Assert.True(application.UseServiceDiscovery);
        Assert.NotNull(application.Observability);
        Assert.True(application.Observability.Logs);
        Assert.False(application.Observability.Traces);
        Assert.True(application.Observability.Metrics);
        Assert.Equal("imported-api", application.Observability.ServiceName);
    }

    [Fact]
    public async Task TemplateService_ImportsReferenceBackedSettings()
    {
        using var fixture = new TemplateFixture();
        var service = fixture.CreateTemplateService();
        var configuration = JsonSerializer.SerializeToElement(
            new
            {
                executablePath = "dotnet",
                arguments = "run",
                workingDirectory = "/workspace",
                endpoint = "http://localhost:5127",
                environmentVariables = new EnvironmentVariableAssignment[]
                {
                    new("ASPNETCORE_ENVIRONMENT", "Development"),
                    EnvironmentVariableAssignment.FromConfiguration(
                        "ConnectionStrings__Default",
                        new ConfigurationEntryReference("configuration:app", "ConnectionStrings:Default")),
                    EnvironmentVariableAssignment.FromSecret(
                        "EXTERNAL_API_KEY",
                        new SecretReference("secrets-vault:app", "ExternalApiKey"))
                },
                appSettings = new AppSetting[]
                {
                    AppSetting.FromConfiguration(
                        "FeatureFlags:Preview",
                        new ConfigurationEntryReference("configuration:app", "FeatureFlags:Preview")),
                    AppSetting.FromSecret(
                        "ConnectionStrings:Password",
                        new SecretReference("secrets-vault:app", "DatabasePassword"))
                },
                lifetime = ApplicationLifetime.Detached,
                references = Array.Empty<string>(),
                useServiceDiscovery = false
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var template = new ResourceGroupTemplate(
            "1.0",
            "resourceGroup",
            "Imported group",
            null,
            [
                new ResourceTemplateDefinition(
                    "Imported API",
                    ApplicationResourceProviderIds.Executable,
                    "application.executable",
                    ["configuration:app", "secrets-vault:app"],
                    "1.0",
                    configuration)
            ]);

        var result = await service.ImportGroupAsync(template);

        var imported = Assert.Single(result.ImportedResources);
        var application = fixture.Provider.GetApplication(imported.ResourceId)!;
        var registration = fixture.Registrations.GetRegistration(imported.ResourceId);

        Assert.Equal(["configuration:app", "secrets-vault:app"], application.DependsOn);
        Assert.Equal(["configuration:app", "secrets-vault:app"], registration?.DependsOn);
        Assert.Contains(
            application.EnvironmentVariables,
            variable => variable.Name == "ConnectionStrings__Default" &&
                variable.ConfigurationEntry?.StoreResourceId == "configuration:app" &&
                variable.ConfigurationEntry.EntryName == "ConnectionStrings:Default");
        Assert.Contains(
            application.EnvironmentVariables,
            variable => variable.Name == "EXTERNAL_API_KEY" &&
                variable.Secret?.VaultResourceId == "secrets-vault:app" &&
                variable.Secret.SecretName == "ExternalApiKey");
        Assert.Contains(
            application.AppSettings,
            setting => setting.Name == "FeatureFlags:Preview" &&
                setting.ConfigurationEntry?.StoreResourceId == "configuration:app" &&
                setting.ConfigurationEntry.EntryName == "FeatureFlags:Preview");
        Assert.Contains(
            application.AppSettings,
            setting => setting.Name == "ConnectionStrings:Password" &&
                setting.Secret?.VaultResourceId == "secrets-vault:app" &&
                setting.Secret.SecretName == "DatabasePassword");
    }

    private sealed class TemplateFixture : IDisposable
    {
        private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemplateFixture()
        {
            var options = new ApplicationProviderOptions
            {
                DefinitionsPath = "application-resources.json",
                RuntimeStatePath = "application-runtime-state.json",
                ContainerDeploymentHistoryPath = "application-container-deployments.json",
                LogDirectory = "application-logs"
            };
            var environment = new TestHostEnvironment(_contentRoot);
            var store = new ApplicationResourceStore(options, environment);
            var processOptions = new LocalProcessOptions
            {
                RuntimeStatePath = options.RuntimeStatePath,
                LogDirectory = options.LogDirectory
            };
            var runtimeStates = new ApplicationRuntimeStateStore(processOptions, environment);
            var containerDeployments = new ApplicationContainerDeploymentStore(options, environment);
            var localProcesses = new LocalProcessRunner(runtimeStates, processOptions, environment);
            var definitionNormalizer = new ApplicationResourceDefinitionNormalizer(environment);
            var definitionSource = new ApplicationResourceDefinitionSource(store, definitionNormalizer);
            var definitionRegistrations = new ApplicationResourceDefinitionRegistrationService(store, definitionNormalizer);
            var registrationOperations = new ApplicationResourceRegistrationOperations(
                definitionSource,
                definitionRegistrations);
            var declarationOperations = new ApplicationResourceDeclarationOperations(
                options,
                definitionSource,
                registrationOperations);
            var templateOperations = new ApplicationResourceTemplateOperations(
                options,
                definitionSource,
                registrationOperations,
                definitionRegistrations);
            var services = new ServiceCollection().BuildServiceProvider();
            Provider = new ApplicationResourceService(
                store,
                runtimeStates,
                containerDeployments,
                localProcesses,
                options,
                environment,
                services,
                [],
                [],
                [],
                [],
                new ResourceDeclarationStore());
            ResourceProvider = new ExecutableApplicationResourceProvider(
                Provider,
                definitionSource,
                Provider,
                templateOperations,
                declarationOperations,
                Provider,
                Provider);
            Group = new ResourceGroup("group-1", "Local Development", "Development resources", ["application:example-web-api"]);
            Registrations = new TestRegistrationStore();
            ResourceGroups = new TestResourceGroupStore(Group);
            ResourceManager = new TestResourceManagerStore(Provider, ResourceProvider, ResourceGroups);

            Provider.SetupApplicationAsync(
                    new ApplicationResourceDefinition(
                        "application:example-web-api",
                        "Example Web API",
                        "dotnet",
                        "run",
                        "/workspace",
                        "http://localhost:5127",
                        [new("ASPNETCORE_URLS", "http://localhost:5127")]),
                    Group.Id,
                    Registrations)
                .GetAwaiter()
                .GetResult();
        }

        public ApplicationResourceService Provider { get; }

        public ExecutableApplicationResourceProvider ResourceProvider { get; }

        public ResourceGroup Group { get; }

        public TestRegistrationStore Registrations { get; }

        public TestResourceGroupStore ResourceGroups { get; }

        public TestResourceManagerStore ResourceManager { get; }

        public ResourceTemplateService CreateTemplateService() =>
            new(ResourceManager, ResourceGroups, Registrations);

        public void Dispose()
        {
            Provider.Dispose();
            if (Directory.Exists(_contentRoot))
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestResourceManagerStore(
        ApplicationResourceService provider,
        IResourceProvider resourceProvider,
        TestResourceGroupStore resourceGroups) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers { get; } = [resourceProvider];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => resourceGroups.GetResourceGroups();

        public IReadOnlyList<Resource> GetAvailableResources() => [];

        public IReadOnlyList<Resource> GetResources() => provider.GetResources();

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string resourceId) =>
            GetResources().FirstOrDefault(resource =>
                string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) =>
            resourceGroups.GetGroupForResource(resourceId);

        public bool IsRegistered(string resourceId) =>
            GetResource(resourceId) is not null;
    }

    private sealed class TestResourceGroupStore(ResourceGroup initialGroup) : IResourceGroupStore
    {
        private readonly List<ResourceGroup> _groups = [initialGroup];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => _groups.ToArray();

        public ResourceGroup? GetGroupForResource(string resourceId) =>
            _groups.FirstOrDefault(group =>
                group.ResourceIds.Contains(resourceId, StringComparer.OrdinalIgnoreCase));

        public Task<ResourceGroup> CreateAsync(
            string name,
            string description,
            CancellationToken cancellationToken = default)
        {
            var group = new ResourceGroup(
                $"group-{_groups.Count + 1}",
                name,
                description,
                []);
            _groups.Add(group);
            return Task.FromResult(group);
        }
    }

    private sealed class TestRegistrationStore : IResourceRegistrationStore
    {
        private readonly Dictionary<string, ResourceRegistration> _registrations =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ResourceRegistration> GetRegistrations() =>
            _registrations.Values.ToArray();

        public ResourceRegistration? GetRegistration(string resourceId) =>
            _registrations.GetValueOrDefault(resourceId);

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            _registrations[resourceId] = new ResourceRegistration(
                resourceId,
                providerId,
                resourceGroupId,
                DateTimeOffset.UtcNow,
                NormalizeDependencies(dependsOn ?? []));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            _registrations.Remove(resourceId);
            return Task.CompletedTask;
        }

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            var registration = _registrations[resourceId];
            _registrations[resourceId] = registration with
            {
                ResourceGroupId = resourceGroupId,
                DependsOn = dependsOn is null
                    ? registration.DependsOn
                    : NormalizeDependencies(dependsOn)
            };
            return Task.CompletedTask;
        }

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default)
        {
            var registration = _registrations[resourceId];
            _registrations[resourceId] = registration with
            {
                DependsOn = NormalizeDependencies(dependsOn)
            };
            return Task.CompletedTask;
        }

        private static IReadOnlyList<string> NormalizeDependencies(IReadOnlyList<string> dependsOn) =>
            dependsOn
                .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
                .Select(dependency => dependency.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}
