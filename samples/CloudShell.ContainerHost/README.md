# CloudShell Container Host Sample

This sample shows the minimal resource graph flow for a container-backed SQL
Server service shape.

CloudShell has two usage modes:

- Local dev orchestrator: the default orchestrator runs resources locally.
  Container resources use the default Docker host unless a resource calls
  `WithContainerHost(...)`.
- On-premise mode: an orchestrator such as Docker Compose owns lifecycle,
  networking, and exposure for the resource graph.

The storage graph is intentionally explicit. The storage resource announces
the `FileSystem` medium. The SQL data volume depends on that storage resource
and projects the same medium. The provider-owned local SQL Server Docker runtime
materializes that `FileSystem` volume as a bind mount when SQL Server starts.

## Resource graph coverage

The sample declares Resource Definitions-backed resources:

- `cloudshell.storage:local`: local storage projection.
- `cloudshell.volume:sql-data`: SQL data volume with a typed startup
  dependency on the storage resource.
- `application.sql-server:sql-server`: SQL Server resource
  with an explicit local `tds` endpoint and a volume-consumer capability for
  `/var/opt/mssql`.

Those resources prove projection, storage/volume attributes, typed storage
dependency, and volume-consumer capability shape.

The SQL Server lifecycle operations use the provider-owned local SQL Server
Docker runtime adapter. That adapter resolves the mounted CloudShell volume and
its storage parent from the graph, creates the storage-backed host directory,
and starts the SQL Server container with a bind mount. Focused Docker smoke
coverage starts, restarts, and stops the SQL resource through Resource Manager
and verifies the storage-backed volume directory is created. The adapter also
removes Docker's failed-created container and retries once when a newly-created
bind-mount path is not immediately visible to the Docker daemon.

The sample now declares only the storage, volume, and SQL Server resources and
uses the provider-owned local SQL Server Docker runtime for lifecycle operations. The old
application-provider storage, volume, and SQL Server records, old application
provider registration, and comparison path have been removed. Docker smoke
coverage exercises the SQL runtime path without old provider records.

The sample also projects the SQL resource into the Resource Manager
orchestration catalog with `ControlPlaneScoped` lifetime. A focused smoke test
starts the SQL resource and then gracefully stops the host process,
verifying that the existing host-scoped shutdown service removes the SQL
container. The generic sample-host readiness smoke test still has defensive
cleanup because that harness kills sample processes instead of allowing hosted
shutdown services to run.
