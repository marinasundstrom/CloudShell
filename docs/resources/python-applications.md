# Python Applications

Use the Python app resource type for local Python services that should
participate in the CloudShell local development resource graph. These
resources project as `application.python-app`.

CloudShell provider authoring is currently C#-only. The built-in Python app
provider is implemented in C#, and Python workloads integrate as managed
application resources. Python-native launcher builders are available through
the experimental package under `Launchers/Python/cloudshell`, while C# launcher
authoring remains available through `AddPythonApp(...)`.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [Executable applications](executable-applications.md),
[JavaScript applications](javascript-applications.md),
[Java applications](java-applications.md), [Go applications](go-applications.md),
and [Container apps](container-apps.md).

## Declaration

Programmatic C# declarations use `AddPythonApp(...)` with a scoped resource
name and project path:

```csharp
resources
    .AddPythonApp("api", "src/api")
    .WithDisplayName("Python API")
    .WithHttpEndpoint(port: 5188, targetPort: 5188, host: "localhost");
```

The default local runtime starts:

```bash
python3 app.py
```

`python.command`, `python.scriptPath`, `python.module`, and
`python.arguments` describe the local process shape without making a specific
Python web framework part of the CloudShell resource model. When
`python.module` is configured, the runtime starts `python3 -m <module>` instead
of a script path.

Python app resources can declare endpoint requests, environment variables,
service references, health checks, log sources, and volume mounts using the
same Resource model patterns as other application resources. The default local
runtime tracks process state and exposes process logs and metrics through
Resource Manager.

Use `AsContainerApp(...)` when a Python app should be authored as a Python
project but run as a scalable container app:

```csharp
resources
    .AddPythonApp("api", "src/api")
    .AsContainerApp(tag: "dev", dockerfile: "Dockerfile")
    .WithReplicas(3);
```

The projection changes the Resource Manager resource to
`application.container-app` while retaining Python project metadata such as
`project.path`, `python.command`, `python.scriptPath`, `python.module`, and
`python.arguments`.

## Samples

`samples/PythonAppHost` demonstrates Python-native launcher authoring. It uses
the experimental package under `Launchers/Python/cloudshell` to declare:

- an `application.python-app` API rooted at `samples/PythonAppHost/App`
- Configuration Store and Secrets Vault resources referenced by the Python app
- endpoint, health, logs, monitoring, and environment projection through
  Resource Manager

Run the launcher-owned local development host in a foreground terminal:

```bash
samples/PythonAppHost/cloudshell.sh run-no-auth
```

From a second terminal, open the Web UI, list resources, and start the Python
app:

```bash
samples/PythonAppHost/cloudshell.sh open
samples/PythonAppHost/cloudshell.sh resources
samples/PythonAppHost/cloudshell.sh start-app
```

The launcher package is still experimental. It covers ResourceTemplate
authoring, template/apply/start/run verbs, and the Python app sample. The
experimental runtime SDK under `sdk/python/cloudshell` provides Configuration
Store and Secrets Vault clients for Python workloads that receive injected
service endpoints and CloudShell identity variables.
