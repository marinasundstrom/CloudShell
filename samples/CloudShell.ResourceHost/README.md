# CloudShell Resource Host Sample

This sample hosts CloudShell UI and the Control Plane in the same ASP.NET Core
process. It adds a custom resource extension and declares two sample resources
at startup.

## Run

From the repository root:

```bash
dotnet run --project samples/CloudShell.ResourceHost --urls http://localhost:5102
```

Open:

```text
http://localhost:5102
```

Useful routes:

```text
http://localhost:5102/resources
http://localhost:5102/api/control-plane/v1/resources
http://localhost:5102/openapi/control-plane-v1.json
```

You can also check the resource API from the command line:

```bash
curl http://localhost:5102/api/control-plane/v1/resources
```

## What To Look For

- `Program.cs` uses the convenience `builder.AddCloudShell()` registration for
  UI and Control Plane together.
- `SampleResourceExtension` contributes a resource provider and resource type.
- `SampleResourceProvider` exposes sample API, database, and worker resources.
- The startup `Resources(...)` block declares `sample:api` and
  `sample:database`, so they are visible immediately in Resource Manager and
  through the Control Plane API.

Runtime state is stored under `samples/CloudShell.ResourceHost/Data/` and is
ignored by git.
