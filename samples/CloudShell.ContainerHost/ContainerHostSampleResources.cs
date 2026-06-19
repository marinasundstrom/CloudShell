using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.ContainerHost;

public static class ContainerHostSampleResources
{
    public static void AddResources(IResourceDeclarationBuilder resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var localStorage = resources
            .AddLocalStorage("local")
            .UseLocation("./Data/storage");

        var sqlData = resources
            .AddVolume("sql-data")
            .WithDisplayName("SQL Server Data")
            .UseStorage(localStorage, "sql-server")
            .WithAccessMode(VolumeAccessMode.ReadWriteOnce);

        resources
            .AddSqlServer("sql-server", dataVolume: sqlData);
    }
}
