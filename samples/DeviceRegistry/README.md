# Device Registry sample

This sample has two apps:

- `CloudShell.DeviceRegistry` is the CloudShell launcher that declares the
  trust vault and Device Registry resources.
- `DeviceApp/CloudShell.DeviceRegistry.DeviceApp` is a separate app that
  enrolls the current machine through `DeviceRegistryClient`.

The registry enrollment policy accepts subjects under `device/` and requires
the `manufacturer=cloudshell` enrollment claim. The launcher starts three
local service resources by default:

| Resource | Default endpoint |
| --- | --- |
| Device Registry | `http://localhost:7150` |
| Factory Trust Vault | `http://localhost:7151` |
| Device Settings | `http://localhost:7152` |

Run the CloudShell launcher:

```bash
dotnet run --project samples/DeviceRegistry/CloudShell.DeviceRegistry.csproj
```

Then run the device app independently:

```bash
dotnet run --project samples/DeviceRegistry/DeviceApp/CloudShell.DeviceRegistry.DeviceApp.csproj \
  --urls http://localhost:7153 \
  --DeviceRegistry:Endpoint http://localhost:7150 \
  --DeviceRegistry:ResourceId iot.device-registry:devices \
  --ConfigurationStore:Endpoint http://localhost:7152 \
  --ConfigurationStore:ResourceId configuration.store:device-settings \
  --ConfigurationStore:EntryName Device:Mode \
  --Device:Manufacturer cloudshell
```

Call the device app to enroll the current machine and read configuration with
the issued device identity:

```bash
curl -X POST http://localhost:7153/enroll-current-device
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
