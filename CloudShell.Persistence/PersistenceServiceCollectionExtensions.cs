using CloudShell.Abstractions.ResourceManager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCloudShellPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddPooledDbContextFactory<CloudShellDbContext>(
            options => options.UseSqlite(connectionString));
        services.AddSingleton<SqliteResourceStore>();
        services.AddSingleton<IResourceRegistrationStore>(
            serviceProvider => serviceProvider.GetRequiredService<SqliteResourceStore>());
        services.AddSingleton<IResourceGroupStore>(
            serviceProvider => serviceProvider.GetRequiredService<SqliteResourceStore>());

        return services;
    }

    public static void InitializeCloudShellDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var contextFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<CloudShellDbContext>>();
        using var context = contextFactory.CreateDbContext();
        context.Database.EnsureCreated();
    }
}
