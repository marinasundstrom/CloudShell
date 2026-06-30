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
| `ApplicationTopology` | Multi-resource app topology across storage, SQL, configuration, secrets, identity, DNS, and project resources. | Switched to the Resource model provider path; SQL Docker, local configuration, and secrets runtime behavior remain sample-local seams. |
| `ReplicatedContainerHealth` | Replicated container app runtime, health/liveness, logs, traces, metrics, and hidden runtime replica projection. | Switched to the Resource model provider path; Docker-backed runtime/log/resource seams remain sample-local until the provider boundary is stabilized. |
| `ContainerAppDeployment` | Container app image and replica updates through the Control Plane API. | Switched to the Resource model provider path; deployment API updates graph state through a sample-local bridge. |
| `CloudShell.ContainerHost` | Storage-backed SQL Server lifecycle using a resource graph volume. | Switched to the new projection only; SQL Docker runtime materialization remains sample-local. |
| `HostVirtualNetwork` | Host-local virtual network endpoint mapping. | Switched to the new projection only; endpoint mapping still delegates through a sample-local Resource Manager provisioner bridge. |
| `LoadBalancer` | Resource model load-balancer routes and DNS/name mapping. | Uses Resource model definitions with sample-local Traefik configuration and local hosts publishing bridges. |
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
