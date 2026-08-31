# MyServiceBus + RabbitMQ Order Fulfillment

This sample puts MyServiceBus into an order-fulfillment context and runs the
whole distributed application as CloudShell container apps. It is both a
showcase for MyServiceBus and Raven in a realistic CloudShell context and an
executable test of CloudShell's distributed-application authoring and runtime
surfaces:

- **Raven Order Intake** is an ASP.NET Core service written in Raven for .NET.
  Two replicas accept orders and use a Raven namespace-level MyServiceBus
  consumer function to observe fulfillment activity.
- **Java Fulfillment** runs three Java replicas. They share one receive
  endpoint and one outbox partition, so RabbitMQ distributes work and the
  PostgreSQL dispatcher leases are safe across replicas.
- **RabbitMQ 4.1.8** carries the cross-language `OrderActivity` contract and
  exposes its management UI.
- **PostgreSQL 17.6** stores application rows and the MyServiceBus
  transactional outboxes.
- **MyServiceBus Monitoring** collects bounded runtime and outbox dispatcher
  observations. **MyServiceBus Dashboard** visualizes the two applications,
  their replicas, endpoints, traffic, failures, and outbox activity.

The sample is pinned to `0.1.0-preview.8`, including the enhanced MyServiceBus
dashboard. The dashboard remains an independently versioned container so later
releases can be adopted without changing the CloudShell resource model.

The Raven and Java HTTP commands each commit an application event and the
final serialized MyServiceBus envelope in one PostgreSQL transaction. Their
background dispatchers publish only committed envelopes to RabbitMQ. The two
logical services use separate outbox partitions (`order-intake` and
`fulfillment`), while replicas of a service compete safely for leases inside
that service's partition.

The Raven contract uses the `TestApp` namespace because MyServiceBus preview
8's cross-language compatibility profile maps Java contracts to the shared
`TestApp:<type-name>` wire identity. That gives both implementations the same
RabbitMQ exchange and message URN even though the Java source lives in the
`OrderFulfillment.Contracts` package. The Raven source keeps the compact
positional record form:

```raven
record OrderActivity(
    EventId: string,
    OrderId: string,
    Kind: string,
    Origin: string,
    OccurredAtUtc: string)
```

## What this sample tests in CloudShell

This is also an executable CloudShell capability probe. It deliberately puts
pressure on several platform seams at once:

- mixed Raven/.NET and Java Docker builds in one application topology;
- dependency-aware startup across a broker, database, monitoring service,
  dashboard, and replicated workloads;
- service discovery and host ingress for both HTTP and TCP endpoints;
- replica declaration, load balancing, per-replica runtime resources, health,
  and logs;
- persistent local volumes for RabbitMQ and PostgreSQL; and
- useful navigation between CloudShell, the broker UI, and a product-owned
  dashboard without prematurely requiring a product-specific extension.

Friction found while running this sample should become a focused CloudShell
improvement, test, or documented limitation. The sample is therefore expected
to evolve as CloudShell's container-app and extension authoring surfaces do.

This is a local-development showcase. It intentionally uses isolated demo
credentials and PostgreSQL trust authentication inside the sample network.
Do not copy those settings into a shared or production environment.

## Prerequisites

- Docker with the daemon running.
- The .NET 11 preview SDK selected by the CloudShell repository
  `global.json`.
- Java 21 and Maven are useful for building the Java service directly; the
  container build includes both.
- `curl` for the walkthrough.

### Install the Raven SDK

The Raven container project pins `Raven.Sdk.Web/0.1.6`, so Docker and
`dotnet build` restore the compiler SDK from NuGet. Install the matching Raven
toolchain as well for `rvn`, diagnostics, editor integration, and direct local
builds.

On macOS or Linux:

```bash
curl -fsSL https://github.com/marinasundstrom/raven/releases/download/v0.1.6/install-raven.sh \
  | sh -s -- 0.1.6
export PATH="$HOME/.raven/bin:$PATH"
rvn doctor
```

On Windows PowerShell:

