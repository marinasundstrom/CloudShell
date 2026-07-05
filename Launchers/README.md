# CloudShell Launchers

Launchers are language-specific packages and samples for declaring CloudShell
resources from application code and starting or targeting a CloudShell host.

A launcher builds a ResourceTemplate, then starts or targets a CloudShell host
profile and applies the template. That target can be the local development
host, a custom host profile, or a separate remote Control Plane. The launcher
itself is not the Control Plane and should not reference Control Plane stores,
UI hosting packages, or provider runtime implementations.
From a user's perspective, running a launcher can feel like creating a local
development host in the language they prefer, but the feature boundary remains
the launcher: resource declaration, host startup configuration, and template
apply.

Current launcher packages:

- `CSharp/CloudShell.AppHost.Launcher`
- `TypeScript/cloudshell`
- `Java/cloudshell-launcher`
- `Go/cloudshell`
- `Python/cloudshell`

## Default Run Contract

Running a launcher project directly should:

1. Read the resource declarations from that project.
2. Read the launcher project's appsettings-style host configuration.
3. Start the project-contained local development host in the foreground.
4. Delegate the effective host configuration to that host.
5. Wait for the Control Plane to become ready.
6. Apply the generated ResourceTemplate.
7. Print the local CloudShell address.
8. Stop the host when the launcher process exits.

Default launcher runs should use project-local configuration and state by
default and should not affect a global daemon.

## Configuration Scopes

- Launcher code declares resources and relationships.
- Launcher appsettings configure the local CloudShell host for this project,
  including persistence, authentication, provider runtime paths, ports, and
  `CloudShell:DataDirectory`.
- ResourceTemplates describe CloudShell resources. They do not carry host
  process settings.

## Commands

Launchers should expose consistent lifecycle verbs:

- `template` or `toJson` emits the ResourceTemplate.
- `apply` targets an already-running Control Plane.
- `start` may start or reuse a daemon-style local host before applying.
- `run` owns a foreground host process for the launcher command lifetime.

Running a launcher project without an explicit verb should be equivalent to
the foreground `run` path. Use an explicit `template` command when the goal is
only to inspect or export the generated ResourceTemplate.

The CloudShell CLI and sample shell scripts remain useful for advanced
automation, hosted/remote Control Plane scenarios, and repository sample
shortcuts. They should not be required for the default local launcher
experience.

Launchers should also keep resource relationship semantics consistent:
references are for provider-specific binding and discovery, while dependencies
are for lifecycle ordering and dependency startup.

Runtime service clients stay under `sdk/`. For example,
`sdk/java/cloudshell` is used by Java applications after CloudShell starts
them, while `Launchers/Java/cloudshell-launcher` is used by Java launcher code
that declares resources.

See [Launchers](../docs/launchers-and-app-hosts.md) for the
terminology, host profile boundary, and cross-language parity expectations.
