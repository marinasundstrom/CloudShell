# CloudShell Application Topology Sample

This sample is the broad local-development MVP proof. It declares a resource
model topology for an ASP.NET Core frontend, ASP.NET Core API, Configuration
Store, Secrets Vault, SQL Server, SQL database, storage, volume, host
configuration source, and local DNS name mapping.

Run the sample:

```bash
dotnet run --project samples/ApplicationTopology/Host -- --urls http://localhost:5104
```

Open Resource Manager at:

```text
http://localhost:5104/resources
```

## Declared Resources

The host declares these Resource Definitions-backed resources:

- `cloudshell.storage:application-topology-local`
- `cloudshell.volume:application-topology-sql-data`
- `application.sql-server:application-topology-sql-server`
- `application.sql-database:application-topology-db`
- `configuration.store:application-topology-settings`
- `secrets.vault:application-topology-secrets`
- `configuration.host:application-topology-host-settings`
- `application.aspnet-core-project:application-topology-api`
- `application.aspnet-core-project:application-topology-frontend`
- `dns:application-topology-local`

The old application, configuration, secrets, storage, volume, and SQL resource
records are no longer declared by this sample. The old side-by-side comparison
setting has been removed.

The frontend references the API through `project.references` and service
discovery. The API references the Configuration Store, Secrets Vault, and SQL
Server through typed provider-owned resource references, not `DependsOn` as a
service-discovery mechanism. `DependsOn` remains startup ordering intent.

## Runtime Behavior

The sample keeps focused runtime seams where behavior is still sample-specific:

- The provider-owned local SQL Server Docker runtime starts the SQL Server
  container for the `application.sql-server` resource and resolves the
  storage-backed volume declaration into a bind mount.
- The provider-owned SQL Server credential endpoint at
  `/api/sql-server/v1/credentials` issues short-lived SQL logins for callers
  with a matching CloudShell resource identity grant. The API receives
  `CLOUDSHELL_SQL_CREDENTIAL_ENDPOINT` through its SQL Server resource
  reference instead of hard-coding the provider route in the sample.
- The API receives standard `CLOUDSHELL_IDENTITY_*` workload credential
  variables through its declared `WithIdentity(...)` binding instead of
  hand-authoring those variables in the sample resource definition.
- Configuration Store and Secrets Vault start through provider-owned runtime
  controllers with seeded sample data.
- ASP.NET Core API and frontend resources start through the Resource model
  ASP.NET Core project runtime controller.

These seams are transitional. The sample should keep proving the workload path
while durable provider runtime implementations replace sample-local code.

## Verification Focus

Current smoke coverage verifies:

- Resource Manager projection without old provider records.
- Storage -> volume -> SQL Server -> database relationships.
- Configuration Store and Secrets Vault runtime startup and authenticated API
  reads.
- API settings/secrets consumption through CloudShell clients.
- SQL credential materialization and API `/database` access.
- Frontend-to-API service discovery through `/upstream`.
- SQL Docker container cleanup on stop and graceful host shutdown.

The local DNS resource maps `app.application-topology.cloudshell.local` to the
frontend HTTP endpoint. Set `CLOUDSHELL_LOCAL_HOSTS_FILE` to inspect generated
host settings without modifying the system hosts file.

Runtime state is stored under `samples/ApplicationTopology/Host/Data/` and is
ignored by git.
