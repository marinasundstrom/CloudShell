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

```yaml
resources:
  - type: application.python-app
    name: api
    project:
      path: src/api
```

The equivalent C# declaration is:

```csharp
resources
    .AddPythonApp("api", "src/api");
```

The default local runtime starts:

```bash
python3 app.py
```

`command`, `scriptPath`, `module`, and `arguments` describe the local process
shape without making a specific Python web framework part of the CloudShell
resource model. When `module` is configured, the runtime starts
`python3 -m <module>` instead of a script path.

The default command is currently `python3` on every operating system. Windows
hosts that use the Python launcher convention can set `command` to `py` or
another interpreter command explicitly; CloudShell passes the selected command
and each argument as discrete process values rather than shell-quoted text.

Endpoint and script choices are scenario-specific additions:

```yaml
resources:
  - type: application.python-app
    name: api
    project:
      path: src/api
    endpoints:
      - name: http
        protocol: http
        targetPort: 5188
        port: 5188
        exposure: Local
    command: python3
    scriptPath: app.py
```

Python app resources can declare endpoint requests, environment variables,
service references, health checks, log sources, and volume mounts using the
same Resource model patterns as other application resources. The default local
runtime tracks process state and exposes process logs and metrics through
Resource Manager.

Lifecycle actions require a Python app runtime controller. The built-in
provider registration supplies the local process runtime controller for normal
hosts. If a custom or direct operation path is constructed without that
controller, Resource Manager projects lifecycle actions as unavailable with a
missing-controller reason, and direct provider-execution calls return the same
readiness failure as a diagnostic instead of succeeding as a no-op.

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
`project.path`, `command`, `scriptPath`, `module`, and `arguments`.

Python launcher declarations use the Python-native `as_container_app(...)`
method for the same projection:

```python
resources.add_python_app("api", "src/api").as_container_app(
    tag="dev",
    dockerfile="Dockerfile",
)
```

## Samples

`samples/PythonAppHost` demonstrates Python-native launcher authoring. It uses
the experimental package under `Launchers/Python/cloudshell` to declare:

- an `application.python-app` API rooted at `samples/PythonAppHost/App`
- Configuration Store and Secrets Vault resources referenced by the Python app
- a resource identity and read grants used by the Python runtime SDK
- endpoint, health, logs, monitoring, and environment projection through
  Resource Manager

`samples/PythonContainerApp` uses the same Python launcher package to declare a
Dockerfile-backed `application.container-app` with Configuration Store, Secrets
Vault, resource identity grants, and a `/configuration` endpoint that reads
both services through the Python runtime SDK.

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
service endpoints and CloudShell identity variables; `samples/PythonAppHost`
uses that SDK from the launched app instead of hand-building service calls.
