using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Usage;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.Tests;

public sealed class PersistenceDatabaseInitializationTests
{
    [Fact]
    public void AddCloudShellPersistence_RejectsSharedResourceManagerAndIdentityDatabase()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCloudShellPersistence(new CloudShellPersistenceOptions
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=Data/cloudshell.db"
            },
            new BuiltInIdentityPersistenceOptions
            {
                Provider = "Sqlite",
                ConnectionString = "Data Source=Data/cloudshell.db"
            }));

        Assert.Contains("separate databases", exception.Message);
    }

    [Fact]
    public void CloudShellDataDirectory_ResolvesRelativeDirectoryFromContentRoot()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-persistence-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [CloudShellDataDirectory.ConfigurationKey] = ".cloudshell/data"
                })
                .Build();
            var environment = new TestHostEnvironment(directory);

            var root = CloudShellDataDirectory.ResolveRoot(configuration, environment);

            Assert.Equal(Path.Combine(directory, ".cloudshell", "data"), root);
            Assert.True(Directory.Exists(root));
            Assert.Equal(
                Path.Combine(directory, ".cloudshell", "data", "Data", "cloudshell.db"),
                CloudShellDataDirectory.ResolvePath("Data/cloudshell.db", configuration, environment));
        }
        finally
        {
            DeleteDatabaseDirectory(directory);
        }
    }

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
                ConnectionString = $"Data Source={Path.Combine(directory, "cloudshell.db")}"
            },
            new BuiltInIdentityPersistenceOptions
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={Path.Combine(directory, "identity.db")}"
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
            DeleteDatabaseDirectory(directory);
        }
    }

    [Fact]
    public void EfCoreTelemetryStores_RetainPersistedTraceAndMetricHistory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-persistence-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var resourceStore = Path.Combine(directory, "cloudshell.db");
        var identityStore = Path.Combine(directory, "identity.db");
        try
        {
            using (var provider = BuildTelemetryPersistenceProvider(resourceStore, identityStore))
            {
                provider.InitializeCloudShellDatabase(initializeIdentityStore: false);
                var traces = provider.GetRequiredService<EfCoreTelemetryTraceStore>();
                var metrics = provider.GetRequiredService<EfCoreTelemetryMetricStore>();

                traces.AddSpans(
                [
                    CreateSpan("trace-1", "span-1", DateTimeOffset.UtcNow.AddSeconds(-30)),
                    CreateSpan("trace-2", "span-2", DateTimeOffset.UtcNow.AddSeconds(-20)),
                    CreateSpan("trace-3", "span-3", DateTimeOffset.UtcNow.AddSeconds(-10))
                ]);
                metrics.AddPoints(
                [
                    CreateMetricPoint("http.server.requests", 1, DateTimeOffset.UtcNow.AddSeconds(-30)),
                    CreateMetricPoint("http.server.requests", 2, DateTimeOffset.UtcNow.AddSeconds(-20)),
                    CreateMetricPoint("http.server.requests", 3, DateTimeOffset.UtcNow.AddSeconds(-10))
                ]);
            }

            using (var provider = BuildTelemetryPersistenceProvider(resourceStore, identityStore))
            {
                provider.InitializeCloudShellDatabase(initializeIdentityStore: false);
                var traces = provider.GetRequiredService<EfCoreTelemetryTraceStore>();
                var metrics = provider.GetRequiredService<EfCoreTelemetryMetricStore>();

                var spans = traces.GetSpans("application:test-api", maxSpans: 10);
                var points = metrics.GetPoints("application:test-api", maxPoints: 10);

                Assert.Equal(["trace-3", "trace-2"], spans.Select(span => span.TraceId));
                Assert.Equal([3, 2], points.Select(point => point.Value));
            }
        }
        finally
        {
            DeleteDatabaseDirectory(directory);
        }
    }

    [Fact]
    public void EfCoreUsageStore_RetainsPersistedUsageSamplesAndStatistics()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-persistence-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var resourceStore = Path.Combine(directory, "cloudshell.db");
        var identityStore = Path.Combine(directory, "identity.db");
        try
        {
            using (var provider = BuildUsagePersistenceProvider(resourceStore, identityStore))
            {
                provider.InitializeCloudShellDatabase(initializeIdentityStore: false);
                var usage = provider.GetRequiredService<EfCoreUsageStore>();

                usage.AddSamples(
                [
                    CreateUsageSample("cpu.percent", 1, DateTimeOffset.UtcNow.AddSeconds(-30)),
                    CreateUsageSample("cpu.percent", 2, DateTimeOffset.UtcNow.AddSeconds(-20)),
                    CreateUsageSample("cpu.percent", 3, DateTimeOffset.UtcNow.AddSeconds(-10))
                ]);
            }

            using (var provider = BuildUsagePersistenceProvider(resourceStore, identityStore))
            {
                provider.InitializeCloudShellDatabase(initializeIdentityStore: false);
                var usage = provider.GetRequiredService<EfCoreUsageStore>();

                var samples = usage.GetSamples("application:test-api", "cpu.percent", maxSamples: 10);
                Assert.Equal([3, 2], samples.Select(sample => sample.Value));
                Assert.Equal("monitoring", samples[0].UsageAttributes[UsageAttributeNames.Source]);

                var statistic = Assert.Single(usage.GetStatistics(
                    "application:test-api",
                    "cpu.percent",
                    maxStatistics: 10));
                Assert.Equal(2, statistic.Count);
                Assert.Equal(5, statistic.Sum);
                Assert.Equal(2.5, statistic.Average);
                Assert.Equal(2, statistic.Min);
                Assert.Equal(3, statistic.Max);
                Assert.Equal(3, statistic.LatestValue);
            }
        }
        finally
        {
            DeleteDatabaseDirectory(directory);
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
                ConnectionString = $"Data Source={Path.Combine(directory, "cloudshell.db")}"
            },
            new BuiltInIdentityPersistenceOptions
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={Path.Combine(directory, "identity.db")}"
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
            DeleteDatabaseDirectory(directory);
        }
    }

    private static ServiceProvider BuildTelemetryPersistenceProvider(
        string resourceStore,
        string identityStore)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<TelemetryOptions>(options =>
        {
            options.Store = TelemetryStores.Database;
            options.RetainedSpansPerResource = 2;
            options.RetainedMetricPointsPerResource = 2;
        });
        services.AddCloudShellPersistence(new CloudShellPersistenceOptions
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={resourceStore}"
        },
        new BuiltInIdentityPersistenceOptions
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={identityStore}"
        });

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildUsagePersistenceProvider(
        string resourceStore,
        string identityStore)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<UsageOptions>(options =>
        {
            options.Store = UsageStores.Database;
            options.RetainedSamplesPerResource = 2;
        });
        services.AddCloudShellPersistence(new CloudShellPersistenceOptions
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={resourceStore}"
        },
        new BuiltInIdentityPersistenceOptions
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={identityStore}"
        });

        return services.BuildServiceProvider();
    }

    private static void DeleteDatabaseDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();

        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50));
                SqliteConnection.ClearAllPools();
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50));
                SqliteConnection.ClearAllPools();
            }
        }
    }

    private static TraceSpan CreateSpan(
        string traceId,
        string spanId,
        DateTimeOffset startTime) =>
        new(
            traceId,
            spanId,
            ParentSpanId: null,
            "GET /alive",
            "application:test-api",
            "test-api",
            "server",
            "ok",
            startTime,
            TimeSpan.FromMilliseconds(10),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["http.route"] = "GET /alive"
            });

    private static MetricPoint CreateMetricPoint(
        string name,
        double value,
        DateTimeOffset timestamp) =>
        new(
            name,
            "application:test-api",
            "test-api",
            value,
            timestamp,
            "count",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["http.route"] = "GET /alive"
            });

    private static UsageSample CreateUsageSample(
        string name,
        double value,
        DateTimeOffset timestamp) =>
        new(
            name,
            "application:test-api",
            value,
            timestamp,
            "%",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [UsageAttributeNames.Source] = UsageAttributeNames.SourceMonitoring
            });

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
                ConnectionString = $"Data Source={databasePath}"
            },
            new BuiltInIdentityPersistenceOptions
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source={Path.Combine(directory, "identity.db")}"
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
                ResourceSignalSeverity.Info,
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
            DeleteDatabaseDirectory(directory);
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

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ControlPlane.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
