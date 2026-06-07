using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.ContainerHost;

public static class SqlServerResourceBuilderExtensions
{
    private const string DefaultSqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";
    private const string DefaultPassword = "CloudShell-Passw0rd!";

    public static IContainerResourceBuilder AddSqlServer(
        this ICloudShellResourceDeclarationBuilder resources,
        string name,
        string? password = null)
    {
        ArgumentNullException.ThrowIfNull(resources);

        return resources
            .AddContainer(name, DefaultSqlServerImage)
            .WithEndpoint("tds", targetPort: 1433, port: 14333, protocol: "tcp")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", string.IsNullOrWhiteSpace(password)
                ? DefaultPassword
                : password);
    }
}
