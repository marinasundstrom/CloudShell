using System.Collections.Generic;
using System.Linq;

namespace CloudShell.Providers.Applications;

internal static class SqlServerDatabaseRowFactory
{
    public static SqlServerDatabaseRowSet Create(
        ApplicationResourceDefinition application,
        IReadOnlyList<SqlServerDatabaseInfo> liveDatabases,
        bool wasLiveQueryAttempted)
    {
        var rows = new Dictionary<string, SqlServerDatabaseRow>(StringComparer.OrdinalIgnoreCase);
        var liveByName = liveDatabases.ToDictionary(database => database.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var declared in application.SqlDatabases)
        {
            liveByName.TryGetValue(declared.Name, out var live);
            rows[declared.Name] = new SqlServerDatabaseRow(
                declared.Name,
                string.IsNullOrWhiteSpace(declared.DisplayName) ? declared.Name : declared.DisplayName,
                IsDeclared: true,
                ExistsOnServer: live is not null,
                IsSystem: live?.IsSystem ?? false,
                State: live?.State,
                WasLiveQueryAttempted: wasLiveQueryAttempted);
        }

        foreach (var live in liveDatabases)
        {
            if (rows.ContainsKey(live.Name))
            {
                continue;
            }

            rows[live.Name] = new SqlServerDatabaseRow(
                live.Name,
                live.Name,
                IsDeclared: false,
                ExistsOnServer: true,
                live.IsSystem,
                live.State,
                WasLiveQueryAttempted: wasLiveQueryAttempted);
        }

        var orderedRows = rows.Values
            .OrderByDescending(database => database.IsDeclared)
            .ThenBy(database => database.IsSystem)
            .ThenBy(database => database.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingDeclaredDatabases = orderedRows
            .Where(database => database.IsDeclared && database.WasLiveQueryAttempted && !database.ExistsOnServer)
            .ToArray();

        return new SqlServerDatabaseRowSet(
            orderedRows
                .Except(missingDeclaredDatabases)
                .ToArray(),
            missingDeclaredDatabases);
    }
}

internal sealed record SqlServerDatabaseRowSet(
    IReadOnlyList<SqlServerDatabaseRow> Databases,
    IReadOnlyList<SqlServerDatabaseRow> MissingDeclaredDatabases);

internal sealed record SqlServerDatabaseRow(
    string Name,
    string Label,
    bool IsDeclared,
    bool ExistsOnServer,
    bool IsSystem,
    string? State,
    bool WasLiveQueryAttempted);
