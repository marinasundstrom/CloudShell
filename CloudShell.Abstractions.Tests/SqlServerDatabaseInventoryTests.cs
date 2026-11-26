using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI.SqlServer;

namespace CloudShell.Abstractions.Tests;

public sealed class SqlServerDatabaseInventoryTests
{
    [Theory]
    [InlineData("parent")]
    [InlineData("owner")]
    [InlineData("dependency")]
    public void RecognizesServerRelationshipsCaseInsensitively(string relationship)
    {
        var database = Database() with
        {
            ParentResourceId = relationship == "parent" ? "SERVER" : null,
            OwnerResourceId = relationship == "owner" ? "SERVER" : null,
            DependsOn = relationship == "dependency" ? ["SERVER"] : []
        };
        Assert.True(SqlServerDatabaseInventory.BelongsTo(database, "server"));
        Assert.False(SqlServerDatabaseInventory.BelongsTo(database, "other"));
        Assert.False(SqlServerDatabaseInventory.BelongsTo(database with { TypeId = "application.rabbitmq" }, "server"));
    }

    private static Resource Database() => new(
        "db", "Database", "database", "test", "local", ResourceState.Running,
        [], "1", DateTimeOffset.UtcNow, [], TypeId: SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString());
}
