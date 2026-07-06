# CloudShell Python launcher

This package provides experimental Python helpers for authoring CloudShell
ResourceTemplate app hosts.

```python
from cloudshell.launcher import CloudShellDistributedApplication

app = CloudShellDistributedApplication.create_builder("sample")
app.with_metadata("cloudshell.source", "python")

def define_resources(resources):
    resources.add_python_app("api", resources.resolve_path("..", "App"))

app.define_resources(define_resources)
raise SystemExit(app.run())
```

Python apps can also be projected as container apps while keeping their Python
project metadata:

```python
resources.add_python_app("api", "src/api").as_container_app(
    tag="dev",
    dockerfile="Dockerfile",
)
```

The launcher supports the shared verbs used by other CloudShell launchers:

- `template` prints the ResourceTemplate JSON.
- `apply` applies the template to an existing Control Plane.
- `start` starts or reuses the configured host daemon, then applies the template.
- `run` runs the host in the foreground, applies the template, and keeps the
  host tied to the launcher lifetime.

No command defaults to `run`.
