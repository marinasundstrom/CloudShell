# Device Registry

Device Registry is the first IoT resource model slice. It can register any
device that needs a CloudShell device identity, from a local PC or gateway to a
low-power IoT device. The model follows familiar IoT registry expectations:
enrollment, trust, device identity, lifecycle state, presence, and optional
desired/reported twin state.

Device Registry is a service resource that owns device enrollment, trusted
factory certificate references, device identity provisioning, registry-owned
device metadata, and the lifecycle of the registry service process.

## Resource Shape

The built-in resource type is `iot.device-registry` with provider ID
`iot.device-registry` and class `service`.

Authoring attributes:

| Attribute | Purpose |
| --- | --- |
| `endpoint` | Device Registry service endpoint. |
| `mqtt.endpoint` | Optional MQTT endpoint for device heartbeat and reported-state sync messages. |
| `trust.certificates` | Vault-backed certificate references trusted for factory enrollment. |
| `enrollmentPolicy.subjectPrefixes` | Device subject prefixes accepted during enrollment. |
| `enrollmentPolicy.requiredClaims` | Required non-secret enrollment claims. |
| `heartbeat.staleAfterSeconds` | Optional heartbeat age after which an active device is reported as stale. |
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

Enrollment policy is the MVP gate for provisioning. Enrollment profiles extend
that policy with resource access grants assigned to the enrolled device
principal, making enrollment the durable source for what services a device can
access after provisioning.

Enrollment profiles are intentionally close to Azure IoT DPS enrollment
language while staying CloudShell-specific:

- An `individual` profile targets one exact device subject and is the path for
  per-device provisioning.
- A `group` profile targets devices that match subject prefixes and required
  claims, sharing the same provisioning outcome.

The current profile outcome is a set of CloudShell resource permission grants
for the resulting `deviceIdentity`. Future slices can add richer attestation,
initial configuration, and UI controls for individual and group enrollments
without changing the device identity contract.

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

Device records carry lifecycle, presence, and twin state. These are separate
concepts:

- lifecycle state is the registry-owned access state such as `active` or
  `revoked`;
- presence is computed contact state such as `online`, `stale`, `unknown`, or
  `revoked`;
- twin state is an optional desired/reported state document used by device
  scenarios that need state reconciliation, especially low-power or
  intermittently connected devices.

Device records expose:

| Field | Meaning |
| --- | --- |
| `status` | Registry-owned device state such as `active` or `revoked`. |
| `presence` | Computed contact state such as `online`, `stale`, `unknown`, or `revoked`. |
| `enrollmentProfileName` | Name of the enrollment profile that provisioned the device, when known. |
| `enrollmentProfileKind` | Profile kind, such as `individual` or `group`, that provisioned the device. |
| `enrolledAt` | Time the device identity was first provisioned. |
| `lastSeenAt` | Last registry-observed device contact. Enrollment and heartbeat update this value. |
| `lastSeenSource` | Source of the last contact, such as `enrollment`, `heartbeat`, or a client-provided source. |
| `revokedAt` | Time the device identity was revoked, when applicable. |
| `revokedReason` | Optional non-secret operator reason for revocation. |

Heartbeat is explicit and opt-in at the device application level. A device
uses its issued identity token to call the heartbeat endpoint for its own
device record. Heartbeat updates `lastSeenAt`, can merge non-secret reported
properties, and does not imply CloudShell can start or stop the physical
device. A registry can optionally configure `heartbeat.staleAfterSeconds`; when
set, active devices whose last contact is older than that threshold report
`presence=stale`. Without that threshold, CloudShell records last-seen metadata
but does not infer stale device presence.

Device twin services are part of the baseline Device Registry capability, but
using twin state is optional for each device scenario. A simple device may only
enroll and use its identity to call CloudShell services. Devices that need state
reconciliation can use the pull-based MVP sync endpoint with their issued
identity token when they wake or reach a configured contact interval. Sync can
update reported state, merge non-secret device properties, update `lastSeenAt`,
and return the latest desired state. Desired and reported state each carry a
monotonically increasing version and timestamp so a device can send the last
desired version it observed and cheaply determine whether it needs to apply new
state.

