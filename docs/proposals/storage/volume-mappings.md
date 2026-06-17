# Storage and Volume Mappings Proposal

## Status

In progress.

Initial domain primitives are in place for volume resources and container app
volume mounts: `cloudshell.volume`, `ResourceVolumeMount`, workload descriptor
projection, container declaration builder support, application resource mount
counts, storage volume/consumer capabilities, standard volume mount
permissions, first Resource Manager selectors plus a dedicated Storage tab for
container-backed resources that can map volumes, and a basic Resource Manager
create/configuration/overview flow for direct `cloudshell.volume` resources.
SQL Server is the first resource-specific flow that recommends a known data
mount point and warns when data will not be persisted.
Volume overviews show reverse consumers, including declared target path and
read/write mode when the workload descriptor is available.
The current container materializers support `FileSystem` mounts and application
Start/Restart availability now reports when a managed volume or storage parent
uses an unsupported medium. Container hosts can now advertise the standard
`storage.mount.filesystem` capability, and application Start/Restart
availability reports when the selected host does not advertise that capability
for a managed `FileSystem` volume. Provider-defined storage resources,
provider-backed volume resources, richer host-specific compatibility
negotiation, and usage monitoring remain open.
The local Docker runner now records runtime-observed volume mount
materialization facts, including resolved source, target path, access mode, and
active/not-active status, after a successful container start. Application
overview pages show those observations and volume overview pages surface the
consumer's aggregate materialization status through projected resource
attributes. Volume materialization observations now use the shared
`IResourceVolumeMountMaterializationStore` contract so orchestrators can report
runtime facts without depending on the application provider. Docker Compose
records observations through that contract after successful Start/Restart
actions and marks existing observations not active after Stop. Resource Manager
generated diagnostics now warn when standard mount materialization attributes
report partial, not-active, or unknown status, and Local Storage overview pages
warn when consumers of owned volumes report incomplete or unobserved mount
materialization. Provider-backed storage runtime reporting remains open.
Deletion is guarded for volume resources that are still referenced by another
resource dependency, and storage mappings cannot be changed while the target
resource is running.

## Problem

Useful application environments need persistent and shared storage. Container
applications, services, databases, queues, and stateful helper resources often
need volumes, bind mounts, configuration files, data directories, or
provider-backed persistent disks.

CloudShell should model mountable storage in the same resource-oriented way as
networking and identity:

- users declare stable storage intent
- providers materialize that intent on a local host, Docker host, cluster,
  NAS, cloud volume, or appliance
- Resource Manager shows which applications use which storage and whether the
  mappings are materialized

Without a common volume model, mountable storage risks becoming hidden
provider-specific configuration inside container apps, Docker Compose files, or
sample code.

At the same time, CloudShell should avoid over-sharing storage concepts. A
volume mount, object bucket, managed database, file share, backup policy, and
storage account are related infrastructure concerns, but they do not all have
the same lifecycle, access model, runtime attachment semantics, or UI workflow.
The first abstraction should cover mountable volumes only, then add adjacent
storage resource types when their contracts are clear.

## Goals

- Model mountable volumes and volume mappings as CloudShell resources or
  projected resource facts.
- Let container applications and executable applications attach named storage
  without depending on Docker-specific syntax.
- Support local development through host directories and Docker volumes first.
- Keep provider-owned storage implementation details behind provider
  contracts.
- Let Resource Manager create, inspect, attach, detach, and diagnose storage
  mappings.
- Support durable state for container apps and provider-owned service
  resources such as databases, registries, identity providers, and observability
  components.
- Preserve a path to on-premise mountable storage providers such as NFS, SMB,
  local disks, Kubernetes persistent volumes, CSI drivers, NAS appliances, or
  cloud disks.

## Non-Goals

- Do not make CloudShell a storage orchestrator in the first version.
- Do not standardize object-storage, database, backup, snapshot, encryption,
  replication, quota, or retention concepts as part of the mountable-volume
  MVP.
- Do not store secrets, credentials, certificates, or connection strings in
  volume metadata.
- Do not require all storage to be persisted by default. Persistence should be
  explicit for runtime services that otherwise can be disposable.
- Do not expose provider-native mount syntax directly as the stable resource
  model.
- Do not make object buckets, managed databases, queues, or secret stores
  pretend to be volumes. They should become separate resource types or provider
  services with their own access and lifecycle model.

