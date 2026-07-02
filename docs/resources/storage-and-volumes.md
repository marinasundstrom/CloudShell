# Storage and Volumes

CloudShell models mountable storage as resources and resource attachments.
Storage intent is part of the resource graph; runtime materialization is owned
by providers and orchestrators.

This document covers the current mountable-volume model. Object buckets,
managed databases, backup policies, snapshots, encryption, and storage accounts
are separate future resource types or provider features, not part of the
mountable-volume contract.

## Resource Types

Current resource types:

| Type | Purpose |
| --- | --- |
| `cloudshell.storage` | Storage provider/root resource, currently used for Local Storage and owned volume grouping. |
| `cloudshell.volume` | CloudShell managed mountable volume, either ad-hoc or owned by a storage resource. |
| `storage.volume` | older/local volume resource type retained for compatibility and focused local-volume cases. |

All current storage and volume resource types use the `storage` resource class.

## Storage Resources

`cloudshell.storage` uses provider id `cloudshell.storage`.

Current attributes:

| Attribute | Meaning |
| --- | --- |
| `storage.kind` | Defaults to `provider`. |
| `storage.provider` | Required provider name; Local Storage is the current default. |
| `storage.medium` | Required storage medium; `FileSystem` is the current implemented medium. |
| `storage.location` | Optional provider/root location. |

Current capabilities:

- `storage.provider`
- `storage.mountProvider`

Current operation:

- `storage.inspect`

Local Storage supports the `FileSystem` medium and can own volume resources
under a filesystem root. Provider-owned credentials, protected APIs, or
external storage state must remain behind provider contracts.

## Volume Resources

`cloudshell.volume` uses provider id `cloudshell.storage`.

Current attributes:

| Attribute | Meaning |
| --- | --- |
| `storage.kind` | Defaults to `volume`. |
| `storage.volume.provider` | Storage provider name. |
| `storage.volume.medium` | Required storage medium; `FileSystem` is currently supported. |
| `storage.volume.location` | Direct volume location for ad-hoc/local volumes. |
| `storage.volume.subPath` | Storage-owned subpath under the parent storage resource. |
| `storage.volume.accessMode` | Required volume access mode. |
| `storage.volume.persistent` | Whether the volume is intended to persist. |
| `storage.volume.maxSizeBytes` | Optional positive byte count for a max-size intent. |
| `storage.volume.maxSizeEnforcement` | `advisory`, `enforced`, or `unknown`. |

Current capability:

- `storage.volume`

Current operation:

- `storage.volume.provision`

Access modes use `StorageVolumeAccessMode` in builders and project to the
volume attributes. The current default is `ReadWriteOnce`.

## Authoring

Declare a direct local filesystem-backed volume:

```csharp
var data = resources
    .AddVolume("data", path: "./data")
    .WithMaxSizeBytes(10L * 1024 * 1024 * 1024);
```

Declare a storage root and a storage-owned volume:

```csharp
var storage = resources
    .AddLocalStorage("local", "./.cloudshell/storage");

var data = storage.AddVolume(
    "sql-data",
    subPath: "sql-data",
    accessMode: StorageVolumeAccessMode.ReadWriteOnce,
    persistent: true);
```

`AddVolume(...)` creates `cloudshell.volume` resources. When a volume is owned
by a storage resource, the volume depends on that storage resource and uses the
storage-owned subpath as its backing location.

## Volume Consumers

Resources consume volumes through the `storage.volumeConsumer` capability
payload. The current payload shape is:

```json
{
  "mounts": [
    {
      "volume": "cloudshell.volume:sql-data",
      "targetPath": "/var/opt/mssql",
      "readOnly": false
    }
  ]
}
```

The graph validator requires every mount target to reference a volume resource
in the graph. Valid current target resource types are `cloudshell.volume` and
`storage.volume`.

The capability dependency provider projects volume mounts into graph
dependencies so Resource Manager can show relationships and block unsafe
deletion while a volume is referenced.

At runtime, workload descriptors project volume mounts as
`ResourceVolumeMount` records:

