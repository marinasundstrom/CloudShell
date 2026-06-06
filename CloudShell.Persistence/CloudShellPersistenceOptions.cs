namespace CloudShell.Persistence;

public sealed class CloudShellPersistenceOptions
{
    public const string SectionName = "Persistence";

    public string Provider { get; set; } = "Sqlite";

    public string ConnectionString { get; set; } = "Data Source=Data/cloudshell.db";

    public string IdentityConnectionString { get; set; } = "Data Source=Data/identity.db";
}
