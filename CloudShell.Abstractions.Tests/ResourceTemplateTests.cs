using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceTemplateTests
{
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

        Assert.Equal("applications", template.ProviderId);
        Assert.Equal("application.executable", template.ResourceType);
        Assert.Equal("1.0", template.ProviderConfigurationVersion);
        Assert.Equal("dotnet", template.Configuration.GetProperty("executablePath").GetString());
        Assert.Equal("run", template.Configuration.GetProperty("arguments").GetString());
    }

    [Fact]
    public async Task TemplateService_ExportsAndImportsAResourceGroup()
    {
        using var fixture = new TemplateFixture();
        var service = fixture.CreateTemplateService();

        var export = await service.ExportGroupAsync(fixture.Group.Id);
        var import = await service.ImportGroupAsync(export.Template);

        Assert.Empty(export.Diagnostics);
        Assert.Empty(import.Diagnostics);
        Assert.Equal("Local Development", import.ResourceGroup.Name);
        Assert.Single(import.ImportedResources);
        Assert.Equal(2, fixture.Provider.GetApplications().Count);
        Assert.Contains(
            fixture.Provider.GetApplications(),
            application => application.Id == "application:example-web-api-2");
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
                LogDirectory = "application-logs"
            };
            var environment = new TestHostEnvironment(_contentRoot);
            var store = new ApplicationResourceStore(options, environment);
            var runtimeStates = new ApplicationRuntimeStateStore(options, environment);
            Provider = new ApplicationResourceProvider(store, runtimeStates, options, environment);
            Group = new ResourceGroup("group-1", "Local Development", "Development resources", ["application:example-web-api"]);
            Registrations = new TestRegistrationStore();
            ResourceGroups = new TestResourceGroupStore(Group);
            ResourceManager = new TestResourceManagerStore(Provider, ResourceGroups);

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

        public ApplicationResourceProvider Provider { get; }

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
        ApplicationResourceProvider provider,
        TestResourceGroupStore resourceGroups) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers { get; } = [provider];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => resourceGroups.GetResourceGroups();

        public IReadOnlyList<CloudResource> GetAvailableResources() => [];

        public IReadOnlyList<CloudResource> GetResources() => provider.GetResources();

        public CloudResource? GetResource(string resourceId) =>
            GetResources().FirstOrDefault(resource =>
                string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<CloudResource> GetChildren(string resourceId) => [];

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
            CancellationToken cancellationToken = default)
        {
            _registrations[resourceId] = new ResourceRegistration(
                resourceId,
                providerId,
                resourceGroupId,
                DateTimeOffset.UtcNow);
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
            CancellationToken cancellationToken = default)
        {
            var registration = _registrations[resourceId];
            _registrations[resourceId] = registration with { ResourceGroupId = resourceGroupId };
            return Task.CompletedTask;
        }
    }
}
