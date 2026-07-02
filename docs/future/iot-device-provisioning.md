# IoT Device Provisioning Future Direction

Status: Deferred strategic direction, not an active proposal.

CloudShell should eventually have an IoT story that fits the same resource,
identity, access, networking, and observability model used by applications and
infrastructure resources. The goal is not to copy a public-cloud service
catalog. The goal is to let device workloads securely join a CloudShell
environment, become manageable resources, and use already-registered services
through provider-neutral CloudShell concepts.

This direction is post-MVP and likely post-initial-on-premise. It should not
displace the local-development MVP, Application Topology confidence, Resource
Manager stabilization work, or the first on-premise control-plane proof.

## Problem

IoT and edge workloads need a bootstrap path that is different from normal
application deployment:

- A device may be manufactured, imaged, or installed before it is known to a
  specific CloudShell environment.
- The device needs a way to authenticate its first contact with the
  environment, commonly through a certificate, enrollment token, hardware
  identity, or another pre-issued credential.
- Once trusted, the device should be registered into the environment catalog
  so CloudShell can inspect, authorize, monitor, and manage it.
- Applications running on the device may need access to CloudShell-managed
  services such as configuration, secrets, databases, message brokers,
  telemetry endpoints, or application APIs.

CloudShell should make this possible without forcing users to learn the
product taxonomy of Azure IoT Hub, AWS IoT, Kubernetes, or any one
broker/runtime.

## Developer experience goal

IoT programming is often hard because provisioning, device identity, service
access, telemetry, and operational state are spread across several tools and
provider-specific concepts. CloudShell should make local and team-owned IoT
development easier by using Resource Manager as the cockpit for device
resources too.

Even an early development-oriented implementation should let a developer see:

- whether a simulated or physical device has been provisioned;
- which principal the device or device workload is using;
- which CloudShell-managed services the device can access;
- which configuration, secrets, endpoints, or broker resources the device
  depends on;
- recent device activity, health, logs, traces, metrics, or telemetry;
- why a device cannot provision, connect, authenticate, or access a service.

This keeps the IoT story consistent with the local distributed-application
flow: program the workload, run it, inspect its resources and relationships,
diagnose failures, and only then decide which pieces need durable environment
state or deployment-specific providers.

## Direction

Model connected devices as resources or resource-owned children in the
CloudShell graph. A device provisioning capability should accept a bootstrap
request from a device, validate the presented credential through a configured
provisioning provider, and create or reconcile the matching device resource.

The initial concepts are:

- **Device resource**: a managed CloudShell resource that represents the
  physical or virtual device. It can have identity, health, telemetry,
  endpoint, relationship, and activity projections like other resources.
- **Device workload**: an application or service running on the device. This
  may later be projected as a child resource, a workload descriptor, or
  provider-owned runtime state depending on the device runtime model.
- **Device enrollment**: desired or observed onboarding state for a device or
  device class. Enrollment should be provider-owned when the backing authority
  owns certificates, hardware roots, or bootstrap policies.
- **Enrollment policy**: the rule that decides which devices may request
  provisioning. A policy may allow known devices by enrollment evidence such as
  a MAC address, serial number, declared device ID, pre-issued token, or
  certificate, place unknown devices into a pending approval queue, or allow
  trusted devices that have already been approved by a local gateway.
- **Enrollment evidence**: the expected facts CloudShell or the provider can
  use to match a provisioning request to a device that should be allowed to
  join. Evidence can include MAC address, serial number, device class,
  enrollment group, or installer-provided metadata. Evidence helps identify
  what the device claims to be; it is not the cryptographic proof by itself.
- **Device proof**: the credential or challenge response that proves the device
  is allowed to claim the enrollment. This may be a certificate, private-key
  signature, signed enrollment token, hardware-backed attestation, trusted
  gateway assertion, or another provider-owned mechanism.
- **Device gateway**: the CloudShell-managed entry point that devices connect
  to when joining an environment. The name is intentionally provisional. It is
  the CloudShell equivalent of the environment's device connection boundary,
  not a commitment to clone Azure IoT Hub.
- **Provisioning provider**: a capability that validates device bootstrap
  credentials, registers or reconciles device resources, and returns the
  information the device needs to connect to environment services.
- **Device principal**: the principal used by the device, or by a workload on
  the device, when it accesses CloudShell-managed services. This is analogous
  to an application or resource identity: after enrollment, CloudShell or the
  provider binds the device resource to a principal and the device obtains a
  token or credential it can use to access granted services. This should align
  with the identity and access model instead of becoming a separate device-only
  authorization system.

