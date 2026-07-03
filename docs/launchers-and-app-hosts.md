# Launchers

CloudShell uses **host profile** for the process that composes CloudShell
itself: Control Plane, Web UI, provider packages, runtime adapters,
authentication, persistence, and environment-level services. The built-in local
development host profile is `CloudShell.LocalDevelopmentHost`.

CloudShell uses **Launcher** as the feature name for language-specific
authoring programs that define a local-development resource graph, emit a
`ResourceTemplate`, and start or target a CloudShell host profile. A developer
may experience this as creating a local development host in their preferred
language, because running the launcher should bring up the local development
host and apply the declared resources. Architecturally, the launcher is still
the declaration and startup client. It is not the Control Plane, Web UI, or
provider runtime host, and it should not reference Control Plane stores,
provider runtimes, or UI hosting packages.

This naming keeps three responsibilities separate:

- **Host profile**: runs CloudShell and installs providers/runtime adapters.
- **Launcher**: declares resources, configures host startup, and applies the
  resource graph for a target environment.
- **Runtime service client**: runs inside an application workload and consumes
  CloudShell-managed services such as Configuration Store or Secrets Vault.

## Cross-Language Boundary

The stable cross-language boundary is the CloudShell Resource model:

- `ResourceTemplate`
- resource type IDs
- provider IDs
- resource IDs and references
- endpoint requests
- non-secret attributes
- metadata
- host launch and template apply behavior

Language SDKs should feel natural in their own ecosystem while preserving that
same model. C# can use extension methods over `ResourceGraphBuilder`.
TypeScript can use builder objects and object-literal options. Java should use
normal classes, fluent methods, records where useful, and package boundaries
that feel ordinary to Java developers. A Java API should not copy C# extension
method patterns directly.

## Default Developer Experience

The default launcher experience should match the way developers expect local
distributed app hosts to behave:

1. The developer runs the launcher project using the native command for that
   ecosystem, such as `dotnet run`, `npm start`, or the Java project runner.
2. The launcher reads the resource declarations from the project.
3. The launcher reads the launcher project's host appsettings and combines
   them with environment variables and explicit launcher options.
4. The launcher starts the local development host in the foreground with that
   delegated host configuration.
5. The launcher waits for the Control Plane to become ready.
6. The launcher applies the declared ResourceTemplate to that host.
7. The launcher prints the local CloudShell address.
8. The local development host remains tied to the launcher process lifetime.

That default run is project-contained. Host configuration, generated
templates, data directories, process handles, and other local state should
default under the launcher project or an explicitly configured project-local
path. Running a launcher project should not mutate or depend on a global
daemon.

## Configuration Scopes

Launcher integrations must keep three configuration scopes distinct:

| Scope | Purpose | Examples |
| --- | --- | --- |
| Launcher code | Declares the resource graph and resource relationships. | Java app, Configuration Store, Secrets Vault, dependencies, endpoint requests, health checks. |
| Launcher appsettings | Configures the local CloudShell host that will run for this project. | `Persistence`, `Identity:BuiltIn:Persistence`, `Authentication`, provider runtime paths, ports, `CloudShell:DataDirectory`. |
| ResourceTemplate | Describes the desired CloudShell resources accepted by the Control Plane. | Resource type IDs, provider IDs, resource IDs, non-secret attributes, references, dependencies, endpoints, metadata. |

Launcher appsettings are delegated to the local development host. They are not
resource declarations and should not be copied into every resource. For
example, a launcher project should be able to select SQLite or SQL Server
persistence for that project through appsettings while the ResourceTemplate
continues to describe only the resources CloudShell should manage.

For C# launchers, `CloudShellDistributedApplication.CreateBuilder(...)`
creates a normal .NET configuration view from the AppHost directory:
`appsettings.json`, `appsettings.{Environment}.json`, environment variables,
and command-line arguments. The builder exposes that configuration through
`app.Configuration` for sample-specific graph inputs such as local endpoint
ports. When the launcher starts a local development host, it forwards the
AppHost `appsettings.json` through `--host-settings`; the host then loads that
file into its own `IConfiguration`. Relative host paths such as
`CloudShell:DataDirectory` resolve relative to the AppHost settings file so
local state stays with the launcher project.

