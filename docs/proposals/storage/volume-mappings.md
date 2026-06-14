# Storage and Volume Mappings Proposal

## Status

In progress.

Initial domain primitives are in place for container app volume mounts:
`ResourceVolumeMount`, workload descriptor projection, container declaration
builder support, application resource mount counts, and a storage volume
consumer capability. Runtime provider materialization, first-class volume
resources, Resource Manager create/attach UI, and usage monitoring remain open.

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

MVP resource type:

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

For MVP, the primary concept is a volume. A volume represents a stable
mountable storage target that can be attached to one or more resources through
a target path.

A volume projects:

- stable resource ID and display name
- storage kind, such as host directory, Docker volume, file share, or provider
  volume
- selected provider or host resource when applicable
- non-secret location metadata when safe to show
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

The exact public abstraction may differ, but the important split is stable:
CloudShell owns the relationship between resource and volume; providers own how
that relationship becomes a bind mount, Docker volume, Kubernetes persistent
volume claim, SMB mount, NFS mount, or platform-native storage attachment.

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
    "volume:postgres-data",
    "Postgres Data");

var postgres = resources
    .AddContainerApplication("application:postgres", "Postgres", "postgres:16")
    .WithVolume(data, "/var/lib/postgresql/data")
    .WithEnvironment("POSTGRES_DB", "cloudshell");
```

Explicit host directory:

```csharp
var data = resources
    .AddVolume("volume:uploads", "Uploads")
    .UseHostPath("./data/uploads");

resources
    .AddAspNetCoreProject("application:web", "Web", "../Web/Web.csproj")
    .WithVolume(data, "/app/uploads");
```

Provider-backed mountable storage:

```csharp
var storage = resources.AddStorageProvider(
    "storage:nas",
    "Team NAS");

var media = resources
    .AddVolume("volume:media", "Media")
    .UseProvider(storage)
    .WithClass("shared-file");
```

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
- whether credentials or host capabilities are missing
- whether a requested path conflicts with another mount
- whether a volume is persistent, ephemeral, shared, or read-only

## Resource Manager

Resource Manager should show storage from both sides:

- volume resource overview: provider, host, usage, diagnostics, and actions
- target resource overview: attached volumes, target paths, read-only flags,
  and materialization status

MVP UI should support:

- creating a basic local or provider-backed volume
- attaching a volume to a container app or executable app
- showing both managed volume resources and unmanaged/local volume references
  used by applications
- showing unresolved provider/host diagnostics
- leaving room for provider-owned usage monitoring data such as capacity,
  consumed bytes, inode/file counts, IOPS, throughput, or quota status
- warning before deleting a volume that has active attachments

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
2. Add the `cloudshell.volume` resource type identifier.
3. Add declaration builders for volumes and volume mounts. Container app
   `WithVolume(...)` support is in place for managed volume resource IDs and
   unmanaged local volume references.
4. Project volume attachments on application resources. Mount counts and
   workload descriptor volume mounts are in place.
5. Map container app volume attachments through the default local and Docker
   Compose orchestrator paths.
6. Add Resource Manager generated overview support for attached volumes.
7. Add create/attach UI for basic volume mappings.
8. Add action capability reasons and diagnostics for missing providers,
   missing host paths, unsupported mounts, and conflicting target paths.
9. Add sample coverage with a stateful container application.
10. Add provider-backed examples for Docker volumes and a future on-premise
    storage provider.

## Remaining Tasks

- Decide whether simple volume mounts should be platform-owned resource data,
  provider-owned app configuration, or both through a projected normalized
  shape.
- Decide first-class support for host paths versus named volumes in local
  development.
- Add deletion safety rules for volumes with active attachments.
- Define backup/snapshot as future capabilities, not MVP requirements.
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
