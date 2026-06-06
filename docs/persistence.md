# Persistence configuration

CloudShell persistence is configured under `Persistence` in
`CloudShell.Host/appsettings.json`. Resource data and local ASP.NET Core
Identity data use separate connection strings.

## SQLite

SQLite is the default and resolves relative data-source paths from the
`CloudShell.Host` content root.

```json
{
  "Persistence": {
    "Provider": "Sqlite",
    "ConnectionString": "Data Source=Data/cloudshell.db",
    "IdentityConnectionString": "Data Source=Data/identity.db"
  }
}
```

The application applies EF Core migrations at startup. Existing databases
created by the previous `EnsureCreated` startup path are baselined to the
initial migration when their expected tables already exist. The Identity
database is only migrated when `Authentication:Mode` is `Identity`.

## SQL Server

Use `SqlServer` (or `Mssql`) and ordinary EF Core SQL Server connection
strings:

```json
{
  "Persistence": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=localhost;Database=CloudShell;User Id=cloudshell;Password=...;TrustServerCertificate=True",
    "IdentityConnectionString": "Server=localhost;Database=CloudShellIdentity;User Id=cloudshell;Password=...;TrustServerCertificate=True"
  }
}
```

Keep secrets out of committed settings. Environment-variable overrides are:

```bash
Persistence__Provider=SqlServer
Persistence__ConnectionString="..."
Persistence__IdentityConnectionString="..."
```

Use separate empty databases/catalogs for resources and Identity. To add a
future migration, use the local EF Core tool from the repository root:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project CloudShell.Persistence/CloudShell.Persistence.csproj --startup-project CloudShell.Host/CloudShell.Host.csproj --context CloudShellDbContext --output-dir Migrations/CloudShell
dotnet ef migrations add <Name> --project CloudShell.Persistence/CloudShell.Persistence.csproj --startup-project CloudShell.Host/CloudShell.Host.csproj --context CloudShellIdentityDbContext --output-dir Migrations/Identity
```