The twin model intentionally does not imply that CloudShell has an always-on
connection to the device. It supports low-power devices that periodically wake,
report their current state, receive desired state, then disconnect. HTTP pull is
the baseline path when a device needs the latest desired state response. Device
Registry can also expose an experimental embedded MQTT endpoint for heartbeat
and sync messages using the same device identity credentials. MQTT sync messages
update the same registry-owned metadata and twin reported state as HTTP calls,
and MQTT 5 clients can provide a response topic to receive the same
`DeviceSyncResponse` desired-state payload.

The MQTT topic contract is:

| Topic | Payload |
| --- | --- |
| `cloudshell/device-registries/{registryId}/devices/{deviceId}/heartbeat` | `DeviceHeartbeatRequest` JSON. |
| `cloudshell/device-registries/{registryId}/devices/{deviceId}/sync` | `DeviceSyncRequest` JSON. |

`registryId` and `deviceId` are URL-escaped topic segments. MQTT clients
authenticate with the enrolled device `clientId` as the MQTT username and the
issued device `clientSecret` as the MQTT password. The embedded MQTT endpoint
is local-development infrastructure for the first slice; external broker
bridges, TLS listener configuration, retained desired-state messages, and
broker-native diagnostics remain future work.

Revocation marks the device record as `revoked` and unregisters the built-in
device identity client so future token requests with that device credential are
rejected. Already-issued short-lived bearer tokens remain normal bearer tokens
until they expire; the registry rejects revoked devices on registry-owned
operations such as heartbeat.

Removing a device record deletes the registry-owned metadata and unregisters
the built-in device identity client. Removal is a management cleanup operation;
revocation remains the explicit audit-friendly access-denial state when an
operator needs to preserve the record.

Enrollment requests also include non-secret device properties. The generic C#
client sends basic current-device facts such as platform, operating system,
OS and process architecture, framework description, machine name, and processor
count. Specialized clients, for example a future Raspberry Pi client, can add
capability properties without changing the registry contract.

The connected-device sample keeps the device app outside the CloudShell
resource graph. The app enrolls the current machine, receives a device
principal, sends HTTP heartbeat and sync updates, publishes a follow-up MQTT
sync update when an MQTT endpoint is configured, and uses that identity to read
a Configuration Store setting.

The built-in Resource Manager UI contributes Device Registry tabs under the
General section:

- Standard Resource Manager endpoint surfaces show the HTTP registry endpoint
  and, when configured, the MQTT endpoint as separate local endpoints so
  operators can tell which device transports are available.
- **Devices** lists enrolled devices and shows device status, last seen,
  identity metadata, enrollment claims, and non-secret device properties
  reported by the client. Registry operators can revoke access for the selected
  device or remove the device record from the dedicated device details view when
  the backing registry service is running. The details view includes identity,
  lifecycle, enrollment profile, properties, claims, and a Twin section that
  shows desired and reported state versions, update timestamps, last sync time,
  read-only reported state JSON, and an editor for the desired state JSON object.
- **Enrollment profiles** shows the base enrollment policy, trusted
  certificate references, individual/group profile matching criteria, and the
  resource permission grants a matching device identity receives. Profile
  editing is intentionally deferred.

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
- Credential rotation, device groups, per-application identities, and
  provider-backed identity systems are future work.
- Enrollment profiles are the first provisioning policy shape; richer matching,
  profile selection diagnostics, and individual/group enrollment management are
  future work.
- Twin history and conflict diagnostics are future work.
- Device telemetry and device-submitted logs should be integrated with
  CloudShell observability under the Device Registry resource and global
  observability views in a future slice.
- External broker integration, TLS listener configuration, retained messages,
  and richer microcontroller provisioning remain future work for MQTT.
