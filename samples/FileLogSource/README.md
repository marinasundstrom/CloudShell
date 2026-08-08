# File Log Source

This focused sample shows the minimum host policy and resource declaration for
reading an application-owned UTF-8 log file through CloudShell's common log
session API. CloudShell does not create or write the file.

Configure the Control Plane host with the directory that contains the log:

```json
{
  "CloudShell": {
    "Logs": {
      "Files": {
        "AllowedRoots": ["/srv/cloudshell-sample/logs"]
      }
    }
  }
}
```

Declare the source on the resource that owns the log:

```csharp
var api = resources
    .AddDotnetProject("api", projectPath)
    .WithFileLogSource(
        "application-file",
        "Application file",
        "/srv/cloudshell-sample/logs/api.log",
        ResourceLogSourceDefinitionValues.JsonConsole);
```

The application or its logging framework writes and rotates `api.log`.
CloudShell opens it only when a user reads the declared source, returns bounded
history, and follows complete appended lines while the session remains open.
The file can be combined with the resource's console source or sources from
other resources in the shared Logs view.

In a split-host deployment, the path is resolved on the Control Plane host,
not on the WebUI or launcher machine. Mount or otherwise expose provider-owned
storage to that host and allowlist only the narrow directory required by the
source.
