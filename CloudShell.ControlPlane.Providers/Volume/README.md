# CloudShell Volume Built-in Provider

## Overview

- Resource type: `cloudshell.volume`
- Provider id: `cloudshell.storage`
- Purpose: declares a CloudShell volume resource in the Resource Graph.

## Ported

- Storage class/type defaults.
- Provider, medium, location, subpath, access-mode, persistence, and observed
  max-size attributes.
- Passive storage-volume capability marker.
- Typed `ResourceReference` storage dependencies and storage-reference graph validation.
- Type-specific `storage.volume.provision` operation provider with an injected provider-owned provisioner seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.
- Manual `ResourceGraphBuilder.AddCloudShellVolume(...)` builder
  plus `AddVolume(...)` convenience builders for code-first volume authoring.
  Graph-level `AddVolume(...)` creates an ad-hoc local filesystem volume;
  storage builder `AddVolume(...)` creates a storage-bound volume with typed
  storage dependencies.

## Example ResourceDefinition

This is the interchange shape for an ad-hoc local filesystem CloudShell volume
declaration. Runtime materialization remains a provider/control-plane concern.

Relative `storage.volume.location` values are resolved by the runtime from the
host's working/content-root context. The default ad-hoc location is `.`.

```json
{
  "name": "data",
  "typeId": "cloudshell.volume",
  "resourceId": "cloudshell.volume:data",
  "providerId": "cloudshell.storage",
  "displayName": "Application data",
  "attributes": {
    "storage.volume.provider": "local",
    "storage.volume.medium": "FileSystem",
    "storage.volume.location": "App_Data",
    "storage.volume.accessMode": "ReadWriteOnce",
    "storage.volume.persistent": true,
    "storage.volume.maxSizeBytes": 1073741824,
    "storage.volume.maxSizeEnforcement": "advisory"
  }
}
```

This is the interchange shape for a storage-bound CloudShell volume declaration
that depends on an explicit storage resource.

For Local Storage, `storage.volume.subPath` identifies the folder mapping under
the local storage resource. A consuming resource still chooses its own mount
target path, for example `App_Data` for an .NET app or
`/var/opt/mssql` for SQL Server.

```json
{
  "name": "sql-data",
  "typeId": "cloudshell.volume",
  "resourceId": "cloudshell.volume:sql-data",
  "providerId": "cloudshell.storage",
  "displayName": "SQL Server Data",
  "dependsOn": [
    {
      "value": "cloudshell.storage:local",
      "relationship": "dependsOn",
      "addressingMode": "resourceId",
      "typeId": "cloudshell.storage",
      "providerId": "cloudshell.storage"
    }
  ],
  "attributes": {
    "storage.volume.provider": "local",
    "storage.volume.medium": "FileSystem",
    "storage.volume.subPath": "sql",
    "storage.volume.accessMode": "ReadWriteOnce",
    "storage.volume.persistent": true,
    "storage.volume.maxSizeBytes": 10737418240,
    "storage.volume.maxSizeEnforcement": "advisory"
  }
}
```

Volume max sizes are observed storage limits. Local filesystem and ordinary
Docker-mounted volumes should report advisory max-size status unless their
backing storage provider can prove hard enforcement. Resource monitoring emits
used bytes, configured max size, remaining bytes, utilization, and
max-size-reached samples so the Usage workspace can retain the volume growth
history over time. The shell can visualize fixed-size snapshots as used and
unused space in a quota circle; usage history remains a line trend.

## Switch-over status

Ready as a supporting graph resource for the current storage and SQL-backed
sample paths. Volume declarations can be authored and projected through the
Resource Manager bridge, and current sample coverage validates storage-backed
mount materialization through runtime handlers. Provider-owned durable volume
materialization, usage, health, monitoring, and UI flows remain outside the
initial switch gate.

## Remaining

- Runtime filesystem availability as capability members or operation plans.
- Provider-backed hard max-size enforcement and richer provider-specific volume
  materialization.
