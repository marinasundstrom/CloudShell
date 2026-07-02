# Persistence configuration

Resource Manager persistence is configured under `Persistence` in the consuming
host's configuration. The development sample uses
`CloudShell.Host/appsettings.json`. Built-in identity provider persistence is
configured separately under `Identity:BuiltIn:Persistence`.

Resource persistence stores platform metadata only: resource registrations,
resource groups, and resource-to-group assignments. Provider-specific resource
configuration is not stored in a common database column. Providers remain the
authority for their own configuration stores.

Built-in identity provider persistence is not Resource Manager state. It stores
local users, password hashes, roles, claims, tokens, and other
ASP.NET Core Identity records in a separate database. External identity
providers already enforce that boundary by owning their own stores; the
built-in provider keeps the same boundary even when it runs in the CloudShell
process.

For example, configuration service resources are registered and grouped in the
core database, but their key-value entries and access tokens are stored by the
configuration provider. The development sample stores them in
`CloudShell.Host/Data/configuration-stores.json`.

## Programmatic Declarations

Resources declared with `Resources` in the Control Plane host are
startup configuration by default. They appear in Resource Manager without
writing provider-owned configuration or core registration rows, so the
checked-in code remains the source of truth.

CloudShell does not serialize programmatic resource definitions into the
database. Transient declarations are in-memory startup state only. The
Resource Manager projects them into the resource graph for the current process,
but the persisted resource registration store is unchanged unless the
declaration explicitly calls `Persist()`.

Calling `Persist()` on a declaration asks the owning provider to apply the
resource through the same setup logic used by the UI. Existing persisted state is
left unchanged unless the declaration uses `Persist(overwrite: true)`.

That call is a persistence boundary, not a deployment operation. Before
persistence, programmatic declarations describe a local development graph owned
by the host startup code. After persistence, the Control Plane's persisted
resource state and each provider's configuration store become the environment
record. Developers can still run the distributed app locally, but changes should
be treated as updates to promote into Control Plane state.

Deploying the persisted graph remains a separate orchestrator concern.
An on-premise CloudShell environment is a deployment target: a standalone
CloudShell cloud environment, potentially for shared hosting, similar in role
to future targets such as Azure or AWS. The intended deployment path is the
orchestrator deployment API once it is ready; `Persist()` should not grow target
deployment semantics. Whether deployment is initiated by CLI, UI, or automation
can be decided later.

See [Programmatic resources](programmatic-resources.md).

## SQLite

SQLite is the default. Relative data-source paths resolve from
`CloudShell:DataDirectory` when that setting is configured, otherwise from the
consuming host's content root.

```json
{
  "Persistence": {
    "Provider": "Sqlite",
    "ConnectionString": "Data Source=Data/cloudshell.db"
  },
  "Identity": {
    "BuiltIn": {
      "Persistence": {
        "Provider": "Sqlite",
        "ConnectionString": "Data Source=Data/identity.db"
      }
    }
  }
}
```

Launcher-style local development flows can keep host data next to the launcher
project by passing a data directory to the CLI or by setting
`CloudShell:DataDirectory` directly:

```bash
dotnet run --project CloudShell.Cli -- template apply ./cloudshell.template.yaml \
  --start \
  --host-project CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj \
  --data-dir samples/CSharpAppHost/.cloudshell
```

The data directory is a CloudShell host setting. It affects CloudShell-owned
local databases and file stores that use relative paths. Absolute paths and
provider-owned workload paths remain explicit and are not moved.

The application applies EF Core migrations at startup. Existing databases
created by the previous `EnsureCreated` startup path are baselined to the
initial migration when their expected tables already exist. The Identity
database is only migrated when `Authentication:Mode` is `Identity`.

## Telemetry

Application/runtime telemetry traces and metric points are retained in memory
by default. Hosts can opt into database-backed telemetry history through
appsettings:

```json
{
  "Observability": {
    "Telemetry": {
      "Store": "Database",
      "RetainedSpansPerResource": 5000,
      "RetainedMetricPointsPerResource": 10000
    }
  }
}
```

