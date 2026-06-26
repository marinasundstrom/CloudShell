namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SqlDatabaseResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<SqlDatabaseResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        SqlDatabaseResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        SqlDatabaseResourceTypeProvider.ProviderId;

    public SqlDatabaseResourceDefinitionBuilder WithDatabaseName(string databaseName) =>
        SetScalarAttribute(SqlDatabaseResourceTypeProvider.Attributes.DatabaseName, databaseName);

    public SqlDatabaseResourceDefinitionBuilder BelongsToServer(
        IResourceDefinitionBuilder server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return BelongsToServer(server.EffectiveResourceId);
    }

    public SqlDatabaseResourceDefinitionBuilder BelongsToServer(string serverResourceId) =>
        AddDependency(ResourceReference.DependsOnResourceId(
            serverResourceId,
            typeId: SqlServerResourceTypeProvider.ResourceTypeId));

    public SqlDatabaseResourceDefinitionBuilder EnsureCreated(bool ensureCreated = true) =>
        SetScalarAttribute(SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated, ensureCreated);
}

public static class SqlDatabaseResourceDefinitionBuilderExtensions
{
    public static SqlDatabaseResourceDefinitionBuilder AddSqlDatabase(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new SqlDatabaseResourceDefinitionBuilder(name)
            .WithDatabaseName(name);
        graph.Add(builder);
        return builder;
    }
}
