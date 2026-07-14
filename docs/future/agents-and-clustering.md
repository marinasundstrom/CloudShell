# CloudShell Agents and Clustering Future Direction

## Status

Deferred strategic direction with a narrow MVP-aligned entry point.

CloudShell should eventually support shared, clustered, and regionally
distributed environments. That requires more than splitting the UI from the
Control Plane. CloudShell needs a durable way to run platform work close to
the hosts, networks, storage systems, and services being managed while keeping
one coherent resource graph, authorization boundary, and operational history.

This document proposes introducing CloudShell agents as the runtime
participants that make that scale-out model explicit. An agent is not an AI
assistant and should not become one large platform object. It is a deployable
CloudShell process role that hosts smaller capability-specific components,
registers those capabilities with the Control Plane, receives typed
assignments, reconciles local state on the machine where it runs, and reports
observed state back through Control Plane-owned contracts.

The full clustering direction is not MVP scope. The MVP-relevant part is the
agent and reconciliation foundation: the Control Plane owns desired state and
typed assignments; agents execute and observe local work; reconciliation
compares desired and observed state and decides what assignment should happen
next.

In practical terms, CloudShell is pushing execution and observation down into
agents instead of letting the Control Plane directly perform host-local
actions. The Control Plane still decides what should happen, records desired
state, enforces identity and policy, and audits the result. The agent runs the
provider-side operation on the machine where the relevant capability exists.

## Purpose

The purpose is to define the direction for scaling both CloudShell itself and
the services running in CloudShell-managed environments.

CloudShell scale-out includes:

- multiple Control Plane API replicas behind a load balancer
- controller duties coordinated through leases
- independent workers for logs, telemetry, health, notifications, and
  provider reconciliation
- agents running near managed hosts in one region first, then in additional
  regions or data-center-like boundaries as the environment grows
- workload placement across hosts, agent pools, and regions
- service routing and load distribution for CloudShell-managed services

The direction should help current MVP work avoid single-process assumptions
without pulling clustering into the MVP scope.

For MVP purposes, this means proving the agent execution boundary with one
boring vertical slice before adding scheduling, regions, multi-host replica
placement, or load distribution.

## Problem

CloudShell currently has a clear split between the shell, Control Plane,
Resource Manager, providers, and orchestrators. That split is sufficient for
local development and simple on-premise deployments, but clustered and
multi-host environments introduce new requirements:

- the Control Plane should be horizontally scalable without every API replica
  running the same background loops
- provider actions must execute near the host, cluster, network, or storage
  boundary that owns the operational capability
- workload placement needs to consider host capacity, platform capability,
  region, data locality, network reachability, and failure domains
- logs, telemetry, health polling, and reconciliation should not duplicate
  work across replicas
- service routing must be able to point to one or more healthy workload
  instances across hosts or regions
- disconnected, partitioned, or degraded hosts need explicit leases,
  heartbeats, and recovery behavior

Without an agent concept, CloudShell either keeps assuming that the Control
Plane process can directly reach every runtime host or invents provider-local
worker models that are difficult to compose, authorize, observe, and operate.

## Proposed Direction

Introduce a CloudShell agent role.

A CloudShell agent is a deployable process that belongs to one CloudShell
environment authority. It registers with the Control Plane, advertises
capabilities, receives or claims typed assignments, executes
capability-owned components on the machine where it runs, and reports results,
diagnostics, observed state, and heartbeats.

Agents should be capability hosts, not monolithic platform abstractions.
Different deployments can run different agent components based on operating
system, distribution, installed tools, network access, provider packages, and
environment policy.

Examples:

- a local development agent that runs in the same process as the combined host
- a normal shared environment with one region and one or more agents across a
  small set of machines
- a data-center environment with many agents grouped by region, zone, rack,
  hardware profile, network boundary, and provider capability
- a Linux host agent with Docker, Podman, systemd, filesystem, and log-reader
  capabilities
- a Windows host agent with Windows service, process, filesystem, and log
  capabilities
- a regional agent pool that can place container applications near a specific
  network or storage boundary