Settings under `CloudShell:Launcher` configure launcher mechanics such as the
default Control Plane URL, state directory, environment id, or custom host
project. Host-owned settings such as `Authentication`, `Persistence`,
`Identity`, provider runtime settings, and `CloudShell:DataDirectory` stay at
their normal host configuration paths.

When a language runtime does not naturally use .NET-style configuration, its
launcher package should still provide an appsettings-compatible input so teams
can move the same host-profile settings between C#, TypeScript, Java, and
future integrations. The launcher may translate that input into command-line
arguments, environment variables, a copied or temporary host configuration
file, or a future host-launch option, but the consumer-facing keys should
remain the host's CloudShell configuration keys.

## Non-Default Paths

The CloudShell CLI, daemon workflows, and sample shell scripts are not the
normal local launcher experience. They remain useful for advanced automation,
hosted or remote Control Plane instances, daemon management, CI, diagnostics,
and repository sample shortcuts. They should not be required when the goal is
to run a launcher project locally.

`template` or `toJson` is an explicit inspection/export path. It should not be
the default behavior of a launcher project that a developer runs directly.

## Current Implementations

Language launcher packages live under the repository's top-level `Launchers/`
folder:

- `Launchers/CSharp/CloudShell.AppHost.Launcher`
- `Launchers/TypeScript/cloudshell`
- `Launchers/Java/cloudshell-launcher`
- `Launchers/Go/cloudshell`

`CloudShell.AppHost.Launcher` is the current C# launcher authoring package. It
reuses Resource Model builders, emits a `ResourceTemplate`, and owns the
developer gesture of starting or targeting a host and applying the graph. It
does not reference Control Plane stores, provider runtimes, or UI hosting
packages.

`CloudShell.LocalDevelopmentHost` is the stable built-in local host profile for
launchers that do not need a custom CloudShell host process. It composes the
Control Plane, Web UI, built-in resource model providers, Resource Manager UI
integrations, local runtime adapters, and sample-friendly local persistence
settings. Custom host profiles are reserved for cases where the CloudShell host
process itself needs extra extensions, authentication, persistence, or
host-specific services.

The CloudShell CLI is an advanced automation and operations boundary, not the
normal local launcher experience. It can start or target hosts, apply
templates, and support hosted or remote Control Plane scenarios, but a
developer should not need to invoke the CLI or a sample shell script just to
run a local launcher project. See [CloudShell CLI](cli.md) for command
details.

Launcher packages and samples should use the same lifecycle vocabulary across
languages:

- `template` or `toJson`: emit the ResourceTemplate without applying it.
- `apply`: apply the template to an already-running Control Plane.
- `start`: start or reuse a daemon-style local host, then apply the template.
- `run`: start the host in the foreground, apply the template, and keep the
  host process tied to the launcher command lifetime.

The API shape can be idiomatic to each language. C# can use records and async
methods, TypeScript can use object-literal options and promises, and Java can
use ordinary option classes and fluent methods. The behavior behind those
verbs should remain consistent.

Resource relationships should keep the same meaning in every launcher.
Service-discovery references and startup dependencies are separate concepts:
references feed provider-specific binding projection, while dependencies feed
lifecycle ordering and `--start-dependencies` behavior.

## Launcher Conventions

All language launchers should preserve the same user experience even when the
declaration API is shaped for the host language:

- Running the launcher project with no explicit verb should follow the default
  developer experience above.
- The launcher package should be able to launch the local development host and
  apply the template itself. It may reuse shared libraries or Control Plane API
  clients, but it should not require the CloudShell CLI or sample shell scripts
  for the default local-development path.
- The launcher should write the local development host address after the host
  is ready and the template has been applied. The output should make the next
  action obvious, for example `CloudShell host: http://127.0.0.1:5100`.
- `run` should remain an explicit spelling for the same foreground behavior so
  scripts and documentation can be unambiguous.
- `template` or `toJson` should be an explicit inspection/export path. It
  should not be the default behavior of a launcher project that a developer
  runs directly.
- `apply` should require or infer a target Control Plane URL and should not
  start a host unless the caller chose a host-starting behavior.
- `start` can remain an explicit compatibility or automation path for
  workflows that want to start or reuse a recorded host and return after
  applying the template, but daemon behavior must not leak into the default
  project-contained launcher run.
