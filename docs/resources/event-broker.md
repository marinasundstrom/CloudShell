# Event Broker

Event Broker is CloudShell's recommended resource model concept for managing
events inside a CloudShell environment. It transports facts about what has
happened and can retain those facts for later query. It is intentionally
separate from Device Registry and from future operation queues or service-bus
resources.

The broker model gives CloudShell a consistent managed-event abstraction
without copying one provider's historical product boundaries. A broker can
support protocols and providers such as MQTT, HTTP, AMQP, Kafka, Azure Event
Hubs, or NATS while the resource graph continues to describe the same portable
intent: event endpoints, retained streams, capabilities, ownership, and access.

Applications and devices are not required to use a CloudShell Event Broker.
They may still connect to custom brokers, device-specific endpoints, or
provider-native event services when that is the right fit. Event Broker is the
portable CloudShell-managed path when an environment wants events to be visible,
queryable, and governable as part of the resource graph.

## Modeling Boundary

An Event Broker transports events. Examples include telemetry, sensor readings,
device check-ins, lifecycle events, audit events, and other facts that may be
distributed to interested consumers.

An Event Broker does not own device identity, desired state, reported state,
device lifecycle, or last-seen calculations. Those remain Device Registry
responsibilities. It also does not own command or work delivery. Operations
such as install firmware, rotate credentials, restart a device, or provision a
certificate require intended recipients, acknowledgement, retries, and
completion tracking; those belong to a future Service Bus or operation-queue
resource model.

The boundary is:

| Resource | Responsibility |
| --- | --- |
| Device Registry | Device identity, enrollment, trust, desired/reported state, presence, and lifecycle. |
| Event Broker | Transporting event facts through one or more protocols. |
| Service Bus or operation queue | Delivering work or commands to intended recipients with completion semantics. |
| Observability | Storing, querying, correlating, and visualizing logs, traces, metrics, and telemetry. |

Event Broker can feed observability, but it is not itself the telemetry store.
Future telemetry ingestion and OpenTelemetry bridges should use the broker as a
transport option while keeping CloudShell's global observability model as the
query and correlation surface.

## Resource Shape

The built-in resource type is `event.broker` with provider ID `event.broker`
and class `service`.

Authoring attributes:

| Attribute | Purpose |
| --- | --- |
| `kind` | Broker kind. Defaults to `broker`. |
| `protocols` | Protocol endpoints exposed by the broker. |

Each protocol endpoint contains:

| Field | Purpose |
| --- | --- |
| `name` | Stable endpoint name, such as `mqtt` or `http`. |
| `protocol` | Protocol family, such as `mqtt`, `http`, `amqp`, `kafka`, `eventhubs`, or `nats`. |
| `endpoint` | Non-secret broker endpoint address. |
| `eventFormat` | Optional payload format hint, such as `json`. |
| `capabilities` | Optional protocol capabilities such as `events.publish`, `events.subscribe`, `events.retained`, or `telemetry.ingest`. |

Broker credentials, provider connection strings, and secrets must not be
projected through resource attributes. A provider-backed broker should keep
those values behind provider-owned configuration, resource identity grants, or
Secrets Vault references.

The built-in HTTP retained event log is protected by CloudShell bearer tokens.
Callers must present a token whose principal carries a resource permission claim
for the target broker:

| Permission | Purpose |
| --- | --- |
| `EventBrokerResourceOperationPermissions.PublishEvents` | Append events to retained streams. |
| `EventBrokerResourceOperationPermissions.ReadEvents` | List retained streams and read retained events. |

## Programmatic Authoring

```csharp
var events = builder
    .AddEventBroker("events")
    .WithDisplayName("Factory Event Broker")
    .WithMqttEndpoint(
        "mqtt://localhost:1883",
        capabilities:
        [
            EventBrokerProtocolCapabilities.PublishEvents,
            EventBrokerProtocolCapabilities.SubscribeEvents,
            EventBrokerProtocolCapabilities.TelemetryIngestion
        ])
    .WithHttpEndpoint("http://localhost:7180/events");
```