- a provider adapter agent that can communicate with a vendor appliance,
  Kubernetes cluster, Nomad cluster, or private cloud API

The Control Plane remains the authority for accepted resource state,
authorization, operation history, and API projection. Agents execute delegated
work but do not own the resource graph.

Resource Manager remains logically centralized. It should stop being the
component that directly reaches every runtime target, but it remains the
canonical entry point for operations such as creating resources, deploying
revisions, restarting resources, and deleting resources. Those operations
should become desired-state changes and assignments instead of long
synchronous calls into Docker, Traefik, or another runtime.

## Core Concepts

### Environment Authority

An environment authority is one Control Plane ownership boundary. It owns
resource state, authorization, provider registrations, logs, traces, and
operation history for an environment.

A clustered CloudShell deployment can have several Control Plane API replicas,
controllers, workers, and agents while still representing one environment
authority.

A federated deployment is different: it has multiple environment authorities.
Federation should remain a separate future direction.

### Normal Environment Shape

The normal shared deployment should not require multi-region clustering.

The expected baseline is:

- one environment authority
- one logical region
- one Control Plane deployment, possibly with more than one API replica later
- one or more agents across one or more machines, often just one or a few
- one default agent pool for general workload placement
- provider-owned routing for the services that need an endpoint

This shape is enough for a team-owned CloudShell environment that has several
machines but no regional topology. The model should scale from here by adding
more agents, then by introducing additional regions or data-center-like
placement boundaries when the environment actually needs them.

### Regional and Data Center Shape

A larger CloudShell environment should be able to scale out by adding more
agents.

In a data-center or private-cloud deployment, regions should loosely
correspond to physical or operational placement boundaries such as data
centers, sites, rooms, availability zones, or private-cloud regions. Each
region can contain many machines, and those machines can run many agents.

The expected shape is:

- one environment authority for the managed environment
- one or more regions, often aligned to data centers or similar operational
  boundaries
- many agents across many machines within each region
- agent pools grouped by region, zone, rack, hardware profile, network
  boundary, operating system, provider capability, or trust scope
- Control Plane API replicas behind a load balancer
- one or more controller roles coordinated through durable leases
- worker pools for logs, telemetry, health, notifications, and reconciliation
- placement policies that can spread or concentrate managed services based on
  capacity, failure domain, data locality, and service intent
- routing providers that can build backend pools from healthy placed workload
  instances

This shape should not require a different resource model from the normal
shared environment. It should use the same agent registration, capability,
work assignment, heartbeat, placement, and provider contracts at larger scale.

Adding agents should increase the amount of work CloudShell can perform and
the number of hosts it can manage inside a region. Adding regions should
increase the placement and failure-domain choices available to CloudShell.
Bigger clustering scenarios should be enabled by policy and provider
capability rather than by making every deployment clustered by default.

### Agent

An agent is a registered runtime participant within one environment authority.
It has identity, version, labels, health, supported capabilities, and an
operational scope.

Agents should report:

- agent id and display name
- version and protocol compatibility
- operating system and host platform facts
- labels such as region, zone, rack, environment, or owner
- capabilities and capability versions
- heartbeat and observed health
- drain, disabled, or maintenance state

Agents must not expose credentials, connection strings, or provider-owned
secret material through resource details, diagnostics, or logs.

### Capability Component

A capability component is the smaller unit hosted by an agent. It owns a
specific operational surface, such as:

- container runtime operations
- process and service operations
- host filesystem operations
- endpoint and port mapping
- log source reading
- telemetry collection
- health probing
- provider reconciliation
- storage mount management
- network route materialization

This keeps the platform abstraction layer composable. CloudShell should
resolve components by capability, platform, distribution, installed tools,
policy, and target scope rather than routing all platform behavior through one
large object.

### Agent Pool

An agent pool is a scheduling and operations boundary for agents that share a
role, region, trust scope, or capability set. A pool can be used for placement,
draining, maintenance, and capacity reporting.

The first model can keep agent pools as Control Plane metadata. A later model
may project pools as resources if users need to inspect, authorize, or operate
them through Resource Manager.

