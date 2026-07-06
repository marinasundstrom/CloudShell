# Robotic Mower IoT sample

This sample models a small public-park mower fleet:

- `application.javascript-app:mower-frontend` is a React/Vite operator view.
- `application.container-app:mower-backend` is an ASP.NET Core SignalR backend
  running as a local container app.
- `iot.device-registry:park-devices` enrolls simulated mower devices and
  records their device identities, presence, desired state, and reported state.
- `DeviceApp` is a standalone simulated mower process. It enrolls through the
  Device Registry API, reports coordinates inside a fixed park area by
  publishing Device Registry MQTT sync messages, and receives start/stop
  commands and mowing pattern changes as desired state in the MQTT sync
  response.

The CloudShell resource graph is declared by the C# launcher in `AppHost`.
The launcher targets `CloudShell.LocalDevelopmentHost`; this sample does not
define its own CloudShell host.

The frontend does not talk directly to mower devices. It connects to the
backend SignalR hub, sends commands to the backend, and receives fleet updates
that the backend observes from Device Registry reported twin state. The device
app does not use SignalR and does not host a web server.

The command path is intentionally registry-backed:

1. The operator clicks **Start**, **Stop**, or changes the mowing pattern in
   the frontend.
2. The frontend calls the backend SignalR hub.
3. The backend uses the registry management identity to write the mower command
   or pattern into Device Registry desired state.
4. The mower simulator publishes its next MQTT sync message with reported
   position and state.
5. Device Registry returns the latest desired state in the MQTT response.
6. The mower applies the command or pattern and reports the resulting state in
   subsequent MQTT sync messages.

The sample uses one backend replica and does not add RabbitMQ, Redis, or a
SignalR backplane. The important scaling boundary is that the backend does not
own durable device connections. Multiple browser clients can manage the same
mower because commands target the mower's registry twin, not a backend-local
SignalR connection.

Device Registry is enough for the current fleet overview: enrolled devices,
presence, last-seen transport, desired state, and reported state. A larger IoT
solution would add an Event Broker or telemetry ingestion service for
historical streams and event facts such as position samples, crashes, slips,
blade stalls, and notifications. That broker should complement the registry;
it should not become the owner of device identity, presence, or desired-state
reconciliation.

## Run

Start the CloudShell host:

```bash
cd samples/RoboticMowerIoT
./cloudshell.sh run
```

From a second terminal, start the registry, backend, and frontend resources:

```bash
./cloudshell.sh start-services
```

Install Node dependencies once for the frontend and device simulator:

```bash
cd samples/RoboticMowerIoT/Frontend
npm install
cd ../DeviceApp
npm install
```

Run a simulated mower:

```bash
cd samples/RoboticMowerIoT
./cloudshell.sh run-mower
```

Open the frontend at:

```text
http://localhost:7162
```

The default mower uses subject `mower/mower-001` and must present the local
sample enrollment token `local-development-mower-enrollment-token`. The Device
Registry enrollment profile accepts mower subjects with
`manufacturer=cloudshell` and `deviceType=robotic-mower`.

Run additional mower simulators by setting `MOWER_ID` and `MOWER_NAME`:

```bash
MOWER_ID=mower-002 MOWER_NAME="Mower 002" ./cloudshell.sh run-mower
```

Run duplicate simulator connections for the same mower identity by reusing
`MOWER_ID` from separate terminals:

```bash
MOWER_ID=mower-001 MOWER_NAME="Mower 001 / B" ./cloudshell.sh run-mower
```

## Endpoints

| Resource | Default endpoint |
| --- | --- |
| CloudShell UI and Control Plane | `http://127.0.0.1:7165` |
| Device Registry | `http://localhost:7160` |
| Device Registry MQTT | `mqtt://localhost:7163` |
| Mower backend ingress | `http://localhost:7161` |
| React frontend | `http://localhost:7162` |

Override the backend, frontend, registry HTTP, or registry MQTT endpoints with
`RoboticMowerIoT:BackendPort`, `RoboticMowerIoT:FrontendEndpoint`,
`RoboticMowerIoT:DeviceRegistryEndpoint`, and
`RoboticMowerIoT:DeviceRegistryMqttEndpoint` when the defaults are already in
use. The backend container receives
`RoboticMowerIoT:BackendDeviceRegistryEndpoint`, which defaults to
`http://host.docker.internal:7160` so it can reach the host-running Device
Registry service from Docker.
