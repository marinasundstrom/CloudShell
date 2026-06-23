using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class SqlServerDatabaseResourceProjectorTests
{
    [Fact]
    public void CreateResources_ProjectsDeclaredDatabaseAsDiagnosticChildResource()
    {
        var application = new ApplicationResourceDefinition(
            "application:sql-server",
            "sql-server",
            string.Empty,
            containerImage: "mcr.microsoft.com/mssql/server:2022-latest",
            resourceType: ApplicationResourceTypes.SqlServer,
            sqlDatabases:
            [
                new SqlServerDatabaseDefinition(
                    "Vehicle Costs",
                    "Vehicle costs",
                    EnsureCreated: true)
            ]);

        var database = Assert.Single(SqlServerDatabaseResourceProjector.CreateResources(application));

        Assert.Equal("application:sql-server/database:vehicle-costs", database.Id);
        Assert.Equal("Vehicle Costs", database.Name);
        Assert.Equal("Vehicle costs", database.DisplayName);
        Assert.Equal("SQL database", database.Kind);
        Assert.Equal("Applications", database.Provider);
        Assert.Equal(ApplicationResourceTypes.SqlDatabase, database.EffectiveTypeId);
        Assert.Equal(ResourceClass.Service, database.ResourceClass);
        Assert.Null(database.State);
        Assert.Equal(application.Id, database.ParentResourceId);
        Assert.Equal(application.Id, database.OwnerResourceId);
        Assert.Equal(ResourceSource.Provider, database.Source);
        Assert.Equal(ResourceManagementMode.ProviderManaged, database.ManagementMode);
        Assert.Equal(ResourceVisibility.Diagnostic, database.Visibility);
        Assert.Equal(ResourceCleanupBehavior.DeleteWithOwner, database.CleanupBehavior);
        Assert.Equal(application.Id, Assert.Single(database.DependsOn));
        Assert.Equal("Vehicle Costs", database.ResourceAttributes[ResourceAttributeNames.DatabaseName]);
        Assert.Equal(application.Id, database.ResourceAttributes[ResourceAttributeNames.DatabaseServerResourceId]);
        Assert.Equal("declared", database.ResourceAttributes[ResourceAttributeNames.DatabaseSource]);
        Assert.Equal("true", database.ResourceAttributes[ResourceAttributeNames.DatabaseEnsureCreated]);
    }
}