### Region and Zone

Region and zone should start as placement labels and failure-domain metadata,
not as hardcoded cloud-provider concepts. A region may map to one data center,
one site, one private-cloud region, or another operational boundary chosen by
the environment owner.

Examples:

- `region=dev-lab`
- `region=us-east`
- `zone=rack-1`
- `zone=az-a`
- `network=private-ci`

Provider-backed environments may map these labels to cloud regions, edge
locations, datacenters, racks, Kubernetes node pools, or appliance boundaries.

### Desired and Observed State

CloudShell needs separate desired and observed state.

Desired state records what the Control Plane has accepted:

- the resource exists
- the current desired generation
- the image, command, endpoint, volume, or runtime configuration
- the requested lifecycle state
- placement or capability constraints when they exist

Observed state records what agents and providers have seen:

- the assignment generation the agent last applied
- whether the local runtime artifact exists
- whether it is running, stopped, failed, or unknown
- diagnostics and status messages
- log, health, telemetry, or readiness observations
- the last heartbeat or observation time

Reconciliation compares these views. If desired state has generation `18` and
an agent reports that it has applied generation `17`, the Control Plane knows
the report is stale and can issue or retain the assignment for generation
`18`.

### Assignment

An assignment is a durable, typed operation request owned by the Control Plane
and eligible to be claimed by one agent or worker.

Assignments are not arbitrary shell commands and should not contain an
unbounded remote execution payload. They should be versioned contracts such as
container instance reconciliation, route application, log-source observation,
or local runtime inspection.

Assignments should include:

- assignment id and idempotency key
- assignment type and schema version
- resource target and accepted resource revision
- desired generation
- required capabilities
- placement constraints
- authorization scope
- lease owner and lease expiration
- progress, result, diagnostics, and retry state

Agents should claim assignments only when their identity, scope, labels, and
capabilities match the operation. Leases and idempotency must prevent duplicate
or conflicting execution.

One logical resource can produce several assignments later. For example, a
container app could eventually produce container-instance assignments,
route assignments, and DNS assignments. The MVP should not require that full
shape; it should prove one typed local assignment first.

## Scaling CloudShell

CloudShell's own backend should scale through distinct process roles:

- API replicas serve Control Plane requests and project domain APIs.
- A primary controller owns singleton duties such as lifecycle
  reconciliation, scheduled polling, and lease-sensitive coordination.
- Workers handle independent background subsystems such as log persistence,
  telemetry ingestion, health polling, notification fan-out, and provider
  reconciliation.
- Agents execute host-local or region-local operations and report state back
  to the Control Plane.

These roles can run in one process for local development. They can also run as
separate processes in a shared or on-premise environment.

In larger environments, scaling CloudShell should mostly mean adding replicas,
workers, and agents within the same environment authority. The Control Plane
coordinates leases, placement, and operation history; agents and workers scale
the execution and observation capacity.

The required platform capabilities include:

- durable leases or leader election for singleton duties
- work queues or assignment tables for agent and worker assignments
- an outbox or event stream for reliable notifications
- idempotent operation handlers
- heartbeat and health tracking
- protocol version negotiation between agents and the Control Plane
- capacity and backlog reporting by agent, pool, region, and capability
- operational views for agent health, work backlog, and failed assignments

## Scaling Managed Services

CloudShell-managed services should scale through Resource Manager-owned
placement and provider-owned runtime materialization. Scaling does not imply
that every service, or every container app replica, must be distributed across
hosts or regions.

Resource definitions should express intent, such as:

- run this service in a region
- run this service with a replica count
- require a container runtime capability
- require access to a storage boundary
- prefer one host, one agent pool, or one region
- spread across failure domains when policy requires it
- constrain placement to a pool, rack, zone, hardware profile, or provider
  capability
- avoid colocating replicas on the same failure domain
- expose a stable endpoint
- allow or disallow cross-region failover

Resource Manager should translate that intent into placement decisions and
provider work. Providers and orchestrators then materialize runtime artifacts,
such as containers, processes, replicas, backend pools, endpoint mappings,
health probes, load-balancer bindings, DNS records, or service-discovery
entries.

