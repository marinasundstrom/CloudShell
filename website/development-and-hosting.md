---
title: Development and hosting
description: Understand how the same CloudShell resource model supports a local developer environment and a standing self-hosted environment.
---

# CloudShell for development and hosting

CloudShell uses one resource model in two operating contexts. A developer can run the platform beside source code for a fast inner loop; a team can run a standing CloudShell environment on infrastructure it owns. The resource vocabulary stays familiar, but the trust and operational boundaries change.

| | Local development | Shared or self-hosted environment |
| --- | --- | --- |
| Primary goal | Build, run, inspect, and diagnose an application graph quickly | Operate durable workloads and services for a team |
| Typical application shape | Language-specific project resources or container apps | Container apps and provider-managed services |
| Filesystem trust | The developer controls the host and may authorize local project paths | Users should not select arbitrary paths on the runtime host |
| State | Project-local and disposable by default | Durable Control Plane state and explicit persistence policy |
| Authentication | May be disabled on loopback for a trusted local session | Required, with authorization enforced by the Control Plane |
| Placement | Usually the developer machine and local Docker | Operator-selected hosts, networks, and providers |

## Development: run close to the code

The current first-run experience is `cloudshell run` with a project-local `cloudshell.yaml`. The CLI starts the bundled local host, applies the template, and keeps the environment tied to the terminal session.

Language-specific resources—JavaScript, Java, Go, Python, .NET, and executable apps—can point at trusted local source. This enables short edit-and-run cycles while still providing resource relationships, endpoints, lifecycle, logs, health, and telemetry.

## Hosting: narrow the trust boundary

A shared host should prefer container apps for application workloads. Images provide a clearer artifact, filesystem, placement, and isolation boundary than a user-supplied path on the host. Host-run language resources should only be enabled when the operator deliberately supports trusted artifact or path-based workflows.

A standing environment also needs durable persistence, authentication, provider configuration, backups, network policy, and operational ownership. CloudShell does not create those policies merely because the local and hosted resource types look similar.

## Host settings that enforce the boundary

Three settings are especially important when moving beyond a developer-owned host:

- `ApplicationResources:HostRunResourceTypesEnabled` controls whether executable, .NET, JavaScript, Java, Go, and Python process-backed resource providers are installed. A hosted profile should normally leave them disabled.
- `ResourceManager:AllowLocalPathResourceDefinitions` controls whether applied definitions may select project paths, executable paths, scripts, working directories, and build contexts on the host filesystem.
- `DeploymentArtifacts:Enabled` controls the host-managed artifact upload and revision path for application resources. Enabling it also requires an artifact store.

These are host policy, not fields in an application template. A launcher can describe a project path for a trusted local profile; it cannot make an arbitrary remote host accept that path.

## Combined and split hosting

The local profile normally combines Resource Manager and the Control Plane in one process. A shared installation can separate the UI from the Control Plane: the UI becomes a client, while the Control Plane remains authoritative for resources, providers, authorization, operations, logs, traces, and persisted state.

Splitting the processes does not create another resource model. Templates, launchers, the CLI, and Resource Manager still target the same Control Plane contracts.

## Move without changing the application story

The transition is usually from a language-specific local resource to a container app, not from one product model to another:

1. Model dependencies, endpoints, configuration, secrets, and telemetry locally.
2. Package the workload as an image.
3. Express it as an `application.container-app` resource.
4. Apply the template to a compatible hosted environment.
5. Let that environment's providers resolve placement, networking, identity, storage, and runtime behavior.

CloudShell is in preview. Remote hosting, provider parity, deployment promotion, and clustered Control Plane behavior are still evolving; validate the capabilities of the target environment before treating a local template as a production deployment definition.

If you are evaluating an application orchestrator rather than a standing environment, see [CloudShell and Aspire](cloudshell-and-aspire.md) for a technical comparison of their operating boundaries.