## Resource Model

Mountable storage should use ordinary CloudShell resources and projected
attachments.

MVP resource types:

- `cloudshell.storage`
- `cloudshell.volume`

Possible later resource types, intentionally not part of the first volume
abstraction:

- `cloudshell.storageAccount`
- `cloudshell.fileShare`
- `cloudshell.objectBucket`

Suggested capability identifiers:

- `storage.provider`
- `storage.volume`
- `storage.fileShare`
- `storage.objectBucket`
- `storage.mountProvider`
- `storage.snapshot`
- `storage.backup`

Container hosts that can materialize filesystem mounts should advertise:

- `storage.mount.filesystem`

For MVP, the primary storage concepts are a storage resource and a volume. The
first concrete storage kind is Local Storage. A Local Storage resource is a
`ResourceClass.Storage` resource that projects class-level attributes such as
storage provider, storage medium, provider location, and owned volume count.
The class describes the portable resource shape CloudShell can reason about;
the concrete kind/provider announces which medium it supports and should
report diagnostics when a requested shape cannot be materialized. Local Storage
supports the `FileSystem` medium and can use a local filesystem location as
the root for owned volumes.

A volume is an allocated physical storage space that can be referenced and
utilized by a resource. In the current direct model, a volume can point at any
folder or provider-addressable location and can be attached to one or more
resources through a target path.

Resource Manager volume selectors should list mountable volume resources, not
storage-provider parent resources. The selector may display the volume storage
medium, such as `FileSystem`, so users can see what kind of mountable storage
they are attaching. Start/Restart action availability should preflight the
currently selected mappings and report when the provider cannot materialize a
managed volume medium. Future host providers should advertise storage media or
mount capabilities explicitly so compatibility can be negotiated per host
instead of being inferred from the current container materializer.
The first host-level negotiation is intentionally narrow:
Docker-compatible hosts advertise `storage.mount.filesystem`, and application
Start/Restart availability checks that capability for managed `FileSystem`
volume resources when a selected container host can be resolved.

Storage-owned volumes are sub-items of their Storage resource in Resource
Manager. They keep `cloudshell.volume` identity, but the projected resource
uses the Storage resource as parent and owner, is hidden from the normal
resource inventory by default, and is managed from the Storage resource's
Volumes tab. This applies to Storage-class providers generally, not just the
Local Storage provider; providers may contribute richer storage-specific
management views later, but the default shell behavior should keep owned
volumes under their storage parent. Hidden in this context means "not a
top-level inventory item by default"; the volume is still part of the resource
graph and Resource Manager may present it from Storage-owned views or workflows
that select mountable volumes when the user has permission. Environments may
also hide or restrict volume inspection by permission.

`resources.AddVolume(...)` declares a CloudShell volume resource through the
default Local Storage provider unless another provider is supplied. Its path is
the direct relative or absolute folder path for that volume and is not derived
from, appended to, or otherwise affected by a separate storage resource
location. This is intentionally lightweight for local development: the volume
can be tracked as a resource without requiring a separate storage-provider
resource or storage-control-plane service. Direct volumes are normal inventory
resources because the volume itself is the thing being managed.

A storage resource is a boundary whose behavior depends on the concrete
resource kind/provider. It may represent local storage, a NAS share, remote
host, appliance, cloud storage account, Kubernetes storage class, or another
provider-defined storage boundary. For the default Local Storage kind, the
resource class is Storage and the storage medium is `FileSystem`. Volumes
created under that storage resource are expected to be sub-items, typically
subfolders, of the provider-managed storage location.
Direct volumes created with `resources.AddVolume(...)` are the exception: they
carry their own supplied path and do not use a storage resource `Location`;
that storage resource location remains null. Other providers may use different
sub-item semantics as long as the projected CloudShell volume remains a
mountable target. The Local Storage provider is a temporary default
until storage capabilities are formalized. After storage capabilities are
defined, the provider should materialize the allocation and report
provider-specific diagnostics and usage metrics for the resource.