## Provisioning flow

A future provisioning flow should be able to work like this:

1. A device starts with a pre-issued credential, such as a certificate.
2. The device connects to the CloudShell environment's device provisioning
   endpoint.
3. The provisioning provider validates the presented credential and determines
   whether the device is allowed to join.
4. The provider matches enrollment evidence, such as expected MAC address,
   serial number, device ID, or enrollment group, with cryptographic proof such
   as a certificate, signed token, private-key signature, or trusted gateway
   assertion.
5. The Control Plane creates or reconciles a device resource, its principal,
   default access grants, and initial resource relationships.
6. The device receives the token, credential, or connection metadata it needs
   to use allowed services and publish telemetry.
7. Resource Manager shows the device in the resource graph with health,
   activity, telemetry, and service relationships.

This flow should be declarative where possible. The Control Plane should own
the resource graph, access grants, and audit trail. The provisioning provider
owns credential validation, external registration, and provider-specific
device state.

## Enrollment policies

The development flow should support both known and unknown devices:

- A known-device policy can start from identifiers the developer already has,
  such as MAC addresses, serial numbers, device IDs, or certificates. These
  identifiers help match a provisioning request to an expected device.
- An unknown-device policy can let a device request provisioning and then show
  the request in Resource Manager as pending approval.
- A local-gateway policy can allow devices that were already approved by a
  trusted local device gateway to connect to the environment's device gateway.

MAC addresses are useful for local development, discovery, and enrollment
matching because they can say "we expect this device to be this device." They
are not strong proof of identity. A production-grade provider should pair
enrollment evidence with stronger proof such as certificates, signed tokens,
private-key signatures, hardware-backed identity, or a trusted gateway
assertion.

Resource Manager should make the policy visible enough that a developer can
understand why a device was accepted, rejected, or placed into pending
approval. The approval operation should create or reconcile the device
resource, assign or bind the device principal, and apply the allowed service
grants. The resulting principal should be the identity that device workloads
use when they request tokens or credentials for CloudShell-managed services.

## Resource model alignment

IoT devices should use existing CloudShell concepts first:

- Resource identity for the device or device workload principal.
- Access grants for service access.
- Health checks and telemetry for observed device state.
- Resource events and activity for provisioning, reconnects, access decisions,
  and management operations.
- Endpoint mappings only when the device exposes reachable services.
- Provider-owned attributes for stable non-secret facts such as model,
  firmware version, device class, or enrollment group.

CloudShell should avoid projecting secrets, private keys, or bootstrap
credentials into resources, logs, diagnostics, or UI fields.

## Relationship to existing services

Device provisioning should let devices use existing CloudShell services rather
than requiring a separate IoT-only service catalog. For example, a device
workload might receive configuration from Configuration Store, read secrets
through the Secrets Vault, connect to a database, publish telemetry, or call an
application endpoint through normal resource permissions.

Message brokers are likely important for real IoT workloads, but they should
enter CloudShell as resources with endpoints, identity, access, observability,
and provider behavior rather than as hardcoded IoT-only infrastructure.

## Open questions

- Is the first device resource a new `ResourceClass`, a service/infrastructure
  resource with a specific type, or a capability projected on an existing
  resource shape?
- What should CloudShell call the environment-local device connection boundary:
  device gateway, device hub, device broker, or something else?
- Should device workloads be explicit child resources, runtime-managed
  provider projections, or a provider-owned descriptor on the device resource?
- Which bootstrap credentials should the built-in development provider support
  first: certificates, enrollment tokens, or signed one-time codes?
- How should known-device identifiers such as MAC addresses be represented so
  they help enrollment without becoming the device's security credential?
- How should offline devices, intermittent connectivity, and delayed telemetry
  affect lifecycle state versus health state?
- Which management operations belong in MVP-like local development examples:
  register, disable, revoke, rotate credential, inspect telemetry, or push
  configuration?

## Deferred

- A full IoT Hub equivalent.
- A device-management portal with firmware updates, twin state, fleet jobs, or
  broker-specific routing.
- Provider-specific integrations for Azure IoT Hub, AWS IoT, MQTT brokers, or
  Kubernetes edge runtimes.
- Dedicated device simulators beyond focused samples that prove provisioning,
  service access, and observability.
