using CloudShell.Abstractions.ResourceManager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCloudShellPersistence(
        this IServiceCollection services,
        CloudShellPersistenceOptions persistenceOptions)
    {
        services.AddPooledDbContextFactory<CloudShellDbContext>(
            options => ConfigureProvider(
                options,
                persistenceOptions.Provider,
                persistenceOptions.ConnectionString));
        services.AddDbContext<CloudShellIdentityDbContext>(
            options => ConfigureProvider(
                options,
                persistenceOptions.Provider,
                persistenceOptions.IdentityConnectionString));
        services.AddSingleton<EfCoreResourceStore>();
        services.AddSingleton<IResourceRegistrationStore>(
            serviceProvider => serviceProvider.GetRequiredService<EfCoreResourceStore>());
        services.AddSingleton<IResourceGroupStore>(
            serviceProvider => serviceProvider.GetRequiredService<EfCoreResourceStore>());

        return services;
    }

    public static void InitializeCloudShellDatabase(
        this IServiceProvider services,
        bool initializeIdentityStore)
    {
        using var scope = services.CreateScope();
        var contextFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<CloudShellDbContext>>();
        using var context = contextFactory.CreateDbContext();
        context.Database.EnsureCreated();

        if (!initializeIdentityStore)
        {
            return;
        }

        var identityContext = scope.ServiceProvider
            .GetRequiredService<CloudShellIdentityDbContext>();
        identityContext.Database.EnsureCreated();
    }

    private static void ConfigureProvider(
        DbContextOptionsBuilder options,
        string provider,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlite(connectionString);
            return;
        }

        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("Mssql", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlServer(connectionString);
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported persistence provider '{provider}'. Use 'Sqlite' or 'SqlServer'.");
    }
}
