using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers.UI.SqlServer;

/// <summary>Matches visible database registrations to their server, without querying SQL.</summary>
public static class SqlServerDatabaseInventory
{
    public static bool BelongsTo(Resource database, string serverResourceId) =>
        BuiltInProviderResourceUiTypes.IsSqlDatabase(database.EffectiveTypeId) &&
        (string.Equals(database.ParentResourceId, serverResourceId, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(database.OwnerResourceId, serverResourceId, StringComparison.OrdinalIgnoreCase) ||
         database.DependsOn.Any(dependency =>
             string.Equals(dependency, serverResourceId, StringComparison.OrdinalIgnoreCase)));
}