```powershell
$version = "0.1.6"
Invoke-WebRequest "https://github.com/marinasundstrom/raven/releases/download/v$version/install-raven.ps1" -OutFile install-raven.ps1
./install-raven.ps1 -Version $version
$env:PATH = "$HOME\.raven\bin;$env:PATH"
rvn doctor
```

The installers verify the release checksum and install under
`~/.raven/sdk/0.1.6` by default.

## Run the topology

From this directory:

```bash
chmod +x cloudshell.sh
./cloudshell.sh run
```

Keep that terminal open. In a second terminal, start the complete topology:

```bash
./cloudshell.sh start-apps
```

The helper isolates Control Plane state and data under this sample's
`.cloudshell/` directory. `start-apps` is safe to repeat when resources are
already running. To clean up, run `./cloudshell.sh stop-apps` while the host is
open, stop the foreground host with Ctrl+C, and then run
`./cloudshell.sh reset`; the last command removes the sample state and
persistent broker/database data.

The first start pulls the infrastructure and MyServiceBus monitoring images,
then builds the Raven and Java application images. Open these endpoints:

| Surface | URL |
| --- | --- |
| CloudShell | <http://127.0.0.1:5112> |
| MyServiceBus dashboard | <http://localhost:5311> |
| RabbitMQ management | <http://localhost:15679> |
| Raven order intake | <http://localhost:5401> |
| Java fulfillment | <http://localhost:5420> |

RabbitMQ's local-only sample login is `myservicebus` /
`local-development-only`.

## Exercise the use case

Place an order through the load-balanced Raven endpoint:

```bash
curl -X POST http://localhost:5401/orders \
  -H 'Content-Type: application/json' \
  -d '{"orderId":"ORDER-1001"}'
```

Then record fulfillment through the load-balanced Java endpoint:

```bash
curl -X POST http://localhost:5420/fulfill/ORDER-1001
```

Inspect what each language received:

```bash
curl http://localhost:5401/received
curl http://localhost:5420/received
```

`/received` is intentionally replica-local so the sample makes competing
consumers visible. A request through the public endpoint can therefore land on
a replica that did not consume the event. For deterministic local inspection,
query the CloudShell-assigned replica probe ports:

```bash
curl http://localhost:5501/received
curl http://localhost:5502/received
curl http://localhost:5520/received
curl http://localhost:5521/received
curl http://localhost:5522/received
```

After the two publish commands, one Java replica will show Raven's
`OrderPlaced` event and one Raven replica will show Java's `OrderFulfilled`
event. A service may also consume its own published activity because both
queues subscribe to the shared event exchange.

Repeat the commands with more order IDs. In CloudShell, inspect **Raven Order
Intake** and **Java Fulfillment** to see their declared replica counts and
runtime replica resources. In the MyServiceBus dashboard, the two application
groups show individual instances, receive endpoints, message flow, and outbox
dispatcher operations. RabbitMQ's management UI shows the shared exchange and
competing receive endpoint.

The Raven consumer is the attributed function in
`RavenOrderService/src/Consumers.rvn`; it is discovered from the assembly by
`AddConsumers(...)` without a container class or `IConsumer<T>` implementation.
The Java service deliberately uses a conventional consumer class so the sample
shows equivalent messaging behavior with language-appropriate authoring.

## Build services directly

These checks do not start infrastructure:

```bash
dotnet build RavenOrderService/OrderIntake.rvnproj --property WarningLevel=0
mvn -B -f JavaFulfillmentService/pom.xml -DskipTests package
```

To inspect the CloudShell resource model without running it:

```bash
./cloudshell.sh template
```

When adopting a newer MyServiceBus preview, update the version constant in
`AppHost/Program.cs`, `MyServiceBusVersion` in
`RavenOrderService/Directory.Build.props`, and
`myservicebus.version` in `JavaFulfillmentService/pom.xml`. Keeping one version
property per build surface makes package and container upgrades explicit.

## Current boundary

CloudShell models MyServiceBus today through ordinary application, broker,
database, endpoint, dependency, health, log, and replica resources. The
MyServiceBus dashboard remains a separately hosted application. A future
CloudShell extension could add MyServiceBus-specific topology and dashboard
links, but this sample intentionally does not introduce a parallel messaging
domain model or couple CloudShell to MyServiceBus internals.
