# Device Registry sample

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

Run the CloudShell launcher:

```bash
cd samples/DeviceRegistry
./cloudshell.sh run
```

The launcher host configuration lives in `AppHost/appsettings.json`. It uses an
in-memory built-in identity user for local development:

| Field | Value |
| --- | --- |
| Email | `device-admin@example.test` |
| Password | `CloudShell123!` |

Authentication is disabled by default so template apply and the device flow can
run without an initial sign-in. Enable authentication in `AppHost/appsettings.json`
when you want to exercise the UI login path with the seeded user.

After the template is applied, start the service resources from a second
terminal:

```bash
./cloudshell.sh start-services
```

Then run the device app independently:

```bash
./cloudshell.sh run-device
```

Call the device app to enroll the current machine and read configuration with
the issued device identity:

```bash
./cloudshell.sh enroll
```

The device app is intentionally not a CloudShell resource; it represents
software running on the enrolled device.

The launcher declares a group enrollment profile that grants matching devices
read access to the Configuration Store entry. The registry expands that profile
into permissions for the device identity created during enrollment.

The generic device client sends basic device properties during enrollment,
including platform, operating system, architecture, framework description,
machine name, and processor count. Specialized clients can add more properties
for device capabilities. The Device Registry resource blade shows enrolled
devices and their reported properties on the Devices tab.
