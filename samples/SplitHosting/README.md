# Split Hosting Sample

This sample runs the Control Plane and CloudShell UI in separate ASP.NET Core
processes.

Run the Control Plane first:

```bash
dotnet run --project samples/SplitHosting/ControlPlane/CloudShell.SplitHosting.ControlPlane.csproj
```

Then run the UI:

```bash
dotnet run --project samples/SplitHosting/UI/CloudShell.SplitHosting.UI.csproj
```

The Control Plane listens on `http://localhost:5095`. The UI listens on
`http://localhost:5096` and registers `IControlPlane` through the remote
`CloudShell.ControlPlane.Client` adapter.
