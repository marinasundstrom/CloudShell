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

The Identity database is only initialized when
`Authentication:Mode` is `Identity`.

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

Use separate empty databases/catalogs for resources and Identity. The current
startup initialization uses EF Core `EnsureCreated`, which doesn't add a
second context's tables to a database that already contains tables. A future
production migration workflow can replace `EnsureCreated` without changing
the store interfaces.
