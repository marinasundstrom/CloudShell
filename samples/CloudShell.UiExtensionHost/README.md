# CloudShell UI Extension Host Sample

This sample hosts only the CloudShell UI shell and adds a custom UI extension.
It does not initialize persistence, register Resource Manager services, or map
the Control Plane API.

## Run

From the repository root:

```bash
dotnet run --project samples/CloudShell.UiExtensionHost --urls http://localhost:5101
```

Open:

```text
http://localhost:5101
```

The shell redirects to the custom extension route:

```text
http://localhost:5101/sample-workspace
```

## What To Look For

- `Program.cs` uses `builder.AddCloudShellUi()`.
- `SampleWorkspaceExtension` contributes a shell navigation item and start
  route.
- The sample uses `app.UseCloudShellUiAsync()` and
  `app.MapCloudShellUi<App>()`, so unknown routes fall back to the Blazor shell
  but Control Plane endpoints such as `/api/control-plane/v1/resources` are not
  mapped.
