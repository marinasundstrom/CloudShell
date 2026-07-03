# Cross-language local development

## Status

- Status: In progress.
- Strategy fit: High; local development must work from an installed CLI before
  broader language SDKs become mandatory.
- Canonical feature docs: [Launchers and app hosts](../../launchers-and-app-hosts.md),
  [CloudShell CLI](../../cli.md), [Programmatic resources](../../programmatic-resources.md),
  [Resource templates](../../resource-templates.md), and
  [SDK clients](../../sdk-clients.md).
- Remaining action: make `dotnet tool install -g CloudShell.Cli` enough to
  start or reuse the default local-development host daemon, then keep launcher
  packages aligned with that same host/profile and Control Plane API boundary.
- Out of scope: full SDK parity across all languages, external-format import,
  deployment projection, and remote provider package installation.

This proposal tracks the local-development authoring and launch experience for
CloudShell hosts controlled through the CLI and optional language-specific
launcher integrations. C# is the native implementation today, but it should
follow the same pattern as other integrations: author resource intent through
builder APIs where useful, exchange the graph through ResourceDefinition-based
templates, and let the Control Plane own validation, lifecycle, projection,
and operations. TypeScript/JavaScript, Java, Python, Go, and other ecosystems
should use the same contracts rather than reimplementing CloudShell.

The first implementation slices now include the CloudShell CLI,
`CloudShell.AppHost.Launcher`, `CloudShell.LocalDevelopmentHost`,
experimental TypeScript/JavaScript hosting packages and samples, Java app
resources, Java runtime service clients, and Java launcher samples. This
proposal remains the tracker for CLI distribution, default local-development
host packaging, remaining SDK hardening, package boundaries, generated
clients, profile/credential behavior, and future ecosystems.

## Problem

CloudShell should not require a developer to write C# just to compose and run a
local distributed application. The core host, Control Plane, and Blazor shell
can stay .NET-based, but the launcher entry point should be available to
teams whose application code and tooling live in another ecosystem.

The current programmatic resource API proves the code-first development model
from C#. That is useful, but it should not become a one-off hosting path that
other languages must imitate by reimplementing CloudShell. A TypeScript
application should be able to declare its web services, containers, databases,
configuration, secrets, networking, and references from TypeScript, start the
CloudShell host from the same development command, and then operate the
environment through Resource Manager and the Control Plane API. The same
integration pattern should also clarify the C# hosting libraries so C# remains
one language binding over the shared resource model instead of the special
case that defines it.

## Goals

- Let C#, TypeScript/JavaScript, and future language integrations define the
  same CloudShell resource graph.
- Keep one Control Plane resource model across C#, TypeScript, JavaScript, and
  future SDKs.
- Let a language SDK start or attach to a CloudShell host without becoming the
  Control Plane implementation.
- Preserve split hosting: the same SDK should be able to target an existing
  remote Control Plane instead of always launching a local combined host.
- Keep provider-owned runtime behavior behind Control Plane providers and
  runtime adapters.
- Make local development feel like one command from the user's ecosystem, such
  as `npm run dev`, while still exposing the normal Resource Manager UI and API.

## Non-goals

- Reimplement the CloudShell Control Plane in each language.
- Make a language SDK a parallel resource manager or lifecycle orchestrator.
- Add language-specific resource concepts that do not round-trip through the
  Resource model.
- Require a browser UI for headless local-development runs.
- Remove the C# hosting surface. C# remains the native implementation today,
  but should align with the same hosting-integration pattern used by other
  languages.

## Proposed model

Cross-language local development starts with the installed CloudShell CLI and
then grows language SDKs around it. The CLI is the common local automation
entry point, similar in role to Azure CLI: it can manage the local host
process, remember the active Control Plane endpoint, target local or remote
Control Plane hosts, perform common resource operations, apply resource
templates, configure selected local machine development affordances such as
hosts-file name mappings, and later own login/profile selection for
command-line workflows. Language SDKs can invoke the CLI or use the same
Control Plane API directly, but they should not reimplement
host/process/profile behavior differently in every ecosystem.

The default local-development path should not require a repository checkout,
custom host project, or launcher package:

```bash
dotnet tool install -g CloudShell.Cli
cloudshell control-plane start
cloudshell template apply ./cloudshell.template.yaml
cloudshell ui open
```

The installed CLI should carry or resolve a version-compatible
`CloudShell.LocalDevelopmentHost` profile and run it as a daemon when the user
selects the default local host. `--host-project` remains useful for repository
development and custom host profiles, but it should not be part of the normal
first-run experience.

The model uses three layers:

| Layer | Owner | Responsibility |
| --- | --- | --- |
| Language SDK | TypeScript/JavaScript, Java, or another ecosystem package | Provides fluent graph builders, local command integration, generated API clients, and ergonomic references for that ecosystem. |
| Host launcher | CloudShell CLI and optional language-specific launcher package | Starts or selects a known .NET CloudShell host profile, passes graph input and settings, watches readiness, and returns endpoint metadata to the SDK. The default local combined host profile is `CloudShell.LocalDevelopmentHost`; daemon startup belongs to the CLI, and foreground host-runner behavior can be added without changing the graph boundary. |
| Control Plane | CloudShell .NET host | Owns resource validation, provider setup, lifecycle actions, persistence, logs, traces, authorization, API projection, and Resource Manager UI. |

The SDK authors resource intent as a `ResourceTemplate` or equivalent
ResourceDefinition-based interchange document. The launcher gives that document
to a local Control Plane host or applies it to an existing Control Plane through
the API. Once the host is running, the SDK talks to the Control Plane API for
status, action execution, logs, and generated endpoint metadata.

The first practical distribution test is the installed CLI starting or reusing
the default local-development host daemon without a source checkout. Launcher
packages are the authoring convenience layer on top of that same path. A C#,
TypeScript, Java, or future launcher package can expose an app/graph root,
resource builders, typed references, endpoint helpers, provider-specific
extension helpers, and `template`/`apply`/`start`/`run` helpers. It still builds
a ResourceTemplate, calls the CloudShell CLI or Control Plane API, and uses
clients for status, logs, endpoints, and operations.

The SDK may offer ecosystem-native helpers:

```ts
const app = cloudshell.app("orders");

const db = app.sqlServer("main").database("orders");

const api = app.node("api", {
  command: "npm run dev --workspace api",
  port: 3000,
}).withReference(db);

const web = app.node("web", {
  command: "npm run dev --workspace web",
  port: 5173,
}).withReference(api);

await app.run();
```

Those helpers compile to the same provider-owned resource definitions a C# host
would produce. The Node SDK does not start resources directly unless a provider
explicitly models a Node process resource and the Control Plane has the
matching runtime adapter.

Builder parity is the main design cost. Every strongly typed resource helper
that exists in C# has an equivalent question in TypeScript: whether it should
be hand-authored, generated from provider metadata, or intentionally omitted in
favor of a lower-level `ResourceDefinition` helper. Provider-specific builders
and extension methods are useful because they hide raw attribute keys and
relationship payloads, but duplicating them manually in every language will
drift. The long-term shape should therefore make provider metadata rich enough
to generate common builder wrappers where practical, while still allowing
language packages to hand-author ergonomic helpers for resources whose
developer experience needs special treatment.

## Launch modes

### CLI-installed local daemon

The first supported local-development distribution path is:

1. Install the CloudShell CLI as a .NET global tool from NuGet.
2. Start or reuse the default local-development host daemon.
3. Apply a YAML or JSON `ResourceTemplate`.
4. Inspect resources, execute actions, and open Resource Manager through the
   CLI.

The CLI-owned daemon state records process ID, Control Plane URL, selected host
profile identity, selected data directory, and startup time. It must not record
credentials or secret values. When the CLI starts the packaged default host,
it forwards the selected URL, data directory, authentication settings, and
other host settings through the same configuration channels used by custom
host profiles.

This path is intentionally lower level than launcher authoring. It accepts
templates generated by hand, CI, a future import tool, or any language SDK. A
launcher package should be optional for users who only need to start
CloudShell locally and apply a template.

### Local combined host

The common host shape for local development starts a combined Control Plane
and CloudShell UI process. The default combined process is
`CloudShell.LocalDevelopmentHost`, which carries the built-in resource types,
Resource Manager UI integrations, and local runtime adapters. The CLI can
start this profile directly as a daemon. A language SDK or launcher uses the
same profile when it:

1. Builds a ResourceDefinition graph.
2. Starts or reuses `CloudShell.LocalDevelopmentHost`, or the configured custom
   CloudShell host profile, through the CLI/launcher path.
3. Waits for the Control Plane readiness endpoint.
4. Applies the graph as transient code-first declarations.
5. Opens or reports the Resource Manager URL.
6. Streams host status back to the invoking process.

The user experiences this as a normal ecosystem command. Custom host profiles
are only needed when the Control Plane/UI process itself requires extra
extensions, authentication, persistence, or host-specific services.

The default host profile should be versioned with the CLI distribution. When a
launcher depends on a newer template capability than the installed host
understands, the failure should be a clear host/profile compatibility
diagnostic rather than an obscure provider or API error.

### Control Plane only

Headless local runs can start only the Control Plane. This is useful for CLI
tests, CI smoke flows, editor integrations, or teams that want the UI in a
separate process.