The current builder records protocol endpoints as resource attributes,
projects them into Resource Manager endpoint surfaces, and uses the HTTP
endpoint as the built-in local Event Broker service endpoint. Convenience
helpers exist for MQTT, HTTP, AMQP, Kafka, Azure Event Hubs, and NATS.

## Event Log

The built-in HTTP broker is a retained event log. Publishers append immutable
events to named streams. Consumers can list streams and query retained events
from a sequence number.

The HTTP MVP exposes:

| Route | Purpose |
| --- | --- |
| `GET /api/events/brokers/{brokerId}/streams` | List retained streams for a broker. |
| `GET /api/events/brokers/{brokerId}/streams/{stream}/events?fromSequence=0&limit=100` | Read retained events after a sequence number. |
| `POST /api/events/brokers/{brokerId}/streams/{stream}/events` | Append an event to a stream. |

All retained event routes require `Authorization: Bearer <token>`. The service
returns unauthorized when no bearer token is present and hides missing or
unauthorized broker resources behind the same not-found response.

The C# `EventBrokerClient` is separate from Device Registry clients. A device
application may opt into publishing device data or check-ins through
`EventBrokerClient`, but Device Registry remains the owner of device identity,
enrollment, presence, and twin state. Device-specific clients can still expose
their own custom broker or endpoint integrations outside this CloudShell-managed
event path.

The token-aware `EventBrokerClient` constructors accept a
`CloudShellResourceCredential` and request the standard `ControlPlane.Access`
scope before calling the protected HTTP service.

## Resource Manager

The built-in Resource Manager UI contributes an Event Broker resource type and
a read-only **Streams** tab under the General section. The tab shows declared
protocol endpoints and retained stream summaries from the running HTTP broker
service.

When reading retained stream summaries, Resource Manager creates a short-lived
built-in bearer token scoped to
`EventBrokerResourceOperationPermissions.ReadEvents` for the selected broker
after the current user passes CloudShell authorization.

There is no specialized subscription, topic-routing, telemetry, or
access-management UI in this first slice.

## Runtime Boundary

The built-in local runtime starts:

```text
CloudShell.EventBrokerService/CloudShell.EventBrokerService.csproj
```

The service receives:

- a broker definition file path through
  `CloudShell:EventBrokerService:DefinitionsPath`
- the scoped broker resource ID through
  `CloudShell:EventBrokerService:ResourceId`
- an optional retained event store path through
  `CloudShell:EventBrokerService:EventsPath`
- built-in authority or service bearer settings through the shared
  `Authentication:*` configuration used by other protected CloudShell runtime
  services

When `EventsPath` is omitted, the service stores retained events next to the
broker definition file using a `.events.json` sidecar. This is the MVP
database boundary for broker-owned retained events.

The built-in runtime does not connect to external brokers, bridge Device
Registry MQTT messages, ingest telemetry into CloudShell observability, or
deliver commands.

Future provider implementations can map the same resource shape to concrete
brokers, including local Mosquitto, RabbitMQ MQTT plugins, Kafka, NATS, Azure
Event Hubs, or other event systems. Provider-native topology and credentials
remain behind provider contracts until CloudShell deliberately models a
portable subset.

## Known Gaps

- Device Registry still owns its experimental embedded MQTT endpoint; it is not
  bridged through Event Broker in this slice.
- Broker-native permission reconciliation is future work. The built-in HTTP
  retained event log already enforces CloudShell resource permission claims.
- Event subscriptions, topic routing, dead-letter handling, retention policy
  controls, and provider-native topology inspection are future work.
- Service Bus or operation queue resources for command/work delivery are
  future work and should remain separate from Event Broker.
- Telemetry ingestion and OpenTelemetry integration should be designed as a
  follow-up that connects Event Broker with CloudShell observability without
  making the broker the telemetry store.
