using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.ContainerHost;

public static class SqlServerResourceBuilderExtensions
{
    private const string DefaultSqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";
    private const string DefaultPassword = "CloudShell-Passw0rd!";

    public static IContainerResourceBuilder AddSqlServer(
        this IResourceDeclarationBuilder resources,
        string name,
        string? password = null,
        IResourceBuilder? dataVolume = null)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var sqlServer = resources
            .AddContainer(name, DefaultSqlServerImage)
            .WithEndpoint("tds", targetPort: 1433, port: 14333, protocol: "tcp")
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
