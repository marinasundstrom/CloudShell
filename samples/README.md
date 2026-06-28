# Samples

This directory contains sample projects used to verify and demonstrate different CloudShell scenarios.

## Supported sample status

The current Resource Graph POC switch-readiness samples default to the new
graph-backed provider path. Use each sample's `GraphOnly=false` setting only
when intentionally running the old-provider comparison path.

| Sample | Primary scenario | Current graph-provider status |
| --- | --- | --- |
| `ProjectReference` | ASP.NET Core project-to-project service discovery, logs, health, traces, and ResourceDefinition apply flow. | Defaults to graph-only; old application-provider project records are comparison-only. |
| `SettingsAndSecrets` | Graph-backed Configuration Store and Secrets Vault consumed by an ASP.NET Core project. | Defaults to graph-only; runtime backing services are provider-owned sample seams. |
| `ThirdPartyIdentity` | Keycloak-backed identity setup and protected graph Configuration Store access. | Defaults to graph-only; Keycloak setup and graph API identity environment are sample-local runtime seams. |
| `ApplicationTopology` | Multi-resource app topology across storage, SQL, configuration, secrets, identity, DNS, and project resources. | Defaults to graph-only; this remains the broad switch-readiness proof for common provider interactions. |
| `ReplicatedContainerHealth` | Replicated container app runtime, health/liveness, logs, traces, metrics, and hidden runtime replica projection. | Defaults to graph-only; Docker-backed runtime/log/resource seams remain sample-local until the provider boundary is stabilized. |
| `ContainerAppDeployment` | Container app image and replica updates through the Control Plane API. | Defaults to graph-only; deployment API updates graph state through a sample-local bridge. |
| `CloudShell.ContainerHost` | Storage-backed SQL Server lifecycle using a graph volume. | Switched to graph-backed resources only; SQL Docker runtime materialization remains sample-local. |
| `HostVirtualNetwork` | Host-local virtual network endpoint mapping. | Switched to the new projection only; endpoint mapping still delegates through a sample-local Resource Manager provisioner bridge. |
| `LoadBalancer` | Graph-declared load-balancer routes and DNS/name mapping. | Defaults to graph-only; Traefik configuration and local hosts publishing use sample-local runtime bridges. |
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
