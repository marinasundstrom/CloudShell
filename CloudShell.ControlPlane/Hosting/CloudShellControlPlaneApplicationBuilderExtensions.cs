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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        builder.Services.Configure<ResourceIdentityOptions>(
            builder.Configuration.GetSection(ResourceIdentityOptions.SectionName));

        ConfigurePersistence(builder);
        builder.Services.AddCloudShellAuthentication(builder.Configuration);

        var controlPlane = builder.Services.AddControlPlane();
        builder.Services.AddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<ResourceIdentityOptions>>()
                .Value
                .ToCatalog());

        builder.Services.AddScoped<IResourceGroupStore, AuthorizedResourceGroupStore>();
        builder.Services.AddScoped<IResourceRegistrationStore, AuthorizedResourceRegistrationStore>();
        builder.Services.AddScoped<IResourceManagerStore, ResourceManagerStore>();
        builder.Services.AddScoped<ILogStore, LogStore>();
        builder.Services.TryAddSingleton<InMemoryResourceEventStore>();
        builder.Services.TryAddSingleton<IResourceEventStore>(
            serviceProvider => serviceProvider.GetRequiredService<InMemoryResourceEventStore>());
        builder.Services.TryAddSingleton<IResourceEventSink>(
            serviceProvider => serviceProvider.GetRequiredService<InMemoryResourceEventStore>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ILogProvider, ResourceEventLogProvider>());
        builder.Services.AddSingleton<ITraceStore, InMemoryTraceStore>();
        builder.Services.AddScoped<ResourceTemplateService>();
        builder.Services.AddScoped<IContainerHostResolver, ContainerHostResolver>();
        builder.Services.AddScoped<ResourceOrchestrationService>();
        builder.Services.AddScoped<ResourceDeclarationStartupService>();
        builder.Services.AddScoped<ResourceIdentityProvisioningService>();
        builder.Services.AddScoped<ResourceIdentityProviderSetupService>();
        builder.Services.TryAddSingleton<BuiltInResourceIdentityRegistry>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceIdentityProvisioner, BuiltInResourceIdentityProvisioner>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceIdentityProvisioningStatusProvider, BuiltInResourceIdentityProvisioner>());
        builder.Services.TryAddSingleton(new PlatformResourceOptions());
        builder.Services.TryAddSingleton<IHostLocalNetworkEnvironment, HostLocalNetworkEnvironment>();
        builder.Services.TryAddSingleton<
            ILocalHostNameResolverCacheRefreshCommandRunner,
            ProcessLocalHostNameResolverCacheRefreshCommandRunner>();
        builder.Services.TryAddSingleton<ILocalHostNameResolverCacheRefresher, LocalHostNameResolverCacheRefresher>();
        builder.Services.TryAddSingleton<PlatformResourceStore>();
        builder.Services.TryAddSingleton<DnsNamePublishingObservationStore>();
        builder.Services.TryAddSingleton<LocalHostNamePublishingProvider>();
        builder.Services.TryAddSingleton<LocalHostNetworkProvisioner>();
        builder.Services.TryAddSingleton<LocalHostNetworkProvider>();
        builder.Services.TryAddSingleton<PlatformResourceProvider>();
        builder.Services.AddSingleton<INamePublishingProvider>(
            serviceProvider => serviceProvider.GetRequiredService<LocalHostNamePublishingProvider>());
        builder.Services.AddSingleton<IResourceEndpointMappingProvisioner>(
            serviceProvider => serviceProvider.GetRequiredService<LocalHostNetworkProvisioner>());
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<PlatformResourceProvider>());
        builder.Services.AddSingleton<IResourceActionAvailabilityProvider>(
            serviceProvider => serviceProvider.GetRequiredService<PlatformResourceProvider>());
        builder.Services.AddSingleton<IResourceProvider>(
            serviceProvider => serviceProvider.GetRequiredService<PlatformResourceProvider>());
        builder.Services.AddSingleton<IResourceProvider>(
            serviceProvider => serviceProvider.GetRequiredService<LocalHostNetworkProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IResourceProvider, ResourceIdentityProvisioningResourceProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProvider, MacOSHostNetworkProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProvider, CloudShellResourceProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProvider, ManagedResourceProvider>());
        builder.Services.AddScoped<IControlPlane, InProcessControlPlane>();
        builder.Services.AddScoped<IResourceManager>(
            serviceProvider => serviceProvider.GetRequiredService<IControlPlane>());
        builder.Services.AddScoped<IResourceTemplateManager>(
            serviceProvider => serviceProvider.GetRequiredService<IControlPlane>());
        builder.Services.AddScoped<IResourceEventManager>(
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
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, HostScopedResourceShutdownService>());

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
