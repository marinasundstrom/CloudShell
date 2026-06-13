using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data;

namespace CloudShell.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    private const string EfCoreProductVersion = "11.0.0-preview.4.26230.115";
    private const string CloudShellInitialMigrationId = "20260606102257_InitialCreate";
    private const string IdentityInitialMigrationId = "20260606102306_InitialCreate";
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

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
        services.AddSingleton<EfCoreExtensionActivationStore>();
        services.TryAddSingleton<IResourceEventStore, EfCoreResourceEventStore>();
        services.TryAddSingleton<IResourceEventSink>(
            serviceProvider => serviceProvider.GetRequiredService<IResourceEventStore>());
        services.AddSingleton<IResourceRegistrationStore>(
            serviceProvider => serviceProvider.GetRequiredService<EfCoreResourceStore>());
        services.AddSingleton<IResourceGroupStore>(
            serviceProvider => serviceProvider.GetRequiredService<EfCoreResourceStore>());
        services.AddSingleton<ICloudShellExtensionActivationStore>(
            serviceProvider => serviceProvider.GetRequiredService<EfCoreExtensionActivationStore>());

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
        MigrateDatabase(
            context,
            CloudShellInitialMigrationId,
            "ResourceGroups");

        if (!initializeIdentityStore)
        {
            return;
        }

        var identityContext = scope.ServiceProvider
            .GetRequiredService<CloudShellIdentityDbContext>();
        MigrateDatabase(
            identityContext,
            IdentityInitialMigrationId,
            "AspNetUsers");
    }

    private static void MigrateDatabase(
        DbContext context,
        string initialMigrationId,
        string baselineTableName)
    {
        var databaseCreator = context.GetService<IRelationalDatabaseCreator>();
        if (databaseCreator.Exists() &&
            databaseCreator.HasTables() &&
            !TableExists(context, MigrationsHistoryTable) &&
            TableExists(context, baselineTableName))
        {
            BaselineInitialMigration(context, initialMigrationId);
        }

        context.Database.Migrate();
    }

    private static void BaselineInitialMigration(
        DbContext context,
        string initialMigrationId)
    {
        if (context.Database.IsSqlite())
        {
            context.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                """);
            context.Database.ExecuteSql($"""
                INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ({initialMigrationId}, {EfCoreProductVersion});
                """);
            return;
        }

        if (context.Database.IsSqlServer())
        {
            context.Database.ExecuteSqlRaw("""
                IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
                BEGIN
                    CREATE TABLE [__EFMigrationsHistory] (
                        [MigrationId] nvarchar(150) NOT NULL,
                        [ProductVersion] nvarchar(32) NOT NULL,
                        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                    );
                END;
                """);
            context.Database.ExecuteSql($"""
                IF NOT EXISTS (
                    SELECT 1 FROM [__EFMigrationsHistory]
                    WHERE [MigrationId] = {initialMigrationId}
                )
                BEGIN
                    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                    VALUES ({initialMigrationId}, {EfCoreProductVersion});
                END;
                """);
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported persistence provider '{context.Database.ProviderName}'.");
    }

    private static bool TableExists(DbContext context, string tableName)
    {
        var connection = context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = context.Database.IsSqlite()
                ? """
                    SELECT COUNT(*)
                    FROM sqlite_master
                    WHERE type = 'table' AND name = @tableName;
                    """
                : """
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_NAME = @tableName;
                    """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            var result = command.ExecuteScalar();
            return Convert.ToInt32(result) > 0;
        }
        finally
        {
            if (closeConnection)
            {
                connection.Close();
            }
        }
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
