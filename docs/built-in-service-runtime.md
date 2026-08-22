# Built-In Service Runtime

Configuration Store, Secrets Vault, and Device Registry are CloudShell
resource types whose backing services can run either as local .NET processes
or as OCI container images. The authored `ResourceDefinition`, projected
resource identity, endpoints, lifecycle operations, seed data, permissions,
and client contracts stay the same in both modes. Runtime realization remains
provider-owned.

## Runtime Modes

`BuiltInServiceRuntimeMode` selects the backing implementation:

| Mode | Intended use | Backing artifact |
| --- | --- | --- |
| `Process` | Developing CloudShell from its source repository. | `dotnet run --project` against the service project. |
| `Container` | Installed CLI and release-host scenarios where service source projects are unavailable. | A configured OCI image started through `IContainerHostRuntime`. |

`CloudShell.LocalDevelopmentHost` defaults to `Process` when built or run
directly. The development host bundled by `CloudShell.Cli` is compiled with
`Container` as its default. A custom build can select the default explicitly:

```bash
dotnet build CloudShell.LocalDevelopmentHost \
  -p:CloudShellBuiltInServiceRuntime=Container
```

Host configuration can override that compiled default:

```text
CloudShell__BuiltInServices__RuntimeMode=Container
CloudShell__BuiltInServices__ConfigurationStore__Image=cloudshell:configuration-store-local
CloudShell__BuiltInServices__SecretsVault__Image=cloudshell:secrets-vault-local
CloudShell__BuiltInServices__DeviceRegistry__Image=cloudshell:device-registry-local
```

The mode is a host/provider choice, not a field in resource templates. A YAML
file or launcher therefore declares Configuration Store, Secrets Vault, and
Device Registry the same way for source and installed hosts.

## Shared Configuration Contract

Both runtime adapters use the same provider-owned configuration:

- the serialized service definition and resource ID
- authentication and service-bearer settings
- Configuration Store settings, Secrets Vault secrets/certificates, and
  Device Registry enrollment configuration
- the externally projected HTTP endpoint
- the optional Device Registry MQTT endpoint

The process adapter passes the endpoint through `--urls` and gives the service
a host filesystem definition path. The container adapter sets
`ASPNETCORE_URLS`, publishes the resource's loopback ports, and mounts the same
generated definition directory read-only at `/cloudshell/definitions`.
Sensitive seed values and signing material are runtime inputs; they are never
baked into an image or projected through Resource Manager diagnostics.

## Building Images Locally

Build all three images from the repository root:

```bash
eng/containers/build-built-in-services.sh
```

The default tags are:

```text
cloudshell:configuration-store-local
cloudshell:secrets-vault-local
cloudshell:device-registry-local
```

Pass a tag as the first argument to build a versioned local set:

```bash
eng/containers/build-built-in-services.sh 0.1.0-preview.5
```

The Dockerfiles accept `DOTNET_VERSION` as a build argument and default to
`11.0-preview`.

### Local Registry

Start a development-only OCI registry on `localhost:5000`, build the image
set, and push it to that registry:

```bash
eng/containers/publish-built-in-services-local.sh local
```

Set `CLOUDSHELL_LOCAL_REGISTRY_PORT` to use another loopback port. The script
uses a fixed `cloudshell-local-registry` container, validates its published
port before reuse, and prints the corresponding host image overrides.

To prove that a locally packed CLI pulls from the local registry, pack it with
the repository override and run the shared verification script:

```bash
dotnet pack CloudShell.Cli/CloudShell.Cli.csproj \
  --configuration Release \
  --output artifacts/local-packages \
  -p:Version=0.1.0-local.1 \
  -p:PackageVersion=0.1.0-local.1 \
  -p:CloudShellBuiltInServiceImageRepository=localhost:5000/cloudshell

eng/containers/publish-built-in-services-local.sh 0.1.0-local.1
eng/containers/verify-cli-built-in-service.sh \
  0.1.0-local.1 \
  artifacts/local-packages \
  localhost:5000/cloudshell
```

The verifier removes the exact Configuration Store image from the local Docker
cache, installs the requested CLI version, starts the YAML sample, executes the
Configuration Store `start` action, checks `/healthz`, and asserts that the
owned container uses the expected registry reference.

## Release Images

The CLI host embeds one immutable image reference per built-in service:

```text
ghcr.io/marinasundstrom/cloudshell:configuration-store-<version>
ghcr.io/marinasundstrom/cloudshell:secrets-vault-<version>
ghcr.io/marinasundstrom/cloudshell:device-registry-<version>
```

An explicit preview publication builds and verifies the NuGet artifacts first,
publishes all three same-version images to GHCR, verifies clean registry pulls,
and only then publishes the packages to MyGet. The installed CLI is finally
smoke-tested against the published Configuration Store image. NuGet.org remains
the stable release channel; MyGet is the development-build feed. Docker Hub can
later mirror the same repository and tags without changing the CLI contract.

## Container Lifecycle

The container adapter uses the shared owner-scoped `IContainerHostRuntime`
boundary. It creates a stable container name, labels the container with the
owning CloudShell resource ID, publishes the configured endpoint ports, waits
for `/healthz`, and maps `start`, `stop`, and `restart` to typed container-host
operations. Failed startup removes the attempted container and returns a
provider diagnostic.

The current container-backed monitor does not yet project container metrics
through the process-specific monitoring contract. Multi-architecture
manifests, supply-chain metadata, and an optional Docker Hub mirror remain
release workflow work.