The user-facing resource remains stable. For example, a container application
resource can represent a managed service whether the runtime contains one
instance, multiple replicas on one host, replicas spread across machines in
one region, or a later provider-specific multi-region topology.

Container app replica distribution should remain an explicit future decision.
The current container app contract already treats replicas as opt-in
app-owned runtime resources and keeps the stable container app as the
management boundary. This direction should preserve that shape. A requested
replica count alone should not automatically mean cross-host or cross-region
distribution until placement policy, health aggregation, ingress, storage,
identity, and data-locality semantics are defined.

## Load Distribution

Load distribution should be modeled in layers:

- placement decides where workload instances run
- health determines which instances are eligible to receive traffic
- endpoint mapping exposes stable service addresses
- routing or load-balancer providers distribute traffic to healthy backends
- DNS or name-mapping providers can publish environment-specific names

The core Control Plane should not embed one global load-balancing algorithm.
It should own the resource graph, desired state, placement policy, health
state, and API projection. Runtime-specific load distribution remains behind
provider contracts.

Initial examples:

- one host-local endpoint mapping for local development
- one backend pool across replicas on a shared Docker or Podman estate
- one regional pool per region with DNS or gateway routing between pools
- provider-owned projection to Kubernetes services, cloud load balancers, or
  appliance-specific routing

## Relationship to Platform Abstraction

Agents are one of the reasons CloudShell needs a platform abstraction layer,
but agents should not be the abstraction layer by themselves.

The platform layer should provide small, resolvable components:

- path building and normalization
- process and command invocation
- shell quoting and environment handling
- filesystem operations
- service/process management
- container runtime command planning
- network and endpoint mapping primitives
- OS and distribution capability detection
- prerequisite diagnostics

Agents host these components where work needs to run. The Control Plane and
providers select components based on target platform, capability, policy, and
scope.

## Provider Split

Providers that execute runtime behavior should eventually have two halves:

- a Control Plane component that validates resources, plans assignments,
  contributes constraints, and interprets observed state
- an agent component that reconciles typed assignments against local runtime
  state

For example, a container provider's Control Plane component can validate a
container app and produce a container-instance assignment. The agent component
can pull the image, create or update the container, inspect runtime state, and
report the observed assignment state.

This split should remain provider-owned. The core platform should define the
assignment, lease, capability, and observation contracts, but it should not
embed Docker, Podman, Kubernetes, Traefik, or operating-system-specific
behavior.

## Security Model

Agents must be explicit trust participants.

The direction should include:

- agent enrollment through a scoped token, certificate, or workload identity
- mutual authentication between agent and Control Plane
- capability-scoped authorization claims
- agent pools or scopes that constrain which resources an agent may operate
- just-in-time delivery of sensitive operation inputs
- no persistent secrets in agent resource details or logs
- audit records for registration, heartbeat, work claim, work completion, and
  administrative actions
- drain and disable-scheduling operations for maintenance

An agent should never be able to claim arbitrary work only because it can
reach the Control Plane. Capability, scope, labels, and authorization must all
match.

## Failure and Recovery

The model must handle partial failure as a normal condition.

Required behavior:

- missed heartbeats mark an agent unavailable
- unavailable agents stop receiving new work
- leases expire so another eligible agent can claim retryable work
- operation handlers are idempotent or guarded by resource revisions
- non-idempotent provider actions record enough state to recover or surface a
  clear manual action
- draining agents complete or release work before maintenance
- region loss affects only workloads whose placement and data policy allow
  failover
- stale runtime artifacts can be discovered through reconciliation

CloudShell should prefer explicit degraded states and actionable diagnostics
over pretending that a partitioned host is still healthy.

## Resource Model Projection

CloudShell does not need to expose every agent concept as a user-authored
resource at first. The initial MVP projection should be operational Control
Plane data:

- registered agents
- agent leases and health
- agent capabilities
- assignments
- observed assignment state

Later operational views can include:

- agent pools
- work backlog
- recent assignments
- placement decisions

