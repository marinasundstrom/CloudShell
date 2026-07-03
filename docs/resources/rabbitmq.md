# RabbitMQ Resources

RabbitMQ resources project as `application.rabbitmq`. They are authored as a
first-class managed service resource, while the current local-development
provider materializes the broker with the RabbitMQ management container image.
The container is a provider-owned runtime detail, not a generic container app.

For shared endpoint and storage concepts, see
[Resource model](../resource-model.md) and
[Storage and Volumes](storage-and-volumes.md).

## Modeling Boundary

RabbitMQ is modeled as a message broker service resource. CloudShell owns the
resource identity, class, dependencies, endpoint projection, lifecycle actions,
storage attachment, generated Resource Manager details, and Control Plane API
projection. RabbitMQ broker-native state, such as users, virtual hosts,
permissions, queues, exchanges, bindings, policies, federation, shovel, and
cluster settings, remains provider-owned broker configuration.

The first implementation intentionally does not duplicate the full RabbitMQ
management experience in Resource Manager. Resource Manager shows the service
resource and exposes the AMQP and management endpoints. Users can open the
RabbitMQ management UI for broker-native work until CloudShell adds focused
broker configuration tabs for the parts that should become portable
CloudShell concepts.

## Registration

The ResourceDefinition type is:

```text
application.rabbitmq
```

The provider id is:

```text
applications.rabbitmq
```

The resource projects as `ResourceClass.Service`.

Common endpoints:

| Endpoint | Protocol | Target port | Purpose |
| --- | --- | --- | --- |
| `amqp` | `tcp` | `5672` | Application AMQP connections. |
| `management` | `http` | `15672` | RabbitMQ management UI and API. |

Programmatic declarations should use the provider-owned RabbitMQ builder:

```csharp
var brokerData = resources
    .AddVolume("rabbitmq-data")
    .WithDisplayName("RabbitMQ Data");

var broker = resources
    .AddRabbitMQ("rabbitmq")
    .WithAmqpEndpoint(host: "localhost", port: 5672)
    .WithManagementEndpoint(host: "localhost", port: 15672)
    .MountVolume(brokerData, RabbitMQResourceDefaults.DataPath);
```

The local Docker runtime uses `rabbitmq:3-management` by default so the
management endpoint is available for simple and advanced broker management.
Future providers can map the same resource shape to other RabbitMQ runtimes or
managed broker environments behind the provider boundary.

`samples/RabbitMQMessaging` shows the preferred launcher shape for broker-backed
applications. A C# AppHost declares the RabbitMQ service resource, a .NET app
resource, and a Java app resource; the workload apps use their native RabbitMQ
clients and exchange fan-out events through queues bound to the same exchange.

## Resource Template

```yaml
resources:
  - type: cloudshell.volume
    name: rabbitmq-data
    storage:
      volume:
        medium: FileSystem
        accessMode: ReadWriteOnce
        persistent: true

  - type: application.rabbitmq
    name: rabbitmq
    dependsOn:
      - resourceId: cloudshell.volume:rabbitmq-data
        typeId: cloudshell.volume
    version: "3"
    endpointRequests:
      - name: amqp
        protocol: tcp
        targetPort: 5672
        port: 5672
        exposure: Local
      - name: management
        protocol: http
        targetPort: 15672
        port: 15672
        exposure: Local
    storage:
      volume:
        mounts:
          - volume: cloudshell.volume:rabbitmq-data
            targetPath: /var/lib/rabbitmq
            readOnly: false
```

## Storage

RabbitMQ has a resource-specific data mount point:

```text
/var/lib/rabbitmq
```

When a volume is mounted at that path, the local Docker runtime binds it into
the RabbitMQ container for durable broker state. If no volume is mounted, the
resource can still run as a disposable local-development broker.

Storage mappings should be treated as service configuration. Stop the resource
before adding, removing, or changing mounted storage.

## Lifecycle

RabbitMQ exposes standard `start`, `stop`, and `restart` resource actions.
The current local Docker runtime starts or removes a mapped RabbitMQ container
for configured resources. The runtime can be registered by a host with
`AddLocalRabbitMQDockerRuntime(...)`; hosts that do not register a runtime keep
the resource shape and actions but use the no-op runtime handler.
`CloudShell.LocalDevelopmentHost` registers the local Docker runtime and uses a
host-scoped deterministic container name for launcher-authored RabbitMQ
resources that do not have explicit runtime mappings.

Credentials for the local Docker bootstrap user are provider-owned runtime
configuration. The local runtime reads configured username/password values from
host configuration when supplied and does not project those values through
Resource attributes, logs, or templates.

Registering `AddLocalRabbitMQDockerRuntime(...)` also enables the
RabbitMQ Management API access reconciler and topology reader. The reconciler
uses the resolved `management` endpoint and provider-owned administrator
credentials to materialize CloudShell resource-identity grants as
RabbitMQ-native users and virtual-host permissions. The topology reader uses
the same provider-owned credential boundary and reads broker-native queues and
exchanges for the configured virtual host through the RabbitMQ Management HTTP
API.

Hosts can register the same management API integration directly with
`AddRabbitMQManagementApiAccessReconciler(...)` when a non-Docker runtime
exposes the RabbitMQ management API.

## Current Resource Manager UI