- Launcher output should distinguish host startup, Control Plane readiness,
  template apply, and final URL. It should avoid dumping generated templates,
  access tokens, secrets, or provider-owned sensitive values during the normal
  `run` path.

The declaration API should also follow these conventions:

- A launcher app declares a resource graph and host-launch options. It does
  not compose the Control Plane or reference Control Plane stores, provider
  runtimes, or UI hosting packages.
- Resource handles should be reusable across declarations so references,
  dependencies, endpoint mappings, configuration bindings, and secrets
  bindings can be authored without repeating string IDs.
- Builder names can follow language norms, but the concepts should be stable:
  resource type, provider id, resource id, display name, attributes,
  references, dependencies, endpoints, health/liveness checks, logs, metadata,
  and lifecycle options.
- Service-discovery references and startup dependencies must be separately
  expressible. A helper can offer a combined convenience later, but the model
  should not make `reference` and `dependsOn` synonyms.
- ResourceTemplate output from different language launchers should be
  semantically equivalent for the same graph, even when ordering, formatting,
  or builder syntax differs.

Current launcher samples still expose explicit template commands for testing
and inspection. They should converge on the no-argument foreground `run`
default before the launcher packages are treated as stable. General
appsettings pass-through from launcher projects into the local development
host is also a required convention that still needs implementation across the
launcher packages. The sample `cloudshell.sh` scripts are temporary convenience
wrappers for repository samples; they should not define the product
experience. The stable experience is launching the language project that
declares the resources. How daemon-oriented workflows evolve is separate from
the launcher default.

### Future: File-Based C# Launchers

.NET file-based app support may allow a C# launcher to be authored as a single
source file instead of a project-backed AppHost. The target developer gesture
would be a small launcher file that declares the resource graph and runs
directly through the .NET CLI, with an optional `appsettings.json` beside the
file for the delegated host profile.

This scenario should preserve the same C# launcher contract:

- the C# file declares resources through the same Resource model builders
- `appsettings.json` beside the file configures the launched host profile
- `CloudShell:Launcher:*` settings still configure launcher mechanics
- generated templates, state, and default data roots stay beside the launcher
  file unless explicitly configured elsewhere
- the launcher discovers or defaults to `CloudShell.LocalDevelopmentHost`
  without source-referencing that host

The implementation question is source location discovery. Project-backed
launchers can use the AppHost project directory as the configuration and state
root. File-based launchers need an equivalent source-file directory convention,
preferably from .NET-provided file-based app metadata if available, with an
explicit override as a fallback. This is tracked as future launcher work; it
should not introduce a separate host composition model.

Future local-development auth may add a launcher-friendly URL form for simple
single-user scenarios, such as a loopback-only default token appended to the
printed URL. That must be opt-in or development-only, short-lived or scoped to
the local host profile, and must not appear in persisted resources, templates,
diagnostics, or remote/shared environment output.

The experimental TypeScript launcher package under `Launchers/TypeScript/cloudshell`
proves the non-C# authoring shape. It has hand-authored builders for the first
resource types, emits ResourceTemplate JSON, and can call the CLI apply path.
`samples/TypeScriptAppHost` exercises the end-to-end flow with
`CloudShell.LocalDevelopmentHost`.

The experimental Java launcher package under `Launchers/Java/cloudshell-launcher`
contains Java-native ResourceTemplate builders for Java launcher apps.
`samples/JavaAppHost` consumes that package from a small Java source-file
launcher, emits a ResourceTemplate, applies it through the CLI, and can run the
local host in the foreground for launcher-owned lifetime scenarios.

The experimental Go launcher package under `Launchers/Go/cloudshell` contains
Go-native ResourceTemplate builders for Go launcher apps. `samples/GoAppHost`
consumes that package from a small Go program, emits a ResourceTemplate,
applies it through the CLI, and can run the local host in the foreground for
launcher-owned lifetime scenarios. The package is launcher support only; the
`application.go-app` provider remains a C# CloudShell provider.

## Java Staging

The Java launcher builder is experimental. It owns the initial Java
ResourceTemplate authoring shape and the first Java process helpers for
`apply`, daemon-backed `start`, and foreground `run`.

The Java runtime service clients under `sdk/java/cloudshell` are a separate
surface. They are for Java applications that are already running and need to
consume CloudShell-managed services through injected environment bindings.
