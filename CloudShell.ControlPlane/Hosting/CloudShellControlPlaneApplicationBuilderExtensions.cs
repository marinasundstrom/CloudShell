using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using CloudShell.ControlPlane;
using CloudShell.ControlPlane.Api;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Logs;
using CloudShell.ControlPlane.Observability;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.Shell;
using CloudShell.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CloudShell.ControlPlane.Hosting;

public static class CloudShellControlPlaneApplicationBuilderExtensions
{
    public static IControlPlaneBuilder AddCloudShellControlPlane(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpClient();
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        builder.Services.AddCloudShellControlPlaneOpenApi();
        builder.Services.Configure<ResourceManagerOptions>(
            builder.Configuration.GetSection(ResourceManagerOptions.SectionName));

        ConfigurePersistence(builder);
        builder.Services.AddCloudShellAuthentication(builder.Configuration);

        var controlPlane = builder.Services.AddControlPlane();

        builder.Services.AddScoped<IResourceGroupStore, AuthorizedResourceGroupStore>();
        builder.Services.AddScoped<IResourceRegistrationStore, AuthorizedResourceRegistrationStore>();
        builder.Services.AddScoped<IResourceManagerStore, ResourceManagerStore>();
        builder.Services.AddScoped<ILogStore, LogStore>();
        builder.Services.AddSingleton<InMemoryResourceEventStore>();
        builder.Services.AddSingleton<IResourceEventSink>(
            serviceProvider => serviceProvider.GetRequiredService<InMemoryResourceEventStore>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ILogProvider, ResourceEventLogProvider>());
        builder.Services.AddSingleton<ITraceStore, InMemoryTraceStore>();
        builder.Services.AddScoped<ResourceTemplateService>();
        builder.Services.AddScoped<ResourceOrchestrationService>();
        builder.Services.AddScoped<ResourceDeclarationStartupService>();
        builder.Services.TryAddSingleton(new PlatformResourceOptions());
        builder.Services.TryAddSingleton<IHostLocalNetworkEnvironment, HostLocalNetworkEnvironment>();
        builder.Services.TryAddSingleton<PlatformResourceStore>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOrchestrationDescriptorProvider, PlatformResourceProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProvider, PlatformResourceProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProvider, CloudShellResourceProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProvider, ManagedResourceProvider>());
        builder.Services.AddScoped<IControlPlane, InProcessControlPlane>();
        builder.Services.AddScoped<IResourceManager>(
            serviceProvider => serviceProvider.GetRequiredService<IControlPlane>());
        builder.Services.AddScoped<IResourceTemplateManager>(
            serviceProvider => serviceProvider.GetRequiredService<IControlPlane>());
        builder.Services.AddScoped<ILogManager>(
            serviceProvider => serviceProvider.GetRequiredService<IControlPlane>());
        builder.Services.AddScoped<ITraceManager>(
            serviceProvider => serviceProvider.GetRequiredService<IControlPlane>());
        builder.Services.AddScoped<ControlPlaneUserSettingsProvider>();
        builder.Services.AddScoped<ICloudShellControlPlaneUserSettingsProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ControlPlaneUserSettingsProvider>());
        builder.Services.AddSingleton<ResourceOrchestratorSelectionStore>();
        builder.Services.AddSingleton<IResourceOrchestrationSettings>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceOrchestratorSelectionStore>());
        builder.Services.AddScoped<IResourceOrchestrationCatalog, ResourceOrchestrationCatalog>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IResourceOrchestrator, DefaultResourceOrchestrator>());

        return controlPlane;
    }

    private static void ConfigurePersistence(WebApplicationBuilder builder)
    {
        var persistenceOptions = builder.Configuration
            .GetSection(CloudShellPersistenceOptions.SectionName)
            .Get<CloudShellPersistenceOptions>()
            ?? new CloudShellPersistenceOptions();

        ResolveSqlitePaths(persistenceOptions, builder.Environment.ContentRootPath);
        builder.Services.AddCloudShellPersistence(persistenceOptions);
    }

    private static void ResolveSqlitePaths(
        CloudShellPersistenceOptions options,
        string contentRootPath)
    {
        if (!options.Provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        options.ConnectionString = ResolveSqlitePath(
            options.ConnectionString,
            contentRootPath);
        options.IdentityConnectionString = ResolveSqlitePath(
            options.IdentityConnectionString,
            contentRootPath);
    }

    private static string ResolveSqlitePath(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(builder.DataSource))
        {
            return connectionString;
        }

        var fullPath = Path.GetFullPath(builder.DataSource, contentRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        builder.DataSource = fullPath;
        return builder.ConnectionString;
    }
}
