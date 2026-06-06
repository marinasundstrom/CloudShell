# Persistence configuration

CloudShell persistence is configured under `Persistence` in
`CloudShell.Host/appsettings.json`. Resource data and local ASP.NET Core
Identity data use separate connection strings.

Resource persistence stores platform metadata only: resource registrations,
resource groups, and resource-to-group assignments. Provider-specific resource
configuration is not stored in a common database column. Providers remain the
authority for their own configuration stores.

For example, configuration service resources are registered and grouped in the
core database, but their key-value entries and access tokens are stored by the
configuration provider in `CloudShell.Host/Data/configuration-stores.json`.

## Programmatic Declarations

Resources declared with `Resources` in the Control Plane host are
startup configuration by default. They appear in Resource Manager without
writing provider-owned configuration or core registration rows, so the
checked-in code remains the source of truth.

Calling `Persist()` on a declaration asks the owning provider to apply the
resource through the same setup logic used by the UI. Existing persisted state is
left unchanged unless the declaration uses `Persist(overwrite: true)`.

See [Programmatic resources](programmatic-resources.md).

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

## Resource Templates

Resource templates provide import/export without changing the database ownership
model. CloudShell exports a resource group envelope and asks each provider that
implements `IResourceTemplateProvider` for a provider-owned configuration
payload. Import creates a new resource group and delegates each resource entry
back to the owning provider.

See [Resource templates](resource-templates.md).
