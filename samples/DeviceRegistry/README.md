# Device Registry sample

This sample has two apps:

- `CloudShell.DeviceRegistry` is the CloudShell launcher that declares the
  trust vault and Device Registry resources.
- `DeviceApp/CloudShell.DeviceRegistry.DeviceApp` is a separate app that
  enrolls the current machine through `DeviceRegistryClient`.

The registry enrollment policy accepts subjects under `device/` and requires
the `manufacturer=cloudshell` enrollment claim. Start the Device Registry
resource from CloudShell, then run the device app independently with
`CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT` and
`CLOUDSHELL_DEVICE_REGISTRY_RESOURCE_ID` set. Call
`POST /enroll-current-device` on the device app endpoint to get device
principal credentials. The device app is intentionally not a CloudShell
resource; it represents software running on the enrolled device.

The generic device client sends basic device properties during enrollment,
including platform, operating system, architecture, framework description,
machine name, and processor count. Specialized clients can add more properties
for device capabilities. The Device Registry resource blade shows enrolled
devices and their reported properties on the Devices tab.
