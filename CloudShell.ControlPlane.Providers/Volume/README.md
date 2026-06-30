# CloudShell Volume Built-in Provider

## Overview

- Resource type: `cloudshell.volume`
- Provider id: `cloudshell.storage`
- Purpose: declares a CloudShell volume resource in the Resource Graph.

## Ported

- Storage class/type defaults.
- Provider, medium, location, subpath, access-mode, and persistence attributes.
- Passive storage-volume capability marker.
- Typed `ResourceReference` storage dependencies and storage-reference graph validation.
- Type-specific `storage.volume.provision` operation provider with an injected provider-owned provisioner seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.
- Manual `ResourceDefinitionGraphBuilder.AddCloudShellVolume(...)` builder
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
    "storage.volume.persistent": true
  }
}
```

This is the interchange shape for a storage-bound CloudShell volume declaration
that depends on an explicit storage resource.

For Local Storage, `storage.volume.subPath` identifies the folder mapping under
the local storage resource. A consuming resource still chooses its own mount
target path, for example `App_Data` for an ASP.NET Core project or
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
    "storage.volume.persistent": true
  }
}
```

## Switch-over status

Ready as a supporting graph resource for the current storage and SQL-backed
sample paths. Volume declarations can be authored and projected through the
Resource Manager bridge, and current sample coverage validates storage-backed
mount materialization through runtime handlers. Provider-owned durable volume
materialization, usage, health, monitoring, and UI flows remain outside the
initial switch gate.

## Remaining

- Runtime filesystem availability as capability members or operation plans.
- Provider-backed volume materialization, health, monitoring, and UI registration/update flow.
