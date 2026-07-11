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
    .WithVirtualHost("my_vhost")
    .MountVolume(brokerData, RabbitMQResourceDefaults.DataPath);
```

The local Docker runtime uses `rabbitmq:3-management` by default so the
management endpoint is available for simple and advanced broker management.
Future providers can map the same resource shape to other RabbitMQ runtimes or
managed broker environments behind the provider boundary.

RabbitMQ `user.*` and `vhost` settings are provider-owned broker
configuration, not CloudShell Resource Manager identity metadata. Omitting
`user` leaves the runtime/default image credentials in effect. Omitting
`vhost` leaves the RabbitMQ default virtual host (`/`) in effect. Templates can
set `user.managed: true` to ask the provider to derive CloudShell-owned
bootstrap credentials for the broker resource; that mode cannot be combined
with explicit `user.username` or `user.password`. The local development
runtime stores generated bootstrap credentials in provider-owned local state
under the host content root so they survive a host process restart while the
broker container is still running, and removes them when CloudShell stops or
restarts the managed container.

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
    user:
      username: guest
      password: guest
    vhost: my_vhost
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
configuration. The local runtime leaves the Docker image defaults in effect
when no resource or host credential settings are supplied. A RabbitMQ resource
can declare explicit `user.username` and `user.password`, or declare
`user.managed: true` to let CloudShell generate provider-owned bootstrap
credentials for the running broker instance. Host configuration can still
provide runtime defaults for hosts that want a shared local setting. Password
attributes are not projected as generated Resource Manager attributes, logs, or
diagnostics, and managed bootstrap credentials remain provider-owned runtime
state. For the local development host, generated bootstrap credentials are
stored in a provider-owned file below `Data/cloudshell/rabbitmq` and deleted
when the RabbitMQ resource is stopped or restarted by CloudShell.

Registering `AddLocalRabbitMQDockerRuntime(...)` also enables the
RabbitMQ Management API access reconciler and topology reader. The reconciler
uses the resolved `management` endpoint and provider-owned administrator
credentials to materialize CloudShell resource-identity grants as
RabbitMQ-native users and virtual-host permissions. The topology reader uses
the same provider-owned credential boundary and reads broker-native queues and
exchanges for the resource virtual host when `vhost` is declared, otherwise
the host/default virtual host.

The RabbitMQ provider exposes a Control Plane credential endpoint at
`/api/rabbitmq/v1/credentials`. A workload presents its CloudShell resource
identity token, requests credentials for a target RabbitMQ resource and broker
permission, and the provider checks the token's resource-permission claims
against declared RabbitMQ grants. When authorized, CloudShell reconciles the
matching RabbitMQ-native user and virtual-host permissions if needed, records
the credential request as a resource event, and returns the username, password,
and virtual host needed by the native RabbitMQ client. Workloads must treat the
returned password as issued access material rather than static configuration;
broker bootstrap credentials remain provider-owned and are not returned through
this endpoint.

Hosts can register the same management API integration directly with
`AddRabbitMQManagementApiAccessReconciler(...)` when a non-Docker runtime
exposes the RabbitMQ management API.

## Workload Credential Resolution

Applications should not receive the RabbitMQ bootstrap administrator username
or password. When a resource identity has RabbitMQ grants, the application
uses its CloudShell resource identity credential to request short-lived
workload credentials from CloudShell, then passes the returned username,
password, and virtual host to its normal RabbitMQ client.

The launcher declares the identity and grants:

```csharp
const string identityProviderId = "identity:built-in";

var broker = resources
    .AddRabbitMQ("rabbitmq")
    .WithCloudShellManagedUser()
    .WithVirtualHost("cloudshell_sample");

var api = resources
    .AddDotnetProject("api", apiProjectPath)
    .WithIdentity(identityProviderId, name: "api")
    .ProvisionIdentityOnStartup()
    .WithReference(broker)
    .WithEnvironmentVariable("RabbitMQ__Authentication", "CloudShell")
    .WithEnvironmentVariable("RabbitMQ__CredentialEndpoint", "http://127.0.0.1:5112/api/rabbitmq/v1/credentials")
    .WithEnvironmentVariable("RabbitMQ__ResourceName", broker.EffectiveResourceId)
    .WithEnvironmentVariable("RabbitMQ__CredentialPermission", RabbitMQResourceOperationPermissions.Configure);

broker.Allow(api.Principal("api", providerId: identityProviderId), RabbitMQResourceOperationPermissions.Configure);
broker.Allow(api.Principal("api", providerId: identityProviderId), RabbitMQResourceOperationPermissions.Publish);
broker.Allow(api.Principal("api", providerId: identityProviderId), RabbitMQResourceOperationPermissions.Consume);
```

At runtime, the application requests a CloudShell token for the permission it
needs and posts it to the credential endpoint:

```http
POST /api/rabbitmq/v1/credentials
Authorization: Bearer <CloudShell resource identity token>
Content-Type: application/json

{
  "rabbitMQResourceName": "application.rabbitmq:rabbitmq",
  "permission": "CloudShell.Messaging/rabbitMQ/configure/action"
}
```

CloudShell returns only the broker-native workload identity material:

```json
{
  "username": "cloudshell-...",
  "password": "...",
  "virtualHost": "cloudshell_sample",
  "expiresOn": null
}
```

A .NET workload should use `CloudShell.RabbitMQ.Client` so the credential
exchange stays behind a RabbitMQ-specific client helper:

```csharp
using CloudShell.RabbitMQ.Client;

