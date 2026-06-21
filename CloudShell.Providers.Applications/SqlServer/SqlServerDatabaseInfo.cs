namespace CloudShell.Providers.Applications;

public sealed record SqlServerDatabaseInfo(
    string Name,
    string State,
    bool IsSystem);
