namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed record SqlServerConfiguration(
    IReadOnlyList<SqlServerDatabaseDefinition> Databases);

public sealed record SqlServerDatabaseDefinition(
    string Name,
    string? DisplayName = null,
    bool EnsureCreated = false);
