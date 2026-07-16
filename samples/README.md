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
| `DeviceRegistry` | C# launcher authoring for device enrollment, device identity, and configuration access. | Preferred IoT/device identity sample. |
| `BunJavaScriptApp` | C# launcher authoring for a Bun-backed JavaScript app resource. | JavaScript runtime variant sample. |
| `TypeScriptAppHost` | TypeScript launcher authoring and template apply. | Experimental launcher sample. |
| `TypeScriptContainerApp` | TypeScript launcher authoring for a Dockerfile-backed Node.js container app that reads Configuration Store and Secrets Vault through the TypeScript runtime SDK. | Experimental launcher sample. |
| `ReactTypeScriptApp` | TypeScript launcher authoring for a React frontend, Node backend, Configuration Store, and load-balancer resource. | Experimental launcher sample. |
| `RoboticMowerIoT` | C# launcher authoring for a React frontend, SignalR container backend, Device Registry MQTT sync, and a standalone simulated mower device. | IoT launcher sample with enrolled device control. |
| `JavaAppHost` | Java launcher authoring and template apply. | Experimental launcher sample. |
| `JavaContainerApp` | Java launcher authoring for a Maven-built, Dockerfile-backed container app that reads Configuration Store and Secrets Vault through the Java runtime SDK. | Experimental launcher sample. |
| `GoAppHost` | Go launcher authoring and template apply. | Experimental launcher sample. |
| `GoContainerApp` | Go launcher authoring for a Dockerfile-backed container app that reads Configuration Store and Secrets Vault through the Go runtime SDK. | Experimental launcher sample. |
| `PythonAppHost` | Python launcher authoring for a Python app, Configuration Store, and Secrets Vault. | Experimental launcher sample. |
| `PythonContainerApp` | Python launcher authoring for a Dockerfile-backed container app that reads Configuration Store and Secrets Vault through the Python runtime SDK. | Experimental launcher sample. |
| `RabbitMQMessaging` | C# launcher authoring for a RabbitMQ broker with .NET and Java app resources exchanging fan-out events. | Preferred broker-backed app topology sample. |

## Host composition samples

These samples intentionally define CloudShell hosts because the host itself, a
split-hosting topology, or sample-local host registration is part of what they
prove.

| Sample | Primary scenario | Status |
| --- | --- | --- |
| `JavaScriptContainerApp` | C# host-composition sample for a JavaScript app projected as a container app; reads Configuration Store and Secrets Vault through the TypeScript runtime SDK. | Provider and host-composition coverage; launcher parity is covered by `TypeScriptContainerApp`. |

## Container App Runtime Proof Matrix

| Runtime | Sample | Authoring surface | SDK proof | Tooling version notes |
| --- | --- | --- | --- | --- |
| JavaScript/TypeScript | `TypeScriptContainerApp` | TypeScript launcher. | `sdk/typescript/configuration-client` reads Configuration Store and Secrets Vault from `/configuration`. | TypeScript 5.9.3 launcher manifest/lockfile; Node.js 22 image; local SDK `file:` dependency. |
| Java | `JavaContainerApp` | Java launcher. | `sdk/java/cloudshell` reads Configuration Store and Secrets Vault from `/configuration`. | Java 21 runtime/compiler target; Maven 3.9.9 Docker build stage. |
| Go | `GoContainerApp` | Go launcher. | `sdk/go/cloudshell` reads Configuration Store and Secrets Vault from `/configuration`. | Go 1.22 build image and module version. |
| Python | `PythonContainerApp` | Python launcher. | `sdk/python/cloudshell` reads Configuration Store and Secrets Vault from `/configuration`. | Python 3.12 runtime image. |

## Supported sample status

The current Resource model switch-readiness samples run through the new
provider path. The remaining seams are runtime/control-plane bridges that keep
the samples functional while provider runtime behavior is moved behind the new
provider boundaries.

## MVP seam triage

The MVP loose-end pass tracks only seams that are visible in supported local
runs, required by smoke coverage, or likely to confuse contributors. Classify
each seam as `Fix now`, `Accepted MVP bridge`, or `Post-MVP deferred` before
opening a broader platform capability.

| Sample | Visible seam or bridge | Classification | Next action |
| --- | --- | --- | --- |
| `ApplicationTopology` | SQL credentials are resolved through the provider-owned `/api/sql-server/v1/credentials` endpoint injected from the API's SQL Server reference, while the full SQL/database identity model remains experimental. | Accepted MVP bridge | Keep the endpoint permission-gated and documented; defer Azure-like database identity semantics until the SQL/database identity proposal is active. |
| `ApplicationTopology` | Local DNS/name mapping is written through the local host-name publisher when requested. | Accepted MVP bridge | Keep explicit and opt-in; use `CLOUDSHELL_LOCAL_HOSTS_FILE` for non-mutating validation. |
| `SettingsAndSecrets` | Configuration Store and Secrets Vault backing services run as local C# service projects. | Accepted MVP bridge | Keep as the current service-integration proof while preserving provider ownership of values, grants, and service endpoints. |
| `ThirdPartyIdentity` | Keycloak setup and workload credential environment materialization use sample-local adapter classes. | Accepted MVP bridge | Keep as the external OIDC validation proof; do not treat the Keycloak adapter as a built-in provider contract. |
| `ReplicatedContainerHealth` | Local Docker/Traefik bridge materializes replicas and ingress while the durable orchestrator is still evolving. | Accepted MVP bridge | Keep covered by smoke tests; defer richer startup states and runtime-resource-specific generated details unless the app page becomes misleading. |
| `ContainerAppDeployment` | Image and replica update APIs apply graph state through the deferred runtime bridge; lifecycle materialization is explicitly deferred and real build-server image production is out of scope. | Accepted MVP bridge | Keep this as the graph-apply/API proof; avoid presenting it as finished deployment orchestration. |
| `HostVirtualNetwork` | CoreDNS file publishing proves DNS/name intent without OS-native virtual adapters or isolation. | Accepted MVP bridge | Keep provider gaps explicit; improve diagnostics only where the reconcile action leaves users without a next step. |
| `LoadBalancer` | Traefik runtime management still uses the existing provider path while graph DNS and route reconciliation are provider-owned. | Accepted MVP bridge | Keep the bridge documented; move runtime management only if current Resource Manager actions or smoke tests expose confusing behavior. |
| `SplitHosting` | The UI sample uses a local-development client credential to call the protected Control Plane API. | Accepted MVP bridge | Keep the local-only secret warning; use this sample as the remote-client projection gate, not a production auth template. |

