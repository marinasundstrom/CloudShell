using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;

namespace CloudShell.ContainerHost;

public static class ContainerHostSampleResources
{
    public static void AddResources(IResourceDeclarationBuilder resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var localStorage = resources
            .AddLocalStorage("local", "Local Storage")
            .UseLocation("./Data/storage");

        var sqlData = resources
            .AddVolume("sql-data", "SQL Server Data")
            .UseStorage(localStorage, "sql-server")
            .WithAccessMode(VolumeAccessMode.ReadWriteOnce);

        resources
            .AddSqlServer("sql-server", dataVolume: sqlData)
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest");
    }
}
