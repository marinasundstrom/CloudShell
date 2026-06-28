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

This sample keeps the UI host itself unauthenticated, but protects the Control
Plane API. The Control Plane enables the built-in token authority and registers
a local `cloudshell-split-ui` client. The UI remote adapter uses the
client-credentials flow to acquire a bearer token before calling the Control
Plane API.

The configured client secret is for local development only. Use a secret store
or environment variable for shared hosts.

Shell environment preferences such as theme and collapsed navigation are saved
through the remote Control Plane settings adapter because the UI sample sets
`Shell:EnvironmentSettings:Storage` to `ControlPlane`. In this sample, the Control
Plane stores them in its local `Data/environment-settings.json` file under the identity
represented by the UI's Control Plane credential.

## Resource Graph POC coverage

The split Control Plane exposes `network:graph-split-sample`, a graph-backed
network resource projected through the Resource Definitions bridge. The
separate UI host renders it through the remote Control Plane client, which
keeps this sample useful for validating graph-backed Resource Manager
projections before changing public API or client contracts.

The Control Plane now exposes only the graph-backed network resource. The old
persisted `network:split-sample` comparison record and `SplitHosting:GraphOnly`
toggle have been removed from this sample. The smoke test uses the sample as a
remote-client switch-readiness gate: the UI must render the graph resource
through the remote adapter, and the Control Plane API must not need a legacy
Resource Manager network record for the graph projection to appear.
