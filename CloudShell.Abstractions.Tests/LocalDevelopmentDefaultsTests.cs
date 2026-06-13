using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Providers.Docker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CloudShell.Abstractions.Tests;

public sealed class LocalDevelopmentDefaultsTests
{
    [Fact]
    public async Task UseLocalDevelopmentDefaults_RegistersDockerAndSelectsDefaultOrchestrator()
    {
        var services = CreateServices();

        services
            .AddControlPlane()
            .UseLocalDevelopmentDefaults();

        using var serviceProvider = services.BuildServiceProvider();

        await StartHostedServicesAsync(serviceProvider);

        var host = Assert.Single(serviceProvider.GetServices<IContainerHostProvider>())
            .GetDefaultHost();
        var selection = serviceProvider
            .GetRequiredService<IResourceOrchestrationSettings>()
            .Get();

        Assert.Equal("docker", host.Id);
        Assert.Equal(ContainerHostKind.Docker, host.Kind);
        Assert.True(host.IsDefault);
        Assert.Equal("default", selection.OrchestratorId);
        Assert.Equal("docker", selection.PreferredContainerHostId);
    }

    [Fact]
    public async Task UseLocalDevelopmentDefaults_DoesNotOverrideExistingOrchestrationSelection()
    {
        var services = CreateServices();

        services
            .AddControlPlane()
            .UseLocalDevelopmentDefaults();

        using var serviceProvider = services.BuildServiceProvider();
        var settings = serviceProvider.GetRequiredService<IResourceOrchestrationSettings>();
        settings.Select("docker-compose", "podman", healthCheckIntervalSeconds: 42);

        await StartHostedServicesAsync(serviceProvider);

        var selection = settings.Get();

        Assert.Equal("docker-compose", selection.OrchestratorId);
        Assert.Equal("podman", selection.PreferredContainerHostId);
        Assert.Equal(42, selection.HealthCheckIntervalSeconds);
    }

    private static ServiceCollection CreateServices()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services.AddSingleton<IOptionsMonitor<ResourceManagerOptions>>(
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));
        services.AddSingleton<ResourceOrchestratorSelectionStore>();
        services.AddSingleton<IResourceOrchestrationSettings>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceOrchestratorSelectionStore>());
        return services;
    }

    private static async Task StartHostedServicesAsync(IServiceProvider serviceProvider)
    {
        foreach (var hostedService in serviceProvider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