builder.Services.AddCloudShellRabbitMQClient(options =>
{
    options.CredentialEndpoint = new Uri("http://127.0.0.1:5112/api/rabbitmq/v1/credentials");
    options.RabbitMQResourceName = "application.rabbitmq:rabbitmq";
    options.HostName = "localhost";
    options.Port = 5672;
    options.Permission = CloudShellRabbitMQPermissions.Configure;
});

var factory = await rabbitMQ.CreateConnectionFactoryAsync(cancellationToken);
using var connection = factory.CreateConnection("orders-api");
```

The .NET client reads `CLOUDSHELL_RABBITMQ_CREDENTIAL_ENDPOINT` by default
when no endpoint is configured explicitly. It requests a CloudShell resource
identity token for the RabbitMQ permission being requested, calls the
credential endpoint, and applies the returned username, password, and virtual
host to the normal `RabbitMQ.Client` connection factory.

The Java sample follows the same protocol directly: it reads
`CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT`, `CLOUDSHELL_IDENTITY_CLIENT_ID`, and
`CLOUDSHELL_IDENTITY_CLIENT_SECRET`, requests a CloudShell token, calls
`/api/rabbitmq/v1/credentials`, then passes the returned values to the
RabbitMQ Java client.

Credential resolution fails with `401` or `403` when the token is missing,
invalid, lacks the requested RabbitMQ permission claim, or no matching
CloudShell grant is declared for the resource identity. It can also fail with
`400` when the target RabbitMQ resource or requested broker permission is
invalid. Successful requests are recorded as RabbitMQ credential resource
events for traceability.

```csharp
public sealed record RabbitMQCredential(
    string Username,
    string Password,
    string VirtualHost,
    DateTimeOffset? ExpiresOn);
```

### Persistent Connections And Rotation

Credential resolution is not meant to happen for every message. Resolve
RabbitMQ credentials when creating the native AMQP connection, keep the
connection open using the normal RabbitMQ client connection/channel model, and
resolve credentials again only when the application needs a new connection or
the returned `expiresOn` value says the credential should be refreshed soon.

The current local provider returns deterministic RabbitMQ-native credentials
for the resource identity and usually leaves `expiresOn` unset. Future
providers may return expiring credentials, rotate generated passwords, or
support a managed broker that invalidates credentials on a schedule. Workloads
should therefore follow this pattern even when local development credentials
appear stable:

1. Use `DefaultCloudShellResourceCredential` to request a CloudShell token for
   the RabbitMQ permission needed by this process.
2. Call `/api/rabbitmq/v1/credentials`.
3. Open the RabbitMQ connection with the returned username, password, and
   virtual host.
4. Reuse that connection and its channels for normal publishing and consuming.
5. If `expiresOn` is present, schedule a reconnect before expiry with a small
   safety window.
6. If the broker closes the connection, the connection cannot authenticate, or
   the client receives an access-refused condition, request credentials again
   before reconnecting.

Do not store the returned password in application configuration, resource
attributes, logs, health output, metrics, traces, or user-facing diagnostics.
It is provider-issued access material, not a durable secret owned by the
application.

## Current Resource Manager UI

Resource Manager registers RabbitMQ type metadata, AMQP and management endpoint
descriptors, generated details, Configuration, Environment, Storage,
Endpoints, Logs, Activity, and other standard resource tabs. It also
contributes a focused **Broker** tab that summarizes broker state, projected
endpoint contracts, resolved AMQP and management addresses, access
reconciliation availability, and a link to the RabbitMQ management UI when the
management endpoint is mapped.

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

RabbitMQ access uses the generated **Access control** and **Identity** tabs
rather than a separate broker-specific access management surface. Access
control records CloudShell grants such as publish, consume, and configure, and
provider status maps those grants to RabbitMQ virtual-host permissions
(`write`, `read`, and `configure`). The Identity tab remains the place to
inspect the resource identities that receive or hold those grants. Resource
Manager does not show the broker administrator username/password or generated
managed-user passwords.

The management endpoint remains the supported path for broker-native
configuration until those workflows are deliberately modeled in CloudShell.

## Logs

RabbitMQ resources declare a default container log source named **Container
logs**. For the local Docker runtime, CloudShell reads that source from the
provider-owned RabbitMQ container using the container runtime log command with
timestamps enabled. The Logs tab shows broker stdout and stderr so operators
can inspect startup, plugin, persistence, clustering, and broker error output
without opening the container directly.

Container logs are provider operational output. They complement the
platform-owned Activity stream, which records CloudShell lifecycle and
management operations against the resource.

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
- No Java, TypeScript, or service-discovery helper for RabbitMQ workload
  clients yet. The .NET `CloudShell.RabbitMQ.Client` package covers the first
  native-client helper path.
- Queues, exchanges, and bindings are visible through the read-only broker
  topology tab, but they are not projected as CloudShell child resources and
  virtual hosts, users, and policies are not surfaced yet.
- The local Docker runtime exposes broker container stdout/stderr through the
  generic Logs tab. File-based RabbitMQ log paths and broker-native log
  configuration are not modeled yet.
- RabbitMQ permission grants can be reconciled and inspected through the
  Management API. Resource Manager shows requested and effective state through
  the generated Access control and Identity tabs; broker-native user
  administration remains in RabbitMQ.
- No RabbitMQ-specific audit/event schema beyond standard resource actions and
  reconciliation diagnostics yet.
- No cluster or non-local RabbitMQ runtime provider yet.
