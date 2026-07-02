# Cross-language local development

## Status

Proposed.

This proposal tracks the local-development authoring and launch experience for
CloudShell hosts that are controlled through language-specific hosting
integrations. C# is the native implementation today, but it should follow the
same pattern as other integrations: author resource intent through builder
APIs, exchange the graph through ResourceDefinition-based templates, and let
the Control Plane own validation, lifecycle, projection, and operations. The
first additional target is TypeScript/JavaScript because it is the closest
comparison point to Aspire's TypeScript app-host work, but the model should
apply to Java, Python, Go, and other ecosystems through the same contracts.

## Problem

CloudShell should not require a developer to write C# just to compose and run a
local distributed application. The core host, Control Plane, and Blazor shell
can stay .NET-based, but the app-host style entry point should be available to
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

Cross-language local development starts with the CloudShell CLI and then grows
language SDKs around it. The CLI is the common local automation entry point,
similar in role to Azure CLI: it can manage the local host process, remember
the active Control Plane endpoint, target local or remote Control Plane hosts,
perform common resource operations, apply resource templates, configure
selected local machine development affordances such as hosts-file name
mappings, and later own login/profile selection for command-line workflows.
Language SDKs can invoke the CLI or use the same Control Plane API directly,
but they should not reimplement host/process/profile behavior differently in
every ecosystem.

The model uses three layers:

| Layer | Owner | Responsibility |
| --- | --- | --- |
| Language SDK | TypeScript/JavaScript, Java, or another ecosystem package | Provides fluent graph builders, local command integration, generated API clients, and ergonomic references for that ecosystem. |
| Host launcher | CloudShell CLI | Starts a known .NET CloudShell host profile, records daemon state, passes graph input and settings, watches readiness, and returns endpoint metadata to the SDK. |
| Control Plane | CloudShell .NET host | Owns resource validation, provider setup, lifecycle actions, persistence, logs, traces, authorization, API projection, and Resource Manager UI. |

The SDK authors resource intent as a `ResourceTemplate` or equivalent
ResourceDefinition-based interchange document. The launcher gives that document
to a local Control Plane host or applies it to an existing Control Plane through
the API. Once the host is running, the SDK talks to the Control Plane API for
status, action execution, logs, and generated endpoint metadata.

The first practical hosting-story test can be a TypeScript package published
for the Node package manager. That package should expose an API shaped like the
C# programmatic resource API: an app/graph root, resource builders, typed
references, endpoint helpers, provider-specific extension helpers, and a
`run`/`apply` path that launches or attaches to a CloudShell host. The package
does not need to execute the Control Plane; it can build a ResourceTemplate,
call the CloudShell CLI or Control Plane API, and then use generated clients
for status, logs, endpoints, and operations.

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

### Local combined host

The common local-development mode starts a combined Control Plane and
CloudShell UI process. The language SDK:

1. Builds a ResourceDefinition graph.
2. Starts the configured CloudShell host profile through the launcher.
3. Waits for the Control Plane readiness endpoint.
4. Applies the graph as transient code-first declarations.
5. Opens or reports the Resource Manager URL.
6. Streams host status back to the invoking process.

The user experiences this as a normal ecosystem command. The implementation is
still the same CloudShell combined host described in the hosting model.

### Control Plane only

Headless local runs can start only the Control Plane. This is useful for CLI
tests, CI smoke flows, editor integrations, or teams that want the UI in a
separate process.

### Attach to existing Control Plane

The SDK can target a running Control Plane by base URL and credential. In this
mode it does not launch a host; it applies or previews the graph through the
API and can query resources and endpoints afterward.

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

- finding or installing the selected CloudShell host profile
- passing appsettings, environment variables, graph files, and port settings
- detecting readiness and failure
- reporting the Control Plane and Resource Manager URLs
- forwarding shutdown signals
- preserving logs needed to diagnose host startup failures

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

- Which parts of the first `cloudshell` CLI should become stable before
  language SDKs start depending on them?
- Which host profiles should be shipped as supported defaults for local
  combined-host and Control Plane-only runs?
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

1. Add the first `cloudshell` CLI with `control-plane start|stop|status`,
   common `resource` operations, local host-name mapping helpers, and
   `template apply` over the Control Plane API.
2. Define the launcher contract and ResourceTemplate envelope expected by
   external language SDKs.
3. Add a TypeScript SDK POC as a Node package manager package with an API
   shaped like the C# programmatic resource API. The first package should prove
   a hand-authored builder path for JavaScript apps, Configuration Store,
   default networking, typed references, endpoint requests, health checks,
   JSON ResourceTemplate emission, and CLI apply integration before expanding
   to ASP.NET Core or generic process apps, container apps, secrets, SQL
   Server, and richer endpoint references.
4. Add Control Plane diagnostics for source metadata and missing provider or
   runtime adapter capabilities.
5. Add sample coverage for a TypeScript-authored local distributed app.
6. Add a JavaScript frontend sample that proves framework dev-server endpoint
   projection, TypeScript support, and hot reload behavior through a
   provider-owned runtime adapter.
7. Add remote attach support using generated Control Plane client bindings.
8. Decide which SDK APIs are stable enough to document as public preview.

## Verification

The first implementation should include:

- unit tests for TypeScript graph emission
- launcher tests for host process startup, readiness failure, and shutdown
- Control Plane contract tests for applying externally authored templates
- a sample smoke test that runs a TypeScript-authored app graph through the
  supported local-development host
- docs that show local combined-host, headless Control Plane, attach, and
  export-only workflows
