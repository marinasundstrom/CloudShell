namespace CloudShell.ResourceModel.ReferenceProviders;

public static class SqlServerResourceDefaults
{
    public const string ContainerImage = "mcr.microsoft.com/mssql/server:2022-latest";
    public const string AdministratorPassword = "CloudShell-Passw0rd!";
    public const string DataPath = "/var/opt/mssql";
}