`Store` defaults to `InMemory`. Use `Database` when a local development or
team-owned environment should preserve traces and metrics across CloudShell
host restarts. Retention limits are per resource and prevent persisted
telemetry from growing without bound. Source logs are provider-owned streams
and are not controlled by this telemetry store switch.

## Usage

Usage samples are retained in memory by default. Resource monitoring can record
provider-observed CPU, memory, network, process, and custom monitoring metrics
as usage samples, and hosts can opt into database-backed usage history through
appsettings:

```json
{
  "Usage": {
    "Store": "Database",
    "RetainedSamplesPerResource": 10000
  }
}
```

`Store` defaults to `InMemory`. Use `Database` when a local development or
team-owned environment should preserve usage samples and usage statistics
across CloudShell host restarts. Retention limits are per resource and prevent
persisted usage history from growing without bound. Usage records keep
non-secret metric metadata, such as the monitoring provider and display name,
so dashboards can summarize resource usage without depending on a single
provider-specific metric shape.

## Application Logs

Application provider stdout/stderr logs are memory-only by default. Hosts can
opt into separate plain log files for application logs by configuring the
application provider from appsettings, for example:

```json
{
  "Observability": {
    "ApplicationLogs": {
      "Store": "File",
      "LogDirectory": "Data/application-logs",
      "LogRetentionDays": 7,
      "RetainedLogEntries": 5000,
      "SplitLogFilesByDay": false
    }
  }
}
```

`Store` defaults to `InMemory`; use `File` when application logs should survive
the current CloudShell session. Persisted application logs remain provider-owned
plain files, not resource snapshot data. `LogRetentionDays` and
`RetainedLogEntries` bound how much is kept. `SplitLogFilesByDay` can be
enabled when an environment wants a separate file per day; the current log
provider can still read across those files, and a future UI can expose a
day picker when that becomes useful.

Resource event logs are a separate platform activity stream and are not
controlled by the application log file settings.

## SQL Server

Use `SqlServer` (or `Mssql`) and ordinary EF Core SQL Server connection
strings:

```json
{
  "Persistence": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=localhost;Database=CloudShell;User Id=cloudshell;Password=...;TrustServerCertificate=True"
  },
  "Identity": {
    "BuiltIn": {
      "Persistence": {
        "Provider": "SqlServer",
        "ConnectionString": "Server=localhost;Database=CloudShellIdentity;User Id=cloudshell_identity;Password=...;TrustServerCertificate=True"
      }
    }
  }
}
```

Keep secrets out of committed settings. Environment-variable overrides are:

```bash
Persistence__Provider=SqlServer
Persistence__ConnectionString="..."
Identity__BuiltIn__Persistence__Provider=SqlServer
Identity__BuiltIn__Persistence__ConnectionString="..."
```

Use separate empty databases/catalogs for Resource Manager and built-in
Identity. Separate credentials are recommended for on-premise/shared hosting.
`Persistence:IdentityConnectionString` is still read as a legacy compatibility
setting when the built-in identity persistence section is absent, but new
configuration should use `Identity:BuiltIn:Persistence`.

To add a future migration, use the local EF Core tool from the repository root:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project CloudShell.Persistence/CloudShell.Persistence.csproj --startup-project CloudShell.Host/CloudShell.Host.csproj --context CloudShellDbContext --output-dir Migrations/CloudShell
dotnet ef migrations add <Name> --project CloudShell.Persistence/CloudShell.Persistence.csproj --startup-project CloudShell.Host/CloudShell.Host.csproj --context CloudShellIdentityDbContext --output-dir Migrations/Identity
```

## Resource Templates

Resource templates provide import/export without changing the database ownership
model. CloudShell exports accepted Resource graph state as a `ResourceTemplate`
containing `ResourceDefinition` entries. Applying a template validates and
commits resource definitions through the Resource Manager graph apply path.
Providers still own validation and runtime planning for their resource types,
but they no longer own per-resource template serializers.

See [Resource templates](resource-templates.md).
