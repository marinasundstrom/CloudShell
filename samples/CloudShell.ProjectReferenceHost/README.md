# CloudShell Project Reference Sample

This sample mirrors the Aspire-style local dev loop where one project resource
references another project resource.

`CloudShell.ProjectReferenceHost` declares two ASP.NET Core project resources:

- `Project Reference API` on `http://localhost:5217`
- `Project Reference Frontend` on `http://localhost:5218`

The frontend resource uses:

```csharp
.WithReference(api)
.DependsOn(api)
.WithServiceDiscovery()
```

CloudShell starts both projects with `dotnet watch` by default. The frontend
receives Aspire-compatible service discovery environment variables and resolves
the API endpoint through normal .NET configuration.

## Run

From the repository root:

```bash
dotnet run --project samples/CloudShell.ProjectReferenceHost --urls http://localhost:5104
```

Open:

```text
http://localhost:5104/resources
```

Run the `Project Reference API` resource, then run the
`Project Reference Frontend` resource. Open:

```text
http://localhost:5218/upstream
```

The response includes the resolved API endpoint and the API health payload.

Runtime state is stored under `samples/CloudShell.ProjectReferenceHost/Data/`
and is ignored by git.