### Attach to existing Control Plane

The SDK can target a running Control Plane by base URL and credential. In this
mode it does not launch a host; it applies or previews the graph through the
API and can query resources and endpoints afterward.

This is an important update and automation path. A TypeScript, JavaScript, or
other language tool can apply changes to an existing local host, split Control
Plane, team environment, or on-premise instance without owning process
lifetime. It should complement the default local development loop, where a
developer runs a host file and that process starts the Control Plane and UI
with its declared resources.

The first CLI accepts an explicit Control Plane URL. Later CLI profile work
should let users select a named Control Plane target, similar to how cloud CLIs
select an active subscription, tenant, or environment. That target can be a
local daemon, split local Control Plane, team-owned host, or on-premise
Control Plane.

### Export only

The SDK can emit the ResourceTemplate without applying it. This supports
review, CI validation, generated declarations, and future import/export flows.

## Interchange contract

The stable interchange is the Resource model, not a TypeScript-specific app
host schema. The initial implementation should prefer ResourceDefinition-based
templates because that is already the direction for C# programmatic resources,
Resource Manager apply, and graph import.

The interchange should include:

- resource names, types, display names, attributes, capabilities, and
  relationships
- endpoint intent and endpoint-network mapping intent
- references and dependencies
- provider-owned non-secret configuration payloads
- startup policy such as programmatic autostart and dependency autostart
- source metadata that identifies the declaring SDK, project path, and file
  where useful for diagnostics

The interchange must not include secret values. SDKs should pass secrets by
reference to environment variables, local secret stores, CloudShell Secrets
Vault entries, or provider-owned protected channels.

## SDK expectations

The first TypeScript/JavaScript SDK should provide:

- resource graph builders that mirror the C# builder vocabulary where practical
- provider-specific builder wrappers for the first supported resource types,
  initially hand-authored if needed, with generation from provider metadata as
  the preferred long-term direction
- typed references between declared resources
- generated Control Plane API client bindings
- launch helpers for local combined-host and Control Plane-only modes
- attach helpers for remote Control Planes
- clear diagnostics when the local .NET host, provider package, or runtime
  adapter is missing
- package scripts that make `npm run dev` or equivalent workflows natural

The SDK should avoid hiding CloudShell concepts behind Node-only terms. Use
CloudShell nouns such as resources, endpoints, dependencies, references,
Control Plane, and Resource Manager.

### JavaScript frontend and TypeScript applications

JavaScript app authoring needs a frontend-aware path, but CloudShell should not
hard-code one frontend framework into the base resource model. Vite, Next.js,
Remix, Angular, Vue, React tooling, workers, and future build systems can all
have different development commands, dev server behavior, bundle outputs,
environment-variable conventions, routing assumptions, and hot reload
mechanics. The TypeScript/JavaScript SDK should expose ergonomic helpers for
common frameworks while preserving the same ResourceDefinition interchange
used by other resource types.

Plain Node.js server applications can use the base JavaScript app resource and
may run TypeScript files directly when the selected Node.js version and project
configuration support it. Browser-focused frontend applications usually need a
build engine or framework development server to transform and bundle the app.
Those build engines should be modeled as SDK/provider/runtime-adapter concerns:
they choose the command, watch behavior, endpoint projection, environment
mapping, and produced artifacts without adding bundler-specific fields to the
core Control Plane resource model.

Hot reload should follow the same rule. The Control Plane owns resource
identity, lifecycle operations, logs, health, endpoint projection, references,
and authorization. The selected JavaScript runtime adapter starts the
framework's development process and lets the framework's watcher or HMR server
reload browser assets or server modules while CloudShell keeps the resource
graph stable.

## Host launcher expectations

The CloudShell CLI launcher is responsible for process concerns:

- finding, carrying, or installing the selected CloudShell host profile
- passing appsettings, environment variables, graph files, and port settings
- detecting readiness and failure
- reporting the Control Plane and Resource Manager URLs
- forwarding shutdown signals
- preserving logs needed to diagnose host startup failures

For the default local-development path, the CLI should be able to run the
version-compatible `CloudShell.LocalDevelopmentHost` without requiring
`--host-project`. Implementation options include bundling the host profile
with the tool, resolving a companion host-profile artifact, or starting an
internal host command from the installed tool. The product contract is the
same in each case: the user installs the CLI, then the CLI can start the
default local host daemon.

The launcher should not validate provider-specific resource semantics itself.
It may validate launcher arguments and template envelope shape, then leave
resource validation to the Control Plane and providers.

