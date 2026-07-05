# Device Registry sample

## Scenario

This sample demonstrates the first CloudShell IoT flow: a device enrolls with a
Device Registry, receives a CloudShell device identity, and then uses that
identity to access a service and synchronize device state.

The physical device is simulated by the local `DeviceApp` process. It is
intentionally outside the CloudShell resource graph to model the real-world
case where the software running on a PC, Raspberry Pi, microcontroller gateway,
or other device is not part of the control plane. CloudShell tracks the device
through the Device Registry record and the provisioned device principal.

The scenario uses these CloudShell features:

| Feature | Role in the scenario |
| --- | --- |
| Launcher-hosted resource template | Declares the local Device Registry, trust vault, and Configuration Store resources. |
| Secrets Vault | Holds the factory CA certificate reference used as the registry trust anchor. |
| Device Registry | Owns enrollment policy, device records, identities, heartbeat, presence, twin state, revocation, and removal. |
| Enrollment profile | Matches devices by subject/claims and grants the resulting device identity access to selected resources. |
| Built-in identity provider | Issues the device identity credentials and bearer tokens used by the sample device app. |
| Configuration Store | Provides a setting that the enrolled device reads remotely using its own identity. |
| Device client API | Enrolls the current machine, sends heartbeat, and performs device twin sync. |
| Resource Manager UI | Shows enrolled devices, reported properties, presence, enrollment profiles, and editable desired twin state. |

The end-to-end flow is:

1. The launcher applies resources for a factory-style local environment.
2. The Device Registry and related backing services are started from the
   resource model.
3. The standalone device app enrolls the current machine with subject
   `device/<machine>`.
4. The registry validates the enrollment profile and provisions a
   `deviceIdentity` principal.
5. The device requests a token with its issued credentials and reads a
   Configuration Store setting.
6. The device sends heartbeat and sync calls so the registry records presence,
   reported properties, and reported twin state.
7. Operators can inspect the device in Resource Manager, update desired twin
   state, revoke access, or remove the device record.

HTTP is the transport used in this sample. The lifecycle, identity, and twin
concepts are intended to stay transport-neutral so a future MQTT transport can
use the same Device Registry model.

## Components

This sample has two apps:

- `AppHost/CloudShell.DeviceRegistryAppHost` is the CloudShell launcher that
  declares the trust vault, Device Registry, and device configuration resources.
- `DeviceApp/CloudShell.DeviceRegistry.DeviceApp` is a separate app that
  enrolls the current machine through `DeviceRegistryClient`.

The registry enrollment policy accepts subjects under `device/` and requires
the `manufacturer=cloudshell` enrollment claim. The launcher targets
`CloudShell.LocalDevelopmentHost` and declares three local service resources:

| Resource | Default endpoint |
| --- | --- |
| Device Registry | `http://localhost:7150` |
| Factory Trust Vault | `http://localhost:7151` |
| Device Settings | `http://localhost:7152` |

## Run

Run the CloudShell launcher:

```bash
cd samples/DeviceRegistry
./cloudshell.sh run
```

The launcher starts CloudShell at `http://127.0.0.1:5108` and applies the
sample resource template. The launcher host configuration lives in
`AppHost/appsettings.json`. It uses an in-memory built-in identity user for
local development:

| Field | Value |
| --- | --- |
| Email | `device-admin@example.test` |
| Password | `CloudShell123!` |

Authentication is disabled by default so template apply and the device flow can
run without an initial sign-in. Enable authentication in `AppHost/appsettings.json`
when you want to exercise the UI login path with the seeded user.

After the template is applied, start the service resources from a second
terminal. The helper script targets the launcher-owned Control Plane URL by
default:

```bash
./cloudshell.sh start-services
```

Then run the device app independently:

```bash
./cloudshell.sh run-device
```

Call the device app to enroll the current machine and read configuration with
the issued device identity. The app also sends a heartbeat check-in after it
receives a device token, then performs a device twin sync so the registry
records reported state and returns desired state:

```bash
./cloudshell.sh enroll
```

The device app is intentionally not a CloudShell resource; it represents
software running on the enrolled device.

The launcher declares a group enrollment profile that grants matching devices
read access to the Configuration Store setting. The registry expands that profile
into permissions for the device identity created during enrollment. It also
configures a five-minute heartbeat stale-after window so the registry can show
device presence as `online` or `stale` based on the most recent heartbeat.
The same identity can call the sync endpoint when a device wakes to report its
current state and fetch the latest desired state version.

The generic device client sends basic device properties during enrollment,
including platform, operating system, architecture, framework description,
machine name, and processor count. Specialized clients can add more properties
for device capabilities. The Device Registry resource blade shows enrolled
devices, status, last-seen information, reported properties, and configured
enrollment profiles. Registry operators can revoke a device identity through
the Device Registry API; revocation disables future token issuance for that
device credential. Operators can also remove the device record after revocation
or when they want to clean up sample state.
