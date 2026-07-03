# Resource Definition Sketch

This early sketch records the original JSON authoring direction. The current
working structure is maintained in
[resource-definition-structure.md](resource-definition-structure.md).

## Resource

```json
{
  "$artifact": "resource",
  "name": "acme-api",
  "typeId": "application.executable",
  "resourceId": "application.executable:acme-api",
  "providerId": "applications.executable",
  "attributes": {
    "path": "whatsup.exe",
    "command": {
      "arguments": "doc"
    },
    "storage.volumeConsumer": {
      "mounts": [
        {
          "volume": "cloudshell.volume:data",
          "targetPath": "/data",
          "readOnly": false
        }
      ]
    }
  }
}
```

## Resource Type

```json
{
  "$artifact": "resource.type",
  "typeId": "application.executable",
  "classId": "application",
  "capabilities": {
    "monitoring": {},
    "storage.volumeConsumer": {}
  },
  "attributes": {
    "command": {
      "defaultValue": ".",
      "required": false,
      "valueType": "complexType"
    }
  },
  "operations": {
    "start": {},
    "stop": {},
    "restart": {}
  }
}
```

## Resource Class

```json
{
  "$artifact": "resource.class",
  "classId": "application",
  "attributes": {},
  "capabilities": {},
  "operations": {}
}
```
