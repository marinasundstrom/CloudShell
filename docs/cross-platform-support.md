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
regression. Docker CI starts with the
`Category=DockerIntegration&DockerCi=true` smoke subset. The full
`Category=DockerIntegration` suite remains the manual Tier 3 hardening target
until those heavier runtime, telemetry, SQL, and identity scenarios are stable
on hosted Linux runners.

Local development on macOS remains useful for fast iteration, but a change is
not cross-platform-ready until CI covers the relevant tier.

## Platform Abstraction Layer

Cross-platform behavior is implemented through small, purpose-owned platform
services resolved by capability and runtime requirements. The platform layer
should select or describe:

- host OS, distribution, and host capability signals;
- path-root, path-composition, and path-comparison semantics when behavior is
  not portable;
- process start information, command names, shell usage, quoting boundaries,
  and executable extension conventions;
- host-mutating operations such as hosts-file edits, DNS resolver refreshes,
  proxy setup, and privileged service operations;
- runtime prerequisite checks and unavailable reasons for Docker, Podman,
  local daemons, and provider-owned tools.

Do not concentrate this into one catch-all platform object. Prefer focused
descriptors, command factories, provider availability services, and
host-operation planners that can be injected and tested from any development
host. Linux distribution and installed-tool differences should be expressed as
capabilities and provider availability, not as broad assumptions that all Linux
hosts behave the same way.

## MVP Support Contract

The MVP target is not feature parity across every host mutation. It is a
portable Resource Manager and launcher baseline that can run on macOS, Linux,
and Windows while reporting clear unavailable reasons for host-specific
operations.

For MVP, cross-platform support is accepted when:

1. Tier 0 restore, build, and non-integration tests are green on macOS, Linux,
   and Windows.
2. CLI and launcher command construction uses injected platform/path/command
   descriptors instead of relying on the development host.
3. Host-affecting Resource Manager behavior resolves small platform services
   for OS, path target, tool availability, and command planning.
4. Privileged networking operations either execute through a provider-specific
   plan or return stable diagnostics without mutating host state.
5. Linux behavior is capability-driven. Distribution and installed-tool
   differences are detected through focused services such as tool resolvers,
   not through a single "Linux means X" branch.
6. Docker-backed sample smoke coverage has a deterministic Linux CI subset,
   while heavier runtime suites remain explicit manual or later-stage gates.

## Current Known Gaps

- CI now verifies the Tier 0 matrix on macOS, Linux, and Windows, but
  provider-owned runtime coverage is still uneven outside the current Linux
  Docker smoke subset.
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
    checks before running the `Category=DockerIntegration&DockerCi=true` smoke
    subset.
16. Moved macOS host-network provider support checks behind an injectable host
    OS descriptor and added deterministic tests for unsupported-platform
    diagnostics without requiring Linux or Windows locally.
17. Expanded the host OS descriptor to include a platform kind and optional
    Linux distribution ID for capability-specific planning.
18. Added a host tool resolver abstraction and a resolver-cache refresh planner
    so macOS, Windows, and Linux DNS refresh commands are selected by available
    tools before process invocation.
19. Moved Control Plane local hosts-file target selection behind the injected
    host OS descriptor.
20. Added deterministic tests for Linux resolver-cache tool selection,
    missing-tool diagnostics, unsupported-host planning, and Linux distribution
    descriptor normalization.
21. Moved host tool availability into the shared Resource Manager abstractions
    so provider runtimes and host networking use the same capability contract.
22. Added a shared container-host command platform for Docker-compatible
    runtimes that resolves Docker, Podman, and host-configured executables.
23. Centralized Docker/Podman process environment setup so Docker-compatible
    hosts use `DOCKER_HOST` and Podman hosts use `CONTAINER_HOST`.
24. Routed Docker container, SQL Server, and RabbitMQ local Docker command
    runners through the shared command platform and stable unavailable
    diagnostics before process invocation.
25. Added deterministic provider-runtime tests for Docker/Podman executable
    selection, custom executable diagnostics, and missing runtime command
    behavior without requiring Docker or Podman locally.

### Active

1. Use the green Tier 0 matrix as the regression gate for the remaining
   cross-platform slices.
2. Move remaining direct OS checks behind small injectable platform descriptors
   where the behavior can be unit-tested from any development host.
3. Continue the same testability pattern for remaining command factories and
   runtime prerequisites.
4. Audit remaining direct process invocations and path construction in
   provider-owned runtime code, starting with Docker, Podman, and executable
   tool prerequisites.
5. Move the larger local Docker container-application bridge to the shared
   container-host command platform without changing its orchestration behavior.

### Next

1. Decide whether Python app local runs should keep the documented `python3`
   default on every OS or select a Windows-friendly default command. The
   current seam and tests preserve `python3` while allowing explicit `py`
   overrides.
2. Broaden launcher smoke coverage to non-C# launcher entry points where
   language toolchains are available without shell scripts.
3. Use the Linux Docker CI job as the first Tier 3 regression gate and improve
   provider/runtime unavailable diagnostics where failures are still generic.
   Promote additional `Category=DockerIntegration` tests into `DockerCi=true`
   only after they are deterministic on hosted Linux runners.
4. Review the remaining host networking operations and document which
   operations are portable, macOS-specific, Linux-specific, or Windows-specific.
5. Add Resource Manager diagnostics for unsupported host/network/runtime
   operations before dispatch.
6. Add a provider prerequisite-check pattern for Docker/Podman/runtime-backed
   providers so missing host tools produce stable unavailable reasons before
   command execution.
7. Add container-app runtime tests for missing Docker/Podman prerequisites and
   custom executable metadata before promoting additional Docker integration
   tests into the CI smoke subset.

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
