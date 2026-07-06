# Robotic Mower IoT sample

This sample models a small public-park mower fleet:

- `application.javascript-app:mower-frontend` is a React/Vite operator view.
- `application.container-app:mower-backend` is an ASP.NET Core SignalR backend
  running as a local container app.
- `iot.device-registry:park-devices` enrolls simulated mower devices and
  records their device identities.
- `DeviceApp` is a standalone simulated mower process. It enrolls through the
  Device Registry API, connects to the backend as a mower client, reports
  coordinates inside a fixed park area, and receives start/stop commands.

The CloudShell resource graph is declared by the C# launcher in `AppHost`.
The launcher targets `CloudShell.LocalDevelopmentHost`; this sample does not
define its own CloudShell host.

The frontend does not talk directly to mower devices. It connects to the
backend SignalR hub, sends commands to the backend, and receives mower
telemetry that the backend relays from connected mower clients.

The sample keeps backend state in memory and does not add RabbitMQ, Redis, or
another SignalR backplane. The backend treats `mowerId` as the device identity
and each SignalR connection as a transient attachment to that identity.
Multiple mower simulator processes can enroll as the same mower and connect at
the same time; the UI shows the number of active backend connections for that
mower.

In a larger IoT architecture, durable device ownership would move behind a
device stream or telemetry collector service. That service would listen to all
device sessions, persist location and health streams, raise notifications such
as crash or slip events, and provide a shared command/desired-state surface.
Replicated backend services would then scale operator/API load while reading
from and writing through that device service instead of owning device
connections as durable state. This sample intentionally uses one backend
replica so that the focus stays on enrollment, telemetry, and command flow.

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
| Mower backend ingress | `http://localhost:7161` |
| React frontend | `http://localhost:7162` |

Override the backend, frontend, or registry endpoints with
`RoboticMowerIoT:BackendPort`, `RoboticMowerIoT:FrontendEndpoint`, and
`RoboticMowerIoT:DeviceRegistryEndpoint` when the defaults are already in use.