Shared on-premise environments need a stricter policy boundary than local
development. Any operation that changes the host machine or shared platform
state should be separately controllable and normally limited to administrators
or platform operators. Examples include standalone local filesystem volumes,
host path mounts, host-file DNS publishing, network setup, public endpoint
binding, and OS feature enablement. The first storage UI keeps standalone
direct volumes available for developer scenarios, but the product direction is
to let hosted environments disable or restrict standalone local storage so
users create storage-backed volumes through approved Storage resources instead.
Creating a volume under a Storage resource already requires manage permission
on that parent Storage resource; direct standalone volumes continue to use the
ordinary resource creation path until environment-level host-affecting
operation policy is introduced.

A volume projects:

- stable resource ID and display name
- storage kind, such as host directory, Docker volume, file share, or provider
  volume
- selected provider or host resource when applicable
- non-secret location metadata when safe to show
- reverse consumer mappings, including target path and requested access mode
  when the consuming workload descriptor is available
- aggregate materialization status for consuming resources, and provider-owned
  runtime observations for source, target path, access mode, and active status
  where the runtime provider can report them
- capabilities and diagnostics
- lifecycle or reconcile actions when provider-backed materialization is
  supported

A volume mapping connects a source volume to a target resource path:

```csharp
public sealed record ResourceVolumeMount(
    string VolumeReference,
    string TargetPath,
    bool ReadOnly = false,
    string? Name = null);
```

The mount's required access permission is derived from `ReadOnly`:

- read-only mount: `CloudShell.Storage/volumes/mount/read/action`
- read/write mount: `CloudShell.Storage/volumes/mount/write/action`

Resource identity grants use the existing CloudShell resource-permission model:

```csharp
var data = resources
    .AddVolume("volume:postgres-data")
    .WithDisplayName("Postgres Data");
var postgres = resources
    .AddContainerApplication("application:postgres", "postgres:16")
    .WithDisplayName("Postgres")
    .RequireIdentity()
    .WithVolume(data, "/var/lib/postgresql/data");

data.Allow(postgres, CloudShellPermissions.Storage.Actions.MountWrite);
```

Authored resource types can also define meaningful storage attachment points.
For example, the SQL Server resource recommends a writable data volume mounted
at `/var/opt/mssql` and can warn when no such mount is configured. This lets a
provider expose resource-specific storage intent without leaking the rest of
its container implementation into the public resource model.

The exact public abstraction may differ, but the important split is stable:
CloudShell owns the relationship between resource and volume; providers own how
that relationship becomes a bind mount, Docker volume, Kubernetes persistent
volume claim, SMB mount, NFS mount, or platform-native storage attachment.
Runtime providers can report observed materialization through
`IResourceVolumeMountMaterializationStore`, using
`ResourceVolumeMountMaterialization` records. This is runtime observation, not
desired configuration: it records the resolved source, target path, access
mode, status, optional reason, and observed time for a resource's mounted
volumes. The first store is backed by application runtime state so application
and volume overview pages can show materialized, not-active, partial, or
unknown storage status without coupling each orchestrator to the Applications
provider.

`VolumeReference` can point at a first-class `cloudshell.volume` resource, but
it does not have to. Local development should allow simple named or host-local
volumes without requiring every mount source to become a managed resource.
On-premise and team-managed environments should prefer first-class volume
resources so Resource Manager can track ownership, usage, lifecycle,
diagnostics, usage monitoring, and deletion safety across resources.

Do not use `ResourceVolumeMount` for non-mounted service access. For example,
an S3-compatible bucket, Azure Blob container, database, queue, or object-store
API should normally be represented as a service/resource endpoint plus identity
and permissions, not as a file-system mount, unless a provider explicitly
offers a mountable file-system interface.

## Declaration Model

Minimal local volume:

```csharp
var data = resources.AddVolume(
    "volume:postgres-data")
    .WithDisplayName("Postgres Data");

var postgres = resources
    .AddContainerApplication("application:postgres", "postgres:16")
    .WithDisplayName("Postgres")
    .WithVolume(data, "/var/lib/postgresql/data")
    .WithEnvironment("POSTGRES_DB", "cloudshell");
```

Explicit host directory:

```csharp
var data = resources
    .AddVolume("volume:uploads")
    .WithDisplayName("Uploads")
    .UseHostPath("./data/uploads");

resources
    .AddAspNetCoreProject("application:web", "../Web/Web.csproj")
    .WithDisplayName("Web")
    .WithVolume(data, "/app/uploads");
```

Provider-backed mountable storage by provider name:

```csharp
var media = resources
    .AddVolume("volume:media")
    .WithDisplayName("Media")
    .UseProvider("nas")
    .UseLocation("team-share/media")
    .WithAccessMode(VolumeAccessMode.ReadWriteMany);
```

Future provider-defined storage resource with provider-owned sub-volumes:

```csharp
var storage = resources.AddLocalStorage(
    "storage:local")
    .WithDisplayName("Local Storage")
    .UseLocation("./storage");

var media = resources
    .AddVolume("volume:media")
    .WithDisplayName("Media")
    .UseStorage(storage, "media");
```

The exact builder names may evolve, but the model split is stable: Storage is
the resource class, Local Storage is the concrete storage kind/provider,
`FileSystem` is the medium it announces, and a volume is the mountable
sub-item that should project compatible medium information for consumers.

## Provider Responsibilities

Volume providers materialize mountable volumes and mounts for the target
runtime.

Examples:

- Local host provider: creates or validates host directories.
- Docker provider: creates Docker volumes or bind mounts.
- Docker Compose provider: emits `volumes` and service mount declarations.
- Kubernetes provider: emits persistent volume claims and volume mounts.
- NAS provider: validates file-share availability and mount options.
- Cloud provider: materializes disks, file shares, or other mountable storage.

Providers should report:

- whether the volume exists
- whether it can be mounted into the target resource
- whether the target host or runtime can handle the storage medium
- whether credentials or host capabilities are missing
- whether a requested path conflicts with another mount
- whether a volume is persistent, ephemeral, shared, or read-only

Not every storage medium can be mounted by every resource runtime. Docker-style
containers can handle `FileSystem` mounts, so Local Storage volumes are a
generally supported first path. Later storage providers may expose media that
need provider-specific materialization or cannot be accepted by a given
container host. Runtime providers and container hosts must be able to reject or
diagnose unsupported volume media instead of treating every `cloudshell.volume`
as universally mountable. A resource may mount a volume only when the target
runtime and storage provider agree on the storage medium.

## Resource Manager

Resource Manager should show storage from both sides:

- storage resource overview: provider boundary, owned volumes, consumers, and
  consumer-reported materialization summaries and diagnostics; the first Local
  Storage view is in place
- volume resource overview: provider, host, usage, diagnostics, and actions
- target resource overview: attached volumes, target paths, read-only flags,
  and materialization status

MVP UI should support:

- creating a basic direct local or provider-addressable volume; the first
  direct volume create/configuration/overview flow is in place
- attaching a volume to a container app or executable app; the first container
  app registration selector and resource Storage tab are in place for
  container-backed resources that can map volumes
- creating and managing volumes under a Storage resource when the user has
  manage permission on that parent Storage resource
- showing both managed volume resources and unmanaged/local volume references
  used by applications
- showing unresolved provider/host diagnostics
- leaving room for provider-owned usage monitoring data such as capacity,
  consumed bytes, inode/file counts, IOPS, throughput, or quota status
- preventing storage mapping changes while the target resource is running
- blocking deletion of a volume that is still referenced by another resource

## Relationship to Container Apps and Services

Container apps should support volume attachments without becoming
provider-specific. Docker, Docker Compose, Kubernetes, and future providers can
translate the same mapping into their own runtime model.

Service resources can depend on volumes when they represent stateful platform
services. For example, a PostgreSQL service resource, identity provider, or
observability stack may attach provider-owned or user-declared storage.

Provider-owned runtime containers can also use volumes, but the stable
CloudShell resource should remain the user-facing resource, such as the load
balancer, identity provider, database, or observability service.

## Relationship to Other Storage Concepts

CloudShell should add other storage abstractions only when their behavior is
clear:

- File shares may be mountable volumes when used through SMB/NFS-like mounts,
  or services when accessed through an API.
- Object buckets are service resources, not volumes, unless mounted through a
  provider-owned file-system layer.
- Databases are service resources with connection endpoints, identity, secrets,
  backups, and provider-owned runtime state.
- Backup and snapshot policies are operational capabilities or separate
  resources, not properties every volume must carry in the MVP.
- Storage accounts are provider or grouping resources when a provider needs
  that boundary; they should not be required for simple local volumes.

## Identity and Access

Storage access should eventually use the same resource identity and permission
model as configuration and secrets where the provider exposes protected APIs or
identity-aware mounts.