| Sample | Primary scenario | Current Resource model status |
| --- | --- | --- |
| `ProjectReference` | ASP.NET Core project-to-project service discovery, logs, health, and traces. | Uses a C# launcher AppHost against `CloudShell.LocalDevelopmentHost`; old application-provider project records are no longer declared. |
| `RabbitMQMessaging` | RabbitMQ broker-backed communication between .NET and Java app resources. | Uses a C# launcher AppHost against `CloudShell.LocalDevelopmentHost`; the shared local host owns RabbitMQ Docker materialization while the workload apps use their native RabbitMQ clients. |
| `SettingsAndSecrets` | Resource model Configuration Store and Secrets Vault consumed by an ASP.NET Core project. | Switched to the Resource model provider path; old application/configuration/secrets provider records are no longer declared. |
| `ThirdPartyIdentity` | Keycloak-backed identity setup and protected Configuration Store access. | Switched to the Resource model provider path; Keycloak setup and API identity environment remain sample-local runtime seams. |
| `ApplicationTopology` | Multi-resource app topology across storage, SQL, configuration, secrets, identity, DNS, and project resources. | Switched to the Resource model provider path; SQL Docker, SQL credential brokering, identity credential environment, configuration, and secrets runtime behavior now use provider-owned adapters. |
| `ReplicatedContainerHealth` | Replicated container app runtime, health/liveness, logs, traces, metrics, and hidden runtime replica projection. | Switched to the Resource model provider path; container-app runtime/orchestrator dispatch, local command execution, and runtime replica log/monitoring providers now use provider-owned seams, while Docker/Traefik materialization and hidden runtime-resource projection remain sample-local. |
| `SignalRContainerApp` | Blazor WebAssembly frontend connected to a replicated SignalR container app backend with sticky routing. | Declares the frontend project and backend container app through the Resource model; runtime materialization is deferred to the shared container app runtime/orchestrator work. |
| `RoboticMowerIoT` | React frontend controlling simulated robotic mower devices through a SignalR container backend and Device Registry MQTT twin sync. | Uses a C# launcher AppHost against `CloudShell.LocalDevelopmentHost`; the simulated mower process remains outside the resource graph to model a real enrolled device. |
| `ContainerAppDeployment` | Container app image and replica updates through the Control Plane API. | Switched to the Resource model provider path; deployment API updates graph state through provider-owned deferred container-app and local Docker container runtime adapters. |
| `CloudShell.ContainerHost` | Storage-backed SQL Server lifecycle using a resource graph volume. | Switched to the new projection only; SQL Docker runtime materialization now uses the provider-owned local SQL Server Docker runtime adapter. |
| `HostVirtualNetwork` | Host-local virtual network endpoint mapping. | Switched to the new projection only; endpoint mapping now uses the provider-owned graph endpoint-mapping reconciler and the Control Plane local host-network provisioner, while CoreDNS file publishing remains sample-local. |
| `LoadBalancer` | Resource model load-balancer routes and DNS/name mapping. | Uses Resource model definitions with provider-owned graph DNS reconciliation and Traefik load-balancer materialization for HTTP, TCP, and optional runtime-container startup. |
| `CertificateLoadBalancer` | Secrets Vault certificate consumed by a Traefik HTTPS load-balancer entrypoint. | Uses Resource model definitions with provider-owned Secrets Vault certificate storage and Traefik PEM materialization; runtime container startup is disabled in smoke coverage so the sample validates generated config and certificate files without Docker. |
| `SplitHosting` | Remote UI rendering Resource Definitions-backed resources through a separate Control Plane. | Switched to the new projection only; the old direct Resource Manager comparison record and toggle have been removed. |

Samples such as `SettingsAndSecrets`, `ApplicationTopology`,
`ThirdPartyIdentity`, and the container/network/load-balancer samples should
migrate to launcher form when their remaining sample-local host seams are
available through `CloudShell.LocalDevelopmentHost` or through first-class
template/control-plane APIs.

The `ProjectReference`, `JavaScriptApp`, `BunJavaScriptApp`, `JavaApp`, `GoApp`, and
`PythonAppHost` samples now use launcher AppHosts because they primarily
exercise resource types and configuration. Their source or launcher projects
depend on launcher/resource builder packages, while the CloudShell host project
is selected at runtime by the launcher configuration.

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
