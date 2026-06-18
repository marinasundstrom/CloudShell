using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.Tests;

public sealed class PersistenceDatabaseInitializationTests
{
    [Fact]
    public void ApplyPersistedProgrammaticResourceDeclarations_SkipsTransientDeclarations()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-persistence-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var declarations = new ResourceDeclarationStore();
            var builder = new TestCloudShellBuilder();
            declarations.Declare(builder, "test", "transient");
            declarations.Declare(builder, "test", "persisted").Persist();

            var services = new ServiceCollection();
            services.AddCloudShellPersistence(new CloudShellPersistenceOptions
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={Path.Combine(directory, "cloudshell.db")}",
                IdentityConnectionString = $"Data Source={Path.Combine(directory, "identity.db")}"
            });
            services.AddSingleton(declarations);
            services.AddSingleton<IResourceProvider>(new RecordingProgrammaticDeclarationProvider());

            using var provider = services.BuildServiceProvider();
            provider.InitializeCloudShellDatabase(initializeIdentityStore: false);

            provider.ApplyPersistedProgrammaticResourceDeclarations();

            var registrations = provider.GetRequiredService<EfCoreResourceStore>();
            Assert.Null(registrations.GetRegistration("transient"));
            var persisted = registrations.GetRegistration("persisted");
            Assert.NotNull(persisted);
            Assert.Equal("test", persisted.ProviderId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EfCoreResourceStore_RoundTripsRegistrationIdentityBinding()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-persistence-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var services = new ServiceCollection();
            services.AddCloudShellPersistence(new CloudShellPersistenceOptions
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={Path.Combine(directory, "cloudshell.db")}",
                IdentityConnectionString = $"Data Source={Path.Combine(directory, "identity.db")}"
            });

            using var provider = services.BuildServiceProvider();
            provider.InitializeCloudShellDatabase(initializeIdentityStore: false);

            var registrations = provider.GetRequiredService<EfCoreResourceStore>();
            await registrations.RegisterAsync("applications", "application:test-api");

            var identity = ResourceIdentityBinding.RequireIdentity(
                ["configuration.read"],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["resource"] = "application:test-api"
                });

            await registrations.SetIdentityAsync("application:test-api", identity);

            var registration = registrations.GetRegistration("application:test-api");
            Assert.NotNull(registration?.IdentityBinding);
            Assert.Equal(ResourceIdentityBindingKind.Required, registration.IdentityBinding.Kind);
            Assert.Equal(["configuration.read"], registration.IdentityBinding.IdentityScopes);
            Assert.Equal("application:test-api", registration.IdentityBinding.IdentityClaims["resource"]);

            await registrations.SetIdentityAsync("application:test-api", null);

            registration = registrations.GetRegistration("application:test-api");
            Assert.Null(registration?.IdentityBinding);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeCloudShellDatabase_RepairsMissingRuntimeTablesInExistingDatabase()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-persistence-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "cloudshell.db");
        try
        {
            await CreateDatabaseWithAppliedMigrationHistoryButMissingRuntimeTablesAsync(databasePath);

            var services = new ServiceCollection();
            services.AddCloudShellPersistence(new CloudShellPersistenceOptions
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={databasePath}",
                IdentityConnectionString = $"Data Source={Path.Combine(directory, "identity.db")}"
            });

            using var provider = services.BuildServiceProvider();
            provider.InitializeCloudShellDatabase(initializeIdentityStore: false);

            Assert.True(await TableExistsAsync(databasePath, "ExtensionActivations"));
            Assert.True(await TableExistsAsync(databasePath, "ResourceEvents"));

            var activationStore = provider.GetRequiredService<ICloudShellExtensionActivationStore>();
            Assert.Empty(activationStore.GetActivationStates());

            await activationStore.SetActivationStateAsync(
                "cloudshell.test",
                CloudShellExtensionActivationState.Disabled);
            Assert.Equal(
                CloudShellExtensionActivationState.Disabled,
                activationStore.GetActivationState("cloudshell.test"));

            var eventStore = provider.GetRequiredService<IResourceEventStore>();
            eventStore.Append(new ResourceEvent(
                "application:test",
                ResourceEventTypes.Events.Lifecycle.Started,
                "Resource started.",
                DateTimeOffset.UtcNow,
                "system",
                "Information",
                TraceId: "4bf92f3577b34da6a3ce929d0e0e4736",
                SpanId: "00f067aa0ba902b7"));

            var events = eventStore.GetEvents(new ResourceEventQuery
            {
                ResourceId = "application:test",
                TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
                SpanId = "00f067aa0ba902b7"
            });
            var resourceEvent = Assert.Single(events);
            Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", resourceEvent.TraceId);
            Assert.Equal("00f067aa0ba902b7", resourceEvent.SpanId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CreateDatabaseWithAppliedMigrationHistoryButMissingRuntimeTablesAsync(
        string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
            CREATE TABLE "ResourceGroups" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ResourceGroups" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Description" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            """);
        await ExecuteAsync(connection, """
            CREATE TABLE "ResourceRegistrations" (
                "ResourceId" TEXT NOT NULL CONSTRAINT "PK_ResourceRegistrations" PRIMARY KEY,
                "ProviderId" TEXT NOT NULL,
                "ResourceGroupId" TEXT NULL,
                "RegisteredAt" INTEGER NOT NULL,
                "DependsOnJson" TEXT NOT NULL DEFAULT '[]'
            );
            """);
        await ExecuteAsync(connection, """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """);
        await ExecuteAsync(connection, """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES
                ('20260606102257_InitialCreate', '11.0.0-preview.4.26230.115'),
                ('20260606113000_AddResourceRegistrationDependencies', '11.0.0-preview.4.26230.115'),
                ('20260607091446_AddExtensionActivations', '11.0.0-preview.4.26230.115'),
                ('20260613162026_AddResourceEvents', '11.0.0-preview.4.26230.115');
            """);
    }

    private static async Task<bool> TableExistsAsync(string databasePath, string tableName)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = $tableName;
            """;
        command.Parameters.AddWithValue("$tableName", tableName);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RecordingProgrammaticDeclarationProvider : IResourceProvider, IProgrammaticResourceDeclarationProvider
    {
        public string Id => "test";

        public string DisplayName => "Test Provider";

        public IReadOnlyList<Resource> GetResources() => [];

        public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
            string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

        public Task ApplyDeclarationAsync(
            ResourceDeclaration declaration,
            IResourceRegistrationStore registrations,
            CancellationToken cancellationToken = default) =>
            registrations.RegisterAsync(
                declaration.ProviderId,
                declaration.ResourceId,
                declaration.ResourceGroupId,
                declaration.DependsOn,
                cancellationToken);
    }

    private sealed class TestCloudShellBuilder : ICloudShellBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
    }
}