For MVP, volume attachment authorization can start with resource management
permissions. Provider-backed storage that exposes protected APIs or credentials
must keep credential material provider-owned and should use resource identity
where possible.

Volume data-plane access should use resource identity grants. Runtime
materializers should evaluate the target resource identity against the volume
resource and the mount's required permission before attaching the volume. A
read-only mount requires the storage mount read permission; a writable mount
requires the storage mount write permission. Local development hosts can
initially warn instead of blocking unmanaged volume references because those
references may intentionally not resolve to a CloudShell volume resource.

Volume control-plane deletion is blocked while another resource depends on the
volume. Providers should keep this rule even after richer attachment tracking
exists: a volume with active attachments should be detached first, then deleted.

## API and UI Projection

The HTTP API should project volume resources as ordinary resources and project
volume mappings on resources that attach storage.

Expected projection:

- resource type IDs such as `cloudshell.volume`
- storage attributes that are safe and non-secret
- volume mount references on target resources
- provider/host references
- action capabilities and diagnostics

Usage monitoring should be modeled as observed provider data, not as required
fields on every mount. A local development volume may have no monitoring data,
while a provider-backed on-premise volume can report capacity, usage, and quota
through future monitoring APIs.

## Implementation Plan

1. Add storage and volume capability identifiers. Initial storage volume
   consumer capability is in place.
2. Add the `cloudshell.volume` resource type identifier. Initial platform
   volume resource projection is in place.
3. Add declaration builders for volumes and volume mounts. `AddVolume(...)`
   and container app `WithVolume(...)` support are in place for managed volume
   resource IDs and unmanaged local volume references.
4. Project volume attachments on application resources. Mount counts and
   workload descriptor volume mounts are in place.
5. Add first Resource Manager volume selector for resources that can reference
   volumes. Container app create selectors and a dedicated resource Storage tab
   for container-backed resources are in place.
6. Map container app volume attachments through runtime paths. The default
   local Docker runner now translates managed `FileSystem` volume resources
   into bind mounts and preserves unmanaged references as Docker named
   volumes. The Docker Compose generator now emits service volume mounts,
   resolves managed `FileSystem` volume resources to host bind-mount paths,
   and preserves unmanaged references as Compose volume names.
7. Add Resource Manager generated overview support for attached volumes. Done
   for application overview pages; generic generated-resource projection can
   still be improved for non-application volume consumers.
8. Add dedicated create/attach UI for basic volume resources and mappings.
   Direct volume create/configuration/overview UI is in place. Local Storage
   overview pages now list owned volumes with consumer counts and
   consumer-reported materialization summaries and warn when those consumers
   report incomplete or unobserved mount materialization; richer attach flows
   and storage-resource-owned sub-volume UI remain open.
9. Add action capability reasons and diagnostics for missing providers,
   missing host paths, unsupported mounts, and conflicting target paths.
10. Extend deletion safety from dependency-based guard to explicit attachment
   tracking once mount materialization records attachment state. Initial
   dependency-based deletion blocking is in place.
11. Add sample coverage with a stateful container application.
12. Add provider-backed examples for Docker volumes and a future on-premise
    storage provider.

## Remaining Tasks

- Decide whether simple volume mounts should be platform-owned resource data,
  provider-owned app configuration, or both through a projected normalized
  shape.
- Decide first-class support for host paths versus named volumes in local
  development.
- Extend deletion safety from dependency-based blocking to explicit attachment
  state once runtime materialization records mounted volumes.
- Define backup/snapshot as future capabilities, not MVP requirements.
- Define the temporary Local Storage provider shape and replace it with
  capability-based storage resources once storage capabilities are stable.
- Add provider/host compatibility diagnostics so container hosts only accept
  volumes whose storage medium they can materialize.
- Map container app volume attachments through later provider-backed
  orchestrator paths.
- Add on-premise storage provider sample after the local/Docker path works.

## Open Questions

- Should a volume mount be a child resource, a projected attachment, or a
  resource attribute plus typed descriptor?
- Should volumes support read/write sharing policies in the first version?
- How should CloudShell represent externally managed volumes?
- Should local host paths be allowed in UI-created production resources, or
  restricted to development environments?
- How should identity-based storage access map to providers such as SMB, NFS,
  Azure Files, S3-compatible storage, or Kubernetes CSI drivers?