The CLI can also own local machine integration commands that are outside the
Control Plane API boundary, such as adding development host-name mappings to a
hosts file. Those commands should be explicit and permission-aware. When a
system file requires elevated privileges, the CLI should fail with a clear
message or support `--dry-run`/custom file targets; it should not silently run
`sudo`.

## Identity and credentials

The CLI and language SDKs should call the Control Plane API using the same
identity model as other remote clients. A local unauthenticated host can still
exist for quick development, but the CLI surface should not assume that
localhost means unauthenticated.

The initial CLI can accept an explicit bearer token or read one from an
environment variable. That keeps authenticated Control Plane calls possible
without prematurely defining a credential store. Tokens and secrets must not be
persisted in daemon state.

Later work should standardize a CloudShell credential/profile store, similar in
role to Azure CLI's local profile. That store should let commands resolve the
active account, selected Control Plane target, selected environment, default
subscription/project equivalent if CloudShell adds one, and credential
material from one well-known location. The store design must define secret
storage, target selection, profile selection, logout behavior, and how language
SDK launchers discover the same active profile.

## Resource Manager behavior

Resources declared from TypeScript or JavaScript should look like ordinary
programmatic declarations in Resource Manager. The UI may show source metadata
such as "Declared by TypeScript app host" when useful, but source language must
not change the resource model, lifecycle behavior, authorization, endpoint
mapping, logs, traces, or persistence semantics.

Persisting a graph follows the same rule as C# declarations: `Persist()` or its
SDK equivalent records accepted resource state into the Control Plane. It is
not deployment to another environment.

## Open questions

- Should the default local-development host be physically bundled into the
  `CloudShell.Cli` tool package, resolved as a companion NuGet package, or run
  through an internal CLI host command?
- Which parts of the first `cloudshell` CLI daemon, template apply, resource,
  and UI commands should become stable before language SDKs start depending on
  them?
- Should CloudShell add a supported Control Plane-only host profile next to
  `CloudShell.LocalDevelopmentHost`?
- How should the launcher discover provider packages required by a graph?
- Should SDKs apply graphs only through the Control Plane API, or may a local
  launcher pass an initial graph to the host before the API is ready?
- How much of the C# builder API should be generated from provider metadata
  versus hand-authored in each language package?
- Which provider metadata is needed to generate language SDK resource
  builders, extension helpers, typed attributes, references, endpoint helpers,
  and diagnostics without forcing each language package to manually mirror the
  C# wrappers?
- Which JavaScript frontend framework helpers should ship first, and how
  should framework-specific hot reload, TypeScript, bundling, and dev server
  behavior be described without leaking build-tool details into the core
  resource model?
- How should editor tooling surface source spans from generated
  ResourceDefinition diagnostics?
- What is the CloudShell credential/profile store layout, and which OS-native
  secure storage backends should protect token material?

## Implementation plan

The current CLI, C# launcher, local host profile, experimental TypeScript
hosting package, Java app resource, Java service clients, and Java launcher
sample are documented in [Launchers](../../launchers-and-app-hosts.md),
[CloudShell CLI](../../cli.md), [SDK clients](../../sdk-clients.md),
[JavaScript applications](../../resources/javascript-applications.md), and
[Java applications](../../resources/java-applications.md).

Remaining implementation work:

1. Package the CloudShell CLI as a NuGet-distributed .NET global tool with a
   default local-development host path that does not require a repository
   checkout or `--host-project`.
2. Add daemon tests for starting, reusing, stopping, and diagnosing the
   packaged/default `CloudShell.LocalDevelopmentHost` path, including log
   capture and version/profile compatibility failures.
3. Update C#, TypeScript, and Java launcher packages so their default `start`
   and `run` helpers prefer the installed CLI/default host profile while
   keeping explicit custom host-profile options.
4. Add Control Plane diagnostics for source metadata and missing provider or
   runtime adapter capabilities.
5. Expand TypeScript builders only where current samples prove the need:
   container apps, secrets, SQL Server, richer endpoint references, and remote
   attach helpers.
6. Decide whether generated Control Plane client bindings are required before
   broader SDK hardening.
7. Define the CLI profile and credential store before persisting token material.
8. Decide which SDK APIs are stable enough to document as public preview.

## Verification

The first implementation should include:

- packaging checks for the .NET global tool and packaged/default local host
  startup path
- CLI daemon tests for host startup, readiness failure, reuse, shutdown, and
  stale state handling
- unit tests for TypeScript graph emission
- launcher tests proving C#, TypeScript, and Java helpers converge on the same
  CLI/default-host behavior
- Control Plane contract tests for applying externally authored templates
- a sample smoke test that runs a TypeScript-authored app graph through the
  supported local-development host
- docs that show local combined-host, headless Control Plane, attach, and
  export-only workflows
