using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.ApplicationTopology;

public static class SqlServerResourceBuilderExtensions
{
    public const string DefaultSqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";
    public const string DefaultPassword = "CloudShell-Passw0rd!";

    public static IContainerResourceBuilder AddSqlServer(
        this IResourceDeclarationBuilder resources,
        string name,
        string? password = null,
        IResourceBuilder? dataVolume = null,
        int port = 14334)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var sqlServer = resources
            .AddContainer(name, DefaultSqlServerImage)
            .WithEndpoint("tds", targetPort: 1433, port: port, protocol: "tcp")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", string.IsNullOrWhiteSpace(password)
                ? DefaultPassword
                : password);

        if (dataVolume is not null)
        {
            sqlServer.WithVolume(dataVolume, "/var/opt/mssql", name: "data");
        }

        return sqlServer;
    }
}
