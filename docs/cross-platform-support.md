# Cross-Platform Support

CloudShell is intended to run as a self-hosted local-development and
team-owned control plane across macOS, Linux, and Windows. This document tracks
the support contract, verification baseline, known gaps, and implementation
queue for making that platform promise concrete.

Cross-platform support covers more than the .NET solution compiling on another
operating system. The supported surface includes the CLI, launchers,
ResourceDefinition apply/export, remote Control Plane clients, Resource
Manager, host-run application resources, container-host integrations, local
networking, DNS/name mapping, storage, diagnostics, and sample workflows.

## Support Tiers

| Tier | Scope | Current expectation |
| --- | --- | --- |
| Tier 0: build and unit tests | `dotnet restore`, `dotnet build`, and non-integration tests on macOS, Linux, and Windows. | Required baseline for every cross-platform slice. |
| Tier 1: CLI and authoring | CLI parsing, profile resolution, template emit/apply, C# launcher behavior, and generated ResourceDefinition shape. | Should be portable and verified without privileged host changes. |
| Tier 2: host-run applications | .NET, JavaScript, Java, Go, Python, and executable app command construction and runtime diagnostics. | Must be verified through command-factory tests first, then sample smoke tests where toolchains exist. |
| Tier 3: container-backed local development | Container hosts, container apps, SQL Server, RabbitMQ, load balancers, and Docker/Podman-backed samples. | Linux CI is the first automated target. macOS and Windows remain supported targets once Docker runtime diagnostics are reliable. |
| Tier 4: host networking and names | Hosts-file changes, resolver cache refresh, local proxies, endpoint mappings, ingress, DNS/name mapping, and privileged host mutations. | Capability-driven diagnostics are required before mutating host state. OS-native behavior must be provider-specific and documented. |

## Verification Baseline

Automated verification starts with a GitHub Actions matrix over:

- `ubuntu-latest`
- `windows-latest`
- `macos-latest`

The baseline job checks committed patch whitespace, restores the solution,
builds it, and runs non-integration tests with
`Category!=Integration&Category!=DockerIntegration`. Docker-dependent and
executable-backed smoke tests remain explicit jobs so a missing runtime
prerequisite is reported as an environment issue rather than a product
regression.

Local development on macOS remains useful for fast iteration, but a change is
not cross-platform-ready until CI covers the relevant tier.

## Current Known Gaps

- There was no repository CI workflow before this tracking work, so failures on
  Linux and Windows are expected until the new matrix has been exercised.
- Several sample entry points still include `.sh` helper scripts. Launcher
  projects should own the normal cross-platform run path; shell scripts should
  remain convenience wrappers only.
- Host networking still has macOS-specific provider behavior. The portable
  model should be capability-driven local endpoint mapping and diagnostics,
  with OS-specific reconciliation behind provider contracts.
- Docker and Podman behavior is provider-owned and runtime-dependent. CI should
  first prove Linux container-backed paths, then add Windows and macOS Docker
  coverage once prerequisite handling is explicit.
- Host-readable local paths are intentionally gated by local-development host
  settings. Windows path handling, case behavior, separators, quoting, and
  executable extension conventions need focused tests in each owning provider.
- Resolver cache refresh and hosts-file mutation are privileged host
  operations. They need stable unavailable reasons and dry-run/testable
  planning before Resource Manager or CLI workflows depend on them.

## Implementation Queue

### Completed initial slice

1. Added the cross-platform CI matrix for patch whitespace, restore, build, and
   non-integration tests on pull requests, `main`, and `codex/**` branches.
2. Added a first injectable platform descriptor for CLI host-name mapping path
   selection.
3. Added focused tests around Unix and Windows default hosts-file resolution.
4. Added a Java app command-platform seam and deterministic Maven/Gradle
   wrapper selection tests for Windows and Unix behavior.
5. Added a JavaScript app command-platform seam and deterministic package-manager
   executable selection tests for Windows and Unix behavior.
6. Added a Go app command-platform seam and deterministic binary-path
   resolution tests for relative, Unix-rooted, and Windows-rooted paths.
7. Changed .NET app command construction to use discrete `dotnet`
   `ArgumentList` values and added path-with-spaces coverage.
8. Added testable CLI Control Plane daemon start-info construction for Windows
   direct launch and Unix detached shell launch.
9. Added executable app command tests for path-with-spaces and empty-argument
   startup behavior.
10. Enabled `codex/**` branch pushes to exercise the matrix before PRs and
    fixed the first Windows test failures for SQLite cleanup and path-shaped
    command assertions.
11. Verified the Tier 0 matrix is green for patch whitespace, restore, build,
    and non-integration tests on Ubuntu, macOS, and Windows.
12. Added a Python app command-platform seam and deterministic command tests
    for the current `python3` default, explicit Windows-friendly command
    overrides, script paths with spaces, and endpoint/environment precedence.
13. Added a C# launcher smoke test that writes a YAML ResourceTemplate,
    deserializes the emitted file, and applies it through an in-memory Resource
    Model Control Plane path without invoking shell scripts.
14. Added sample-level C# launcher template smoke coverage for lightweight
    AppHost samples so emitted templates are applied in memory without shell
    scripts, host startup, or runtime processes.
15. Split Docker-backed sample smoke coverage into an explicit Ubuntu Docker
    integration job with Docker daemon, Compose, and image-pull prerequisite
    checks before running `Category=DockerIntegration` tests.

### Active

1. Use the green Tier 0 matrix as the regression gate for the remaining
   cross-platform slices.
2. Move more direct OS checks behind small injectable platform descriptors where
   the behavior can be unit-tested from any development host.
3. Continue the same testability pattern for remaining command factories and
   runtime prerequisites.

### Next

1. Decide whether Python app local runs should keep the documented `python3`
   default on every OS or select a Windows-friendly default command. The
   current seam and tests preserve `python3` while allowing explicit `py`
   overrides.
2. Broaden launcher smoke coverage to non-C# launcher entry points where
   language toolchains are available without shell scripts.
3. Use the Linux Docker CI job as the first Tier 3 regression gate and improve
   provider/runtime unavailable diagnostics where failures are still generic.
4. Review host networking providers and document which operations are portable,
   macOS-specific, Linux-specific, or Windows-specific.
5. Add Resource Manager diagnostics for unsupported host/network/runtime
   operations before dispatch.

### Deferred

1. Windows-native container provider behavior beyond Docker Desktop or WSL.
2. OS secure-store integration for developer profiles.
3. OS-native service installation for daemonized Control Plane hosts.
4. Provider-backed DNS integration beyond local hosts-file/proxy behavior.

## Documentation Rules

When a feature, provider, launcher, or sample adds cross-platform behavior,
update the canonical feature/specification doc with:

- supported operating systems and runtimes,
- required toolchain versions,
- unsupported or degraded modes,
- provider-owned diagnostics and unavailable reasons,
- sample or CI coverage that proves the behavior,
- security notes for host-affecting or privileged operations.

Known non-parity should be recorded here or in the owning feature document. It
should not live only in test skips, local notes, or proposal text.