Resource Manager registers RabbitMQ type metadata, AMQP and management endpoint
descriptors, generated details, Configuration, Environment, Storage,
Endpoints, Activity, and other standard resource tabs. It also contributes a
focused **Broker** tab that summarizes broker state, projected endpoint
contracts, resolved AMQP and management addresses, access reconciliation
availability, and a link to the RabbitMQ management UI when the management
endpoint is mapped.

When a RabbitMQ Management API topology provider is registered, Resource
Manager also contributes a read-only **Topology** tab. It lists queues,
exchanges, and bindings reported by the broker for the configured virtual
host. The view is diagnostic and operational: it shows broker-native names,
durability, auto-delete/internal flags, queue state, message counts, consumer
counts, and binding source/destination relationships without creating
CloudShell child resources for those broker objects.

The Broker and Topology tabs are intentionally not a RabbitMQ-native
administration console. They do not create, update, or delete queues,
exchanges, bindings, users, virtual hosts, policies, or cluster state.

Resource Manager also contributes a focused **Broker access** tab for
identity traceability. It lists RabbitMQ publish, consume, and configure
grants assigned through the CloudShell Access control model, the mapped
broker-native account name for resource-identity principals, the RabbitMQ
permission category (`write`, `read`, or `configure`), and the provider
effective status reported by the Management API-backed grant inspector. The
view intentionally shows only non-secret broker account names and status
details. It does not show the broker administrator username/password or the
generated managed-user password.

The management endpoint remains the supported path for broker-native
configuration until those workflows are deliberately modeled in CloudShell.

## Identity, Access, And Audit

RabbitMQ resources participate in the CloudShell access model. Resource
identities can receive broker-scoped grants for:

- `RabbitMQResourceOperationPermissions.Publish`
- `RabbitMQResourceOperationPermissions.Consume`
- `RabbitMQResourceOperationPermissions.Configure`
- `RabbitMQResourceOperationPermissions.ReconcileAccess`

RabbitMQ exposes a **Reconcile access** resource operation,
`application.rabbitmq.reconcile-access`, guarded by
`RabbitMQResourceOperationPermissions.ReconcileAccess`. The operation invokes
the provider-owned `IRabbitMQAccessReconciler` seam. The default reconciler
reports an informational diagnostic and does not mutate broker state. The
Management API reconciler maps resource-identity grants to broker-native users
and vhost permissions:

| CloudShell grant | RabbitMQ permission |
| --- | --- |
| `Publish` | `write` |
| `Consume` | `read` |
| `Configure` | `configure` |

The CloudShell `ReconcileAccess` grant authorizes the CloudShell operation and
is not projected into a RabbitMQ broker permission.

RabbitMQ broker administrator credentials are a provider-owned bootstrap path
for CloudShell reconciliation and inspection. Hosts may configure those
credentials, but Resource Manager must not project the administrator username,
password, connection string, or equivalent secret material through resource
attributes, generated UI, diagnostics, logs, or templates. Normal users should
work through CloudShell resource identities, grants, grant status, and
provider-scoped audit/activity events instead of needing to know the broker
administrator account.

Traceability comes from reconciling CloudShell identities to broker-native
accounts and permissions. The RabbitMQ provider can create or update
broker-owned users for resource-identity principals, then RabbitMQ enforces
and records activity under those broker accounts. CloudShell records the
requested principal and grant, reconciliation result, and non-secret effective
status so operators can relate broker activity back to the CloudShell resource
identity without exposing the provider administrator credential.

The grant-status provider recognizes RabbitMQ grants. Without a runtime
inspector it reports broker grants as pending. When the Management API status
inspector is registered, it reads RabbitMQ virtual-host permissions and reports
broker grants as applied, drifted, not applied, failed, or unknown based on
observed broker-native state. `ReconcileAccess` is reported as a CloudShell
operation grant because it is enforced by CloudShell authorization rather than
RabbitMQ broker permissions.

Future RabbitMQ runtime providers should reconcile requested grants into
RabbitMQ-owned users, virtual-host permissions, credentials, or policy state
without projecting secret values. Runtime providers should also report
effective broker-native state so Resource Manager can distinguish requested
CloudShell grants from applied, missing, failed, or drifted broker state.

Lifecycle actions, grant reconciliation, broker configuration operations, and
provider failures should emit resource-scoped audit/activity events.
Diagnostics and events must remain non-secret and avoid dumping broker-native
credential material, connection strings, or message payloads.

## Known Gaps

- No specialized Resource Manager broker configuration UI for creating,
  updating, or deleting queues, exchanges, bindings, users, virtual hosts,
  policies, or cluster state yet.
- No RabbitMQ-specific workload client package or service-discovery helper yet.
- Queues, exchanges, and bindings are visible through the read-only broker
  topology tab, but they are not projected as CloudShell child resources and
  virtual hosts, users, and policies are not surfaced yet.
- RabbitMQ permission grants can be reconciled and inspected through the
  Management API, and Resource Manager can show the non-secret broker account
  mapping and effective status for publish, consume, and configure grants.
  Editing still uses the generic Access control tab, and broker-native user
  administration remains in RabbitMQ.
- No RabbitMQ-specific audit/event schema beyond standard resource actions and
  reconciliation diagnostics yet.
- No cluster or non-local RabbitMQ runtime provider yet.
