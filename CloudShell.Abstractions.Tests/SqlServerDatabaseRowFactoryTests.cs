using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class SqlServerDatabaseRowFactoryTests
{
    [Fact]
    public void Create_KeepsDeclaredDatabasesInMainListUntilLiveQueryRuns()
    {
        var application = CreateSqlServerApplication(
        [
            new SqlServerDatabaseDefinition("appdb", "Application DB")
        ]);

        var rows = SqlServerDatabaseRowFactory.Create(
            application,
            liveDatabases: [],
            wasLiveQueryAttempted: false);

        var database = Assert.Single(rows.Databases);
        Assert.Equal("appdb", database.Name);
        Assert.Equal("Application DB", database.Label);
        Assert.True(database.IsDeclared);
        Assert.False(database.ExistsOnServer);
        Assert.Empty(rows.MissingDeclaredDatabases);
    }

    [Fact]
    public void Create_SeparatesOnlyMissingDeclaredDatabasesAfterLiveQuery()
    {
        var application = CreateSqlServerApplication(
        [
            new SqlServerDatabaseDefinition("appdb", "Application DB"),
            new SqlServerDatabaseDefinition("missingdb", "Missing DB")
        ]);

        var rows = SqlServerDatabaseRowFactory.Create(
            application,
            liveDatabases:
            [
                new SqlServerDatabaseInfo("master", "ONLINE", IsSystem: true),
                new SqlServerDatabaseInfo("appdb", "ONLINE", IsSystem: false),
                new SqlServerDatabaseInfo("runtime_only", "ONLINE", IsSystem: false)
            ],
            wasLiveQueryAttempted: true);

        Assert.Collection(
            rows.Databases,
            database =>
            {
                Assert.Equal("appdb", database.Name);
                Assert.Equal("Application DB", database.Label);
                Assert.True(database.IsDeclared);
                Assert.True(database.ExistsOnServer);
            },
            database =>
            {
                Assert.Equal("runtime_only", database.Name);
                Assert.False(database.IsDeclared);
                Assert.True(database.ExistsOnServer);
            },
            database =>
            {
                Assert.Equal("master", database.Name);
                Assert.True(database.IsSystem);
                Assert.True(database.ExistsOnServer);
            });

        var missing = Assert.Single(rows.MissingDeclaredDatabases);
        Assert.Equal("missingdb", missing.Name);
        Assert.Equal("Missing DB", missing.Label);
        Assert.True(missing.IsDeclared);
        Assert.False(missing.ExistsOnServer);
        Assert.True(missing.WasLiveQueryAttempted);
    }

    private static ApplicationResourceDefinition CreateSqlServerApplication(
        IReadOnlyList<SqlServerDatabaseDefinition> databases) =>
        new(
            "application:sql",
            "SQL Server",
            executablePath: string.Empty,
            resourceType: ApplicationResourceTypes.SqlServer,
            sqlDatabases: databases);
}