- `VolumeReference`
- `TargetPath`
- `ReadOnly`
- optional mount name

`ResourceVolumeMount.RequiredPermission` maps read-only mounts to
`CloudShell.Storage/volumes/mount/read/action` and writable mounts to
`CloudShell.Storage/volumes/mount/write/action`.

## Runtime Materialization

Providers own how a volume becomes a runtime mount:

- local process-backed applications materialize `FileSystem` volumes as
  filesystem links before launch
- Docker/container-backed resources materialize `FileSystem` volumes as bind
  mounts or provider-owned volume mounts
- Docker Compose records mount observations after successful Start/Restart and
  marks observations not active after Stop
- SQL Server uses a provider-owned local Docker runtime adapter for its data
  volume path

Container hosts that can materialize filesystem mounts advertise
`storage.mount.filesystem`. Start/Restart readiness checks fail early when a
managed `FileSystem` volume is selected but the resolved container host does
not advertise that capability.

The runtime observation contract is
`IResourceVolumeMountMaterializationStore`. Providers and orchestrators record
`ResourceVolumeMountMaterialization` values with:

- volume reference
- target path
- resolved source
- read-only flag
- status
- optional reason
- observation timestamp

Current statuses are `materialized` and `notActive`. Resource Manager also
derives aggregate materialization attributes such as
`storage.volumeMounts`, `storage.volumeMounts.materialized`, and
`storage.volumeMounts.materializationStatus` so generated diagnostics can warn
when mounts are partial, inactive, or unknown.

Storage and volume resources can also project runtime status with
`storage.runtimeStatus` and `storage.runtimeStatusReason`.

## Resource Manager Experience

Current Resource Manager behavior includes:

- volume selectors for resources that can attach volumes
- a Storage tab for container apps, executable apps, ASP.NET Core projects,
  SQL Server, and other volume-capable resources
- direct `cloudshell.volume` create/configure/overview flows
- storage resource pages that list owned volumes and consumer counts
- volume overview pages that show reverse consumers, target paths, read/write
  mode, and materialization summaries when available
- delete guards for volume resources still referenced by another resource
- blocked storage mapping changes while the target resource is running

Storage-owned volumes may be hidden from top-level inventory while still
visible from the parent storage resource, relationship views, selectors, and
authorized direct links.

## Provider Parity

A storage or host provider should document and implement:

- which storage media it supports
- whether volumes are direct, storage-owned, provider-owned, or imported
- how volume locations/subpaths are interpreted
- whether max size is advisory, enforced, or unknown
- which access modes are supported
- whether filesystem mounts require a host capability such as
  `storage.mount.filesystem`
- how runtime materialization observations are reported
- how cleanup and delete guards behave
- which Resource Manager views expose owned volumes, consumers, usage, and
  diagnostics

Do not expose provider-native mount syntax, storage credentials, certificates,
or connection strings as the stable CloudShell resource model.

## Launcher And Language Parity

Launchers and language SDKs should:

- emit `cloudshell.storage`, `cloudshell.volume`, and `storage.volumeConsumer`
  declarations through `ResourceDefinition`/`ResourceTemplate`
- preserve mount target path, read-only flag, storage dependency, and access
  mode semantics
- avoid serializing local machine secrets or provider credentials
- call the Control Plane/CLI apply path so graph validation and Resource
  Manager diagnostics run normally
- expose language-appropriate helpers for local storage roots, ad-hoc volumes,
  storage-owned volumes, and resource volume mounts

## Known Gaps

- Provider-backed storage beyond Local Storage remains open.
- Rich host/storage-medium compatibility negotiation is still narrow; the
  current implemented check is `FileSystem` plus `storage.mount.filesystem`.
- Provider-backed storage usage metrics are still incomplete.
- Direct/ad-hoc local volumes remain useful for developer scenarios, but
  approved storage-backed volume flows should become the preferred
  team/on-premise model.
- Object buckets, file shares, backup/snapshot policy, quotas, encryption, and
  storage accounts should be modeled separately when their contracts are
  proven.
