# Remote Docker Hosts Proposal

## Status

Proposed.

## Problem

The Docker provider currently models the Docker Engine as the local host
engine. That works for local development, but CloudShell also needs to register
Docker hosts that run on remote machines while preserving the existing resource
model and provider-owned configuration boundary.

Remote Docker support should let a resource group contain Docker host
resources for the engines it can operate. A host should be registered only once
in a given resource group, even if a user tries to add it again with a
different display name or through a different registration path.

## Goals

- Introduce `docker.host` as the canonical Docker host resource type with
  `ResourceClass.Infrastructure`.
- Support both local Docker hosts and remote Docker hosts through the same
  Docker host resource type.
- Keep host connection details and credentials behind the Docker provider.
- Project only non-secret host facts, such as host kind and normalized endpoint.
- Enforce one Docker host per normalized host identity in each resource group.
- Allow the same physical host to be registered in different resource groups
  when teams intentionally want separate scopes.
- Keep Docker container sub-resources parented under the selected Docker host.

## Non-Goals

- Do not turn Docker hosts into a platform-owned resource group primitive.
- Do not expose remote host credentials through `Resource.Attributes`,
  endpoints, logs, or API responses.
- Do not require remote Docker support for local development defaults.
- Do not model Docker contexts as first-class CloudShell resources in the first
  version.
- Do not implement multi-engine container scheduling in this proposal.

## Resource Type Naming

`docker.host` is the more appropriate canonical resource type for this feature.
The resource represents a configured Docker host connection in a resource
group, not just the Docker Engine process itself. A local host and a remote host
both expose a Docker Engine API, but users add, group, update, and de-duplicate
the host connection.

`docker.engine` already exists and should be kept as a compatibility alias
during migration. Existing local development defaults, tests, routes, and
programmatic declarations can continue to work while new projection and docs
move toward `docker.host`.

Recommended transition:

1. Project new Docker host resources with `TypeId: docker.host`.
2. Continue recognizing existing `docker.engine` registrations as Docker host
   resources.
3. Keep the default local resource ID `docker:engine` initially to avoid
   breaking persisted state.
4. Add a future persisted-state migration only if the resource ID should become
   `docker:local` or another host-oriented default.

## Resource Model

Docker hosts are `Resource` projections with:

- `TypeId`: `docker.host`
- `ResourceClass`: `Infrastructure`
- infrastructure kind: `Docker`
- endpoints: the Docker Engine endpoint, with private exposure
- children: Docker container resources discovered or declared under that host
- provider-owned actions and diagnostics

The Docker provider should add non-secret attributes:

- `docker.host.kind`: `local` or `remote`
- `docker.host.endpoint`: normalized endpoint with credentials removed
- `container.registry`: existing registry value

`docker.host.kind` distinguishes the existing local host from a remote host
without introducing a parallel resource shape. Local hosts use the current
socket or named-pipe endpoint resolution. Remote engines use an explicit
endpoint supplied during registration or programmatic declaration.

## Host Configuration

Provider-owned host configuration should normalize into a model similar to:

```csharp
public sealed record DockerHostDefinition(
    DockerHostKind Kind,
    Uri Endpoint,
    DockerHostCredentials? Credentials = null);

public enum DockerHostKind
{
    Local,
    Remote
}

public sealed record DockerHostCredentials(
    DockerHostCredentialKind Kind,
    string? Username = null,
    string? PasswordEnvironmentVariable = null,
    string? ClientCertificatePath = null,
    string? ClientKeyPath = null,
    string? CertificateAuthorityPath = null);

public enum DockerHostCredentialKind
{
    None,
    UsernamePasswordEnvironmentVariable,
    TlsCertificateFiles
}
```

The first implementation should support the credential mechanisms required by
the Docker client transport that CloudShell enables:

- no credentials for local sockets, named pipes, or explicitly allowed
  unauthenticated remote endpoints
- username plus password environment variable for transports that support
  basic authentication or SSH-style authentication
- TLS certificate file references for HTTPS Docker Engine endpoints

Credential values stay provider-owned. Only credential kind and safe reference
metadata may be displayed in provider UI. Password values must be read from the
named environment variable at execution time.

## Programmatic API

The existing `AddDocker()` API should keep declaring the default local Docker
host.

Remote authoring should use an explicit host shape:

```csharp
var remoteDocker = resources
    .AddDocker("docker:build-01", "Build Host 01")
    .UseRemoteHost(new Uri("tcp://build-01.example.com:2376"))
    .WithTlsCertificateFiles(
        certificateAuthorityPath: "/etc/cloudshell/docker/ca.pem",
        clientCertificatePath: "/etc/cloudshell/docker/cert.pem",
        clientKeyPath: "/etc/cloudshell/docker/key.pem")
    .WithResourceGroup("team-platform");

remoteDocker.AddContainer("redis", "redis:7.2");
```

