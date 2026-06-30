# Samples

This directory contains sample projects used to verify and demonstrate different CloudShell scenarios.

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
| `ReplicatedContainerHealth` | Replicated container app runtime, health/liveness, logs, traces, metrics, and hidden runtime replica projection. | Switched to the Resource model provider path; container-app runtime/orchestrator dispatch now uses the provider-owned delegating handler, while Docker/Traefik materialization, logs, monitoring, and hidden runtime-resource projection remain sample-local. |
| `SignalRContainerApp` | Blazor WebAssembly frontend connected to a replicated SignalR container app backend with sticky routing. | Declares the frontend project and backend container app through the Resource model; runtime materialization is deferred to the shared container app runtime/orchestrator work. |
| `ContainerAppDeployment` | Container app image and replica updates through the Control Plane API. | Switched to the Resource model provider path; deployment API updates graph state through provider-owned deferred container-app and local Docker container runtime adapters. |
| `CloudShell.ContainerHost` | Storage-backed SQL Server lifecycle using a resource graph volume. | Switched to the new projection only; SQL Docker runtime materialization now uses the provider-owned local SQL Server Docker runtime adapter. |
| `HostVirtualNetwork` | Host-local virtual network endpoint mapping. | Switched to the new projection only; endpoint mapping now uses the provider-owned graph endpoint-mapping reconciler and the Control Plane local host-network provisioner, while CoreDNS file publishing remains sample-local. |
| `LoadBalancer` | Resource model load-balancer routes and DNS/name mapping. | Uses Resource model definitions with provider-owned graph DNS reconciliation; Traefik route translation remains sample-local until load-balancer runtime integration moves fully into providers. |
| `SplitHosting` | Remote UI rendering Resource Definitions-backed resources through a separate Control Plane. | Switched to the new projection only; the old direct Resource Manager comparison record and toggle have been removed. |

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
