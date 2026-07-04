# Device Registry

Device Registry is the first IoT resource model slice. It is a service
resource that owns device enrollment, trusted factory certificate references,
device identity provisioning, registry-owned device metadata, and the lifecycle
of the registry service process.

## Resource Shape

The built-in resource type is `iot.device-registry` with provider ID
`iot.device-registry` and class `service`.

Authoring attributes:

| Attribute | Purpose |
| --- | --- |
| `endpoint` | Device Registry service endpoint. |
| `trust.certificates` | Vault-backed certificate references trusted for factory enrollment. |
| `enrollmentPolicy.subjectPrefixes` | Device subject prefixes accepted during enrollment. |
| `enrollmentPolicy.requiredClaims` | Required non-secret enrollment claims. |
| `enrolledDeviceCount` | Provider-managed count projected on the registry resource. |

Device credentials and certificate payloads must not be projected through
resource attributes. Trusted certificates are stored as
`ResourceCertificateReference` values pointing at a Secrets Vault certificate.

## Programmatic Authoring

```csharp
var vault = builder.AddSecretsVault("vault");
var ca = vault.Certificate("factory-ca");

var devices = builder
    .AddDeviceRegistry("devices")
    .TrustCertificate(ca)
    .UseEnrollmentPolicy(policy =>
    {
        policy.AllowSubjectPrefix("device/");
        policy.RequireClaim("manufacturer", "acme");
    });
```

`TrustCertificate(...)` records the certificate reference and adds a dependency
on the vault resource. The registry can then be started through its lifecycle
actions, using the same host-local process runtime pattern as Configuration
Store and Secrets Vault.

Enrollment policy is the MVP gate for provisioning. Future enrollment templates
should also describe the resource access grants assigned to the enrolled
device principal, making enrollment the durable source for what services a
device can access after provisioning.

## Device Identity

An enrolled device establishes a `deviceIdentity` principal category. Device
identities use the same built-in authority mechanics as app/resource identities
in the MVP, but they are not app resources and should remain distinguishable in
access control, diagnostics, API projection, and future UI.

The Device Registry service persists registry-owned device metadata in its own
device store. During enrollment it registers the device identity with the
built-in authority registry and returns client credentials for the device.
Those credentials are returned only in the enrollment response and are not
stored in projected resource attributes.

Enrollment requests also include non-secret device properties. The generic C#
client sends basic current-device facts such as platform, operating system,
OS and process architecture, framework description, machine name, and processor
count. Specialized clients, for example a future Raspberry Pi client, can add
capability properties without changing the registry contract.

The connected-device sample keeps the device app outside the CloudShell
resource graph. The app enrolls the current machine, receives a device
principal, and uses that identity to read a Configuration Store entry. Devices
that need offline or push-based settings are expected to use a future protocol
surface such as MQTT rather than this HTTP pull flow.

The built-in Resource Manager UI contributes a read-only Devices tab for
Device Registry resources. It lists enrolled devices and shows the device
identity metadata, enrollment claims, and non-secret device properties reported
by the client.

## Runtime Boundary

The default service project is:

```text
CloudShell.DeviceRegistryService/CloudShell.DeviceRegistryService.csproj
```

The service receives:

- a registry definition file path through
  `CloudShell:DeviceRegistryService:DefinitionsPath`
- the scoped registry resource ID through
  `CloudShell:DeviceRegistryService:ResourceId`
- an optional device database path through
  `CloudShell:DeviceRegistryService:DevicesPath`

When `DevicesPath` is omitted, the service stores devices next to the registry
definition file using a `.devices.json` sidecar. This is the MVP database
boundary for registry-owned device metadata. A future provider can replace the
store with a stronger database while keeping the resource model stable.

## Known Gaps

- Factory certificate proof validation is not implemented yet; trusted
  certificate references are modeled and persisted for the next slice.
- Enrolled devices are not projected as resources yet.
- Device revocation, credential rotation, device groups, per-application
  identities, and provider-backed identity systems are future work.
- Enrollment templates for device permission grants are future work; the MVP
  carries registry-scoped permission grants through the runtime definition.
- Device inventory UI is read-only; remove/revoke actions are not implemented
  yet.
