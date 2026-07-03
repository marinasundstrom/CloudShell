# Samples

This directory contains sample projects used to verify and demonstrate
different CloudShell scenarios.

## Sample authoring policy

New application and resource-authoring samples should use a launcher whenever
the sample is proving ordinary user authoring. The sample app host should
declare a `ResourceTemplate`, then run or target `CloudShell.LocalDevelopmentHost`
through the launcher/CLI path. It should not define a full CloudShell Control
Plane host just to declare resources.

Use a custom C# host sample only when the point of the sample is host
composition or host-owned behavior, such as:

- building a Control Plane or UI host
- demonstrating split hosting
- registering a UI extension, Resource Manager UI extension, provider, or
  runtime adapter that is not installed in `CloudShell.LocalDevelopmentHost`
- proving a sample-local runtime seam that has not moved behind a provider
  package yet

When a full host remains necessary, the sample README should say why. Once the
required provider/runtime behavior is installed in the local development host
profile, move the sample to a launcher shape.

## Launcher samples

These samples are the preferred proof path for language-specific app
authoring. They declare resources outside the CloudShell host process and
target `CloudShell.LocalDevelopmentHost`.

| Sample | Primary scenario | Status |
| --- | --- | --- |
| `CSharpAppHost` | C# launcher authoring with Resource Model builders. | Preferred C# app-authoring sample. |
| `TypeScriptAppHost` | TypeScript launcher authoring and template apply. | Experimental launcher sample. |
| `JavaAppHost` | Java launcher authoring and template apply. | Experimental launcher sample. |
| `GoAppHost` | Go launcher authoring and template apply. | Experimental launcher sample. |

## Host composition samples

These samples intentionally define CloudShell hosts because the host itself, a
split-hosting topology, or sample-local host registration is part of what they
prove.

## Supported sample status

The current Resource model switch-readiness samples run through the new
provider path. The remaining seams are runtime/control-plane bridges that keep
the samples functional while provider runtime behavior is moved behind the new
provider boundaries.

| Sample | Primary scenario | Current Resource model status |
| --- | --- | --- |
| `ProjectReference` | ASP.NET Core project-to-project service discovery, logs, health, traces, and ResourceDefinition apply flow. | Switched to the Resource model provider path; old application-provider project records are no longer declared. |
| `SettingsAndSecrets` | Resource model Configuration Store and Secrets Vault consumed by an ASP.NET Core project. | Switched to the Resource model provider path; old application/configuration/secrets provider records are no longer declared. |
| `ThirdPartyIdentity` | Keycloak-backed identity setup and protected Configuration Store access. | Switched to the Resource model provider path; Keycloak setup and API identity environment remain sample-local runtime seams. |
| `ApplicationTopology` | Multi-resource app topology across storage, SQL, configuration, secrets, identity, DNS, and project resources. | Switched to the Resource model provider path; SQL Docker, configuration, and secrets runtime behavior now use provider-owned adapters, while the SQL credential endpoint remains sample-local. |
| `ReplicatedContainerHealth` | Replicated container app runtime, health/liveness, logs, traces, metrics, and hidden runtime replica projection. | Switched to the Resource model provider path; container-app runtime/orchestrator dispatch, local command execution, and runtime replica log/monitoring providers now use provider-owned seams, while Docker/Traefik materialization and hidden runtime-resource projection remain sample-local. |
| `SignalRContainerApp` | Blazor WebAssembly frontend connected to a replicated SignalR container app backend with sticky routing. | Declares the frontend project and backend container app through the Resource model; runtime materialization is deferred to the shared container app runtime/orchestrator work. |
| `ContainerAppDeployment` | Container app image and replica updates through the Control Plane API. | Switched to the Resource model provider path; deployment API updates graph state through provider-owned deferred container-app and local Docker container runtime adapters. |
| `CloudShell.ContainerHost` | Storage-backed SQL Server lifecycle using a resource graph volume. | Switched to the new projection only; SQL Docker runtime materialization now uses the provider-owned local SQL Server Docker runtime adapter. |
| `HostVirtualNetwork` | Host-local virtual network endpoint mapping. | Switched to the new projection only; endpoint mapping now uses the provider-owned graph endpoint-mapping reconciler and the Control Plane local host-network provisioner, while CoreDNS file publishing remains sample-local. |
| `LoadBalancer` | Resource model load-balancer routes and DNS/name mapping. | Uses Resource model definitions with provider-owned graph DNS reconciliation; Traefik route translation remains sample-local until load-balancer runtime integration moves fully into providers. |
| `SplitHosting` | Remote UI rendering Resource Definitions-backed resources through a separate Control Plane. | Switched to the new projection only; the old direct Resource Manager comparison record and toggle have been removed. |

Samples such as `ProjectReference`, `SettingsAndSecrets`,
`ApplicationTopology`, `JavaScriptApp`, `JavaApp`, and `GoApp` are candidates
to migrate to launcher form when their remaining sample-local host seams are
available through `CloudShell.LocalDevelopmentHost`.

## Creating a new sample

### Blazor applications

For the CloudShell hosting application to be compiled correctly as a Blazor application, the project must contain at least one Razor file.

If the sample does not define any custom Razor components, add an `_Imports.razor` file with the following content:

```razor
@using Microsoft.AspNetCore.Components
@using Microsoft.AspNetCore.Components.Web
```

### Ignoring the Data directory

The Control Plane may create a `Data` directory during execution. Since this directory contains generated data, it should typically not be committed to source control.

To exclude it from Git, add a `.gitignore` file with the following content:

```text
Data
```

This ensures that the `Data` directory is ignored by Git.
