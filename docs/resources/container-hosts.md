# Container Hosts

Container hosts are placement/control boundaries for container-backed runtime
work. They are not the user-facing application resource and they are not the
runtime container instances that implement an app.

Use this feature doc for current generic container-host behavior. Docker-specific
remote host details remain tracked by the
[Remote Docker Hosts proposal](../proposals/containers/remote-docker-hosts.md)
until they become supported product behavior.

## Concepts

CloudShell keeps these concepts separate:

| Concept | Purpose |
| --- | --- |
| Stable resource | User-facing resource such as a container app, SQL Server, load balancer, or Docker host. |
| Container host | Placement boundary that can run container-backed runtime work. |
| Runtime state | Provider-owned containers, helper services, generated config, container IDs, and transient status. |

The generic graph-backed host type is `cloudshell.container-host`. Docker hosts
can also project as `docker.host` when the Docker provider owns a concrete
host resource. `docker.host` is Docker-specific; `cloudshell.container-host`
is the generic host boundary that providers can reference without binding
directly to Docker.

## Resource Model Shape

The built-in generic container-host provider uses:

- Resource type: `cloudshell.container-host`
- Provider id: `container-host.reference`
- Resource class: `Infrastructure`

Current attributes:

| Attribute | Meaning |
| --- | --- |
| `infrastructure.kind` | Defaults to `containerHost`. |
| `container.host.kind` | Host family such as `Docker`, `Podman`, `DockerCompatible`, `Kubernetes`, `Process`, or `Custom`. |
| `container.host.endpoint` | Non-secret endpoint display/connection value, defaulting to `unix:///var/run/docker.sock`. |
| `container.registry` | Default registry, defaulting to `docker.io`. |
| `container.host.default` | Boolean marker for default host selection. |

Current capability markers:

| Capability | Meaning |
| --- | --- |
| `container.image` | Host can run image-based container workloads. |
| `container.build` | Host can build or materialize images from local context/project input. |
| `storage.mount.filesystem` | Host can materialize filesystem-backed volume mounts. |

The generic host also exposes the `container.host.inspect` operation. Runtime
behavior remains provider-owned; the generic host provider is primarily a
resource model and descriptor bridge today.

## Host Descriptor

Runtime resolution uses `ContainerHostDescriptor`, not provider-specific
resource internals. The descriptor contains:

- host id and display name
- `ContainerHostKind`
- endpoint
- default-host marker
- default registry
- optional registry credentials
- `CredentialsAvailable`
- non-secret metadata
- advertised host capabilities

`ContainerHostDescriptor` is projected through
`ResourceOrchestrationDescriptor` using the resource type
`cloudshell.container-host`. The Control Plane resolver reads that descriptor
when selecting a host for container-backed work.

Credentials and transport details that are secret or provider-specific must
remain behind provider contracts. They must not be stored in platform
registration state, resource attributes, diagnostics, logs, or exported
templates.

## Resolution

Consumers use `IContainerHostResolver`. A resolution request includes:

- target resource id
- optional resource group id
- explicit host resource id
- preferred host id
- required host capability

The resolver order is:

1. Explicit host id from the resource or operation.
2. Preferred host id from the caller/orchestrator context.
3. Configured default hosts from `IContainerHostProvider`.
4. Graph-backed default host resources projected through orchestration
   descriptors.
5. Failure with a default-host-missing diagnostic.

Before a host is accepted, the resolver verifies:

- the host resource exists when selected by id
- the host resource is running when it came from the Resource Manager graph
- credentials are available
- the required capability is advertised

Current failure reasons are:

- `HostNotRegistered`
- `DefaultHostMissing`
- `HostUnavailable`
- `RequiredCapabilityMissing`
- `CredentialsUnavailable`
- `UnsupportedWorkload`

These failure reasons feed action capability diagnostics and preflight checks.
They should be shown as actionable Resource Manager reasons instead of leaked
provider exception text.

## Programmatic Authoring

`ResourceGraphBuilder.GetContainerHost()` lazily creates the default generic
host only when something needs it:

```csharp
var host = resources.GetContainerHost();

resources
    .AddContainerApplication("api")
    .WithImage("team/api:dev")
    .UseContainerHost(host);
```

The default authored resource is:

- resource id: `cloudshell.container-host:default`
- name: `default`
- display name: `Default container host`
- host kind: `Docker`
- endpoint: `unix:///var/run/docker.sock`
- registry: `docker.io`
- default marker: true

Use `AddContainerHost(...)` when a graph should declare an explicit generic
host. Docker-specific host resources and remote Docker behavior remain
provider-specific.

## Runtime Boundaries

Container hosts select where provider-owned runtime work can be materialized.
They do not make runtime containers the stable deployment target.

Examples:

- A container app owns image, endpoint, replica, revision, and ingress intent.
  The selected host materializes runtime replicas for that app.
- A SQL Server resource can omit an explicit host and let its builder use the
  default container host. The SQL runtime adapter materializes a Docker
  container and storage-backed bind mount behind the SQL Server resource.
- A load balancer resource can select a host for provider-owned Traefik
  runtime infrastructure while the load balancer remains the stable resource.

Provider-owned runtime state such as container IDs, generated configuration
files, transient health, and host credentials stays behind provider/runtime
contracts. Providers may project runtime-managed child resources for
inspection, but those are not the authored stable resource. See
[Provider-created and runtime-managed resources](../runtime-managed-resources.md)
for the source, management, visibility, ownership, and cleanup rules.

## Provider Parity

A new host provider should define:

- a resource type or configured host provider that can produce a
  `ContainerHostDescriptor`
- non-secret resource attributes for durable host facts
- private handling for credentials and transport details
- capability markers for image, build, and filesystem mount support where
  applicable
- resolver diagnostics for missing host, unavailable host, missing capability,
  and unavailable credentials
- provider-owned runtime integration for materializing containers, helper
  services, logs, monitoring, cleanup, and storage mounts
- Resource Manager projection that keeps host placement understandable without
  exposing secrets

Do not require Docker-compatible operations for every host provider. A future
Podman, Kubernetes, systemd, or appliance-backed host can satisfy the generic
placement contract while exposing provider-specific runtime behavior behind
its own adapter.

## Launcher And Language Parity

Launchers and language SDKs should preserve the same authoring model:

- emit `ResourceDefinition`/`ResourceTemplate` shapes, not runtime container
  commands
- use the default host only when the graph needs container-backed work
- expose explicit host selection when a resource supports placement
- keep registry credentials and host credentials out of serialized templates
- call the Control Plane or CLI apply path instead of bypassing Resource
  Manager host resolution

C#, TypeScript/JavaScript, Java, and future language launchers should converge
on the same resource model shape even if their fluent builder names differ.

## Known Gaps

- The generic container-host provider is currently a supporting graph resource
  and descriptor bridge, not a full standalone host runtime implementation.
- Docker is the first concrete runtime path. Remote Docker host support remains
  partially implemented and tracked separately.
- Rich host readiness diagnostics for provider-specific conditions, such as
  unsupported ingress, unavailable volume materialization, or runtime-specific
  credential brokers, still need more provider work.
- Host-oriented naming is mostly in place, but some older container-host or
  engine-oriented names remain in compatibility paths.