Future resource types may be useful once the operational shape settles:

- `cloudshell.agent`
- `cloudshell.agentPool`
- `cloudshell.region`
- `cloudshell.zone`
- `cloudshell.workAssignment`

These should not be introduced until the domain contract is needed by users,
extensions, or providers. Until then, Control Plane APIs and Resource Manager
operations can expose operational views without turning every runtime
participant into an authored resource.

## MVP Implementation Slices

The first implementation should stay intentionally small. It should prove that
CloudShell can move one local runtime operation behind an agent and
reconciliation boundary without committing to clustering.

1. Define the agent terminology, trust boundary, and Control Plane ownership
   rules in the architecture and hosting docs.
2. Add operational Control Plane records for agent registration, agent lease,
   agent capability, assignment, and observed assignment state.
3. Add a local development agent mode that can run in process with the
   combined host or as a simple external process for one machine.
4. Let the agent register, advertise capabilities, renew a lease, and report
   heartbeat state.
5. Add one typed assignment contract for a low-risk local provider operation.
6. Move one Docker or container-app runtime operation behind an agent-side
   assignment handler while keeping Resource Manager as the user-facing
   operation boundary.
7. Have the agent reconcile local state for that assignment and report
   observed state, diagnostics, and applied generation.
8. Have the Control Plane derive resource status from desired state,
   assignment state, and observed state.

After that foundation is proven, follow-on slices can add:

- a second agent and simple capability filtering
- basic placement selection for one assignment type
- operational UI for agents, leases, assignments, and failed reconciliation
- a normal shared-environment sample with one logical region and two agents
- health-aware backend pool projection for one provider
- regional and data-center placement as a later scale-out feature

Each slice should preserve the current Resource Manager facade. Users should
not need to understand internal assignments to start, inspect, or diagnose a
managed service.

## MVP Impact

The full clustering direction is not an MVP feature. Agents and
reconciliation can be MVP-relevant if they are kept narrow and used to prevent
single-process provider execution from becoming permanent architecture.

The MVP shape is:

- one Control Plane environment authority
- one logical region
- one embedded or external agent
- typed assignments
- agent lease and heartbeat
- desired state, observed state, and applied generation tracking
- one provider operation reconciled through an agent-side handler

Current MVP work should only take dependencies on this direction where it
prevents single-process assumptions:

- keep Control Plane APIs as the authority for resource operations
- avoid direct UI dependencies on provider runtime internals
- keep provider execution behind provider and orchestration contracts
- use platform abstraction components for paths, command planning,
  invocation, and prerequisite diagnostics
- record enough operation state for future idempotency and worker execution
- keep logs, health, and diagnostics shaped so a worker or agent can produce
  them later

Do not build clustered scheduling, multi-host replica placement, regions,
data-center topology, cross-host networking, gateway routing, or multi-region
routing for the MVP unless a release-gating sample requires them.

## Non-Goals

- Do not replace Kubernetes, Nomad, Docker, Podman, or platform-native
  schedulers.
- Do not require every deployment to run remote agents.
- Do not make agents own the resource graph.
- Do not turn agent internals into public resource types before the contract
  is proven.
- Do not solve multi-Control Plane federation here.
- Do not define a complete multi-region database replication strategy here.
- Do not assume every provider capability can be executed on every operating
  system or Linux distribution.

## Open Questions

- Should the user-facing term be CloudShell agent, environment agent, host
  agent, or another name?
- Can one agent manage multiple hosts, or should a host-local agent be the
  default trust boundary?
- Which transport should agents use first: outbound HTTP polling, SignalR,
  gRPC streams, or provider-specific channels?
- Which persistence feature owns work claims: a generic Control Plane work
  queue, provider-specific stores, or a shared orchestration store?
- Which agent details should be resources, and which should remain
  operational Control Plane data?
- How should placement policy be authored for simple local development,
  shared on-premise environments, and provider-backed deployments?
- Should container app replicas default to same-host materialization,
  same-region spreading, explicit anti-affinity only, or provider-native
  scheduling?
- How much of the agent protocol is extension-facing in the first version?