Local authoring can remain concise:

```csharp
var localDocker = resources.AddDocker();
```

The builder should also support an explicit local host call when users want a
named non-default local engine:

```csharp
resources
    .AddDocker("docker:local-ci", "Local CI Docker")
    .UseLocalHost();
```

## Registration UI

The Docker host registration surface should expose:

- resource group
- host mode: local or remote
- remote endpoint when host mode is remote
- credential kind
- credential fields for the selected kind
- registry and registry credential settings, preserving the existing behavior
- a connection test before or during registration

The local mode should use the current endpoint discovery order. The remote mode
should require an explicit absolute endpoint and should show validation errors
before creating platform registration state.

## Uniqueness Rule

The Docker provider should compute a normalized host identity from:

1. host kind
2. normalized endpoint
3. resource group ID, where the default group is treated as a concrete scope

For local hosts, the normalized endpoint should be the resolved Docker endpoint
after applying the existing discovery order. For remote hosts, normalization
should lower-case scheme and host, remove trailing slashes, normalize default
ports, and strip credentials or query fragments.

When a host with the same normalized identity already exists in the resource
group, registration should return a validation result instead of creating a new
registration. The existing host can be updated for display name, registry, and
credential references through the update flow, but adding it again should not
create a second resource.

The same remote host may be registered in another resource group because group
membership is an authorization and ownership boundary.

## Provider Responsibilities

The Docker provider must:

1. Persist provider-owned host configuration for UI-created Docker hosts.
2. Register the Docker host through Resource Manager only after provider
   validation succeeds.
3. Enforce the group-scoped host uniqueness rule.
4. Create one Docker client per configured host, instead of one provider-wide
   client.
5. Discover containers per host and parent them under that host resource.
6. Execute container actions through the Docker client for the parent host.
7. Redact credentials from projected resources, logs, diagnostics, and errors.
8. Keep the existing local Docker Engine behavior for local development
   defaults.

## Persistence

Remote host connection settings are provider-owned configuration. They should
not be stored as platform resource attributes. For persisted UI-created hosts,
the Docker provider needs a small provider configuration store keyed by Docker
resource ID and resource group ID.

The platform-owned registration store continues to hold:

- resource ID
- provider ID
- resource group ID
- dependencies

The Docker provider-owned store holds:

- host kind
- endpoint
- credential references
- registry
- registry credential references
- display name

If the provider configuration is missing for a registered remote host, the
resource should project as unavailable with a diagnostic that names the missing
provider configuration, not as a local Docker fallback.

## API and Client Impact

No new Control Plane platform primitive is required. The existing resource API
continues to expose Docker hosts as ordinary resource projections.

The create/update path for Docker host resources needs provider-specific
configuration payload support, either through the existing resource creation
metadata path or a Docker-provider-specific registration component. Invalid
remote endpoint or duplicate-host attempts should surface as stable validation
errors rather than runtime exceptions.

## Tests

Add focused coverage for:

- declaring a local Docker host preserves the existing `docker:engine` behavior
- declaring a remote Docker host projects `TypeId=docker.host` and
  `docker.host.kind=remote`
- existing `docker.engine` registrations continue to resolve as Docker host
  resources during migration
- remote host credentials are not projected as attributes or endpoints
- UI/provider registration rejects duplicate local host registration in the
  same resource group
- UI/provider registration rejects duplicate remote endpoint registration in
  the same resource group
- the same remote endpoint can be registered in two different resource groups
- containers discovered from a remote host are parented under that host
- actions on remote-host containers dispatch through that host's Docker client
- missing provider-owned configuration projects the host as unavailable

## Open Questions

- Should the first remote implementation support SSH transport directly, or
  require TCP/HTTPS Docker Engine endpoints and add SSH later?
- Should Docker host configuration be stored in the existing persistence
  project as a provider table, or should providers get a generic private
  configuration store first?
- Should duplicate add attempts navigate to the existing Docker host, offer to
  update it, or return only a validation message?
- Should host identity include credential identity, or should endpoint plus
  resource group be the only uniqueness key?
- How should container app host binding choose among multiple Docker hosts in a
  group before a broader scheduling model exists?

## Implementation Plan

1. Add Docker host kind, endpoint, credential, and identity normalization
   models to the Docker provider.
2. Split Docker discovery and action execution so each configured host owns a
   Docker client and container snapshot.
3. Add provider-owned persisted configuration for UI-created Docker hosts.
4. Extend the Docker host registration and update UI for local/remote host
   modes and credentials.
5. Add group-scoped duplicate-host validation before resource registration.
6. Add programmatic builder methods for explicit local and remote Docker
   hosts.
7. Update Docker docs and Resource Manager tests for registration, projection,
   credential redaction, and duplicate handling.
