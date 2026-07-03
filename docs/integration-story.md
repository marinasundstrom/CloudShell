# Integration Story

CloudShell integrations should feel consistent whether they are authored from
C#, TypeScript/JavaScript, Java, or a future language. The implementation can
stay idiomatic to each ecosystem, but the user-facing contract should remain
the same: define resources, start or target a CloudShell host, apply a
`ResourceTemplate`, operate through the Control Plane API, and consume
CloudShell-managed services from the running workload.

This document is the umbrella contract for that experience. It links together
the detailed launcher, SDK, configuration, secrets, resource-template, and
resource-builder docs so new integrations can be evaluated against one minimum
expectation set.

## Integration Layers

CloudShell integrations have four distinct layers:

| Layer | Audience | Responsibility |
| --- | --- | --- |
| Resource authoring | Launcher projects, tests, templates, automation | Build `ResourceDefinition` values and `ResourceTemplate` documents through `ResourceGraphBuilder`, language-native builders, YAML, or JSON. |
| Launch and apply | Launcher packages and the CloudShell CLI | Start or select a host profile, pass host configuration, wait for readiness, apply the template, and report the CloudShell URL. |
| Control Plane client | Shell integrations, automation, launchers, authored services that need management APIs | Call domain-shaped Control Plane operations through `IControlPlane`, manager facets, or language-specific API clients instead of stores or provider internals. |
| Runtime service clients | Workloads started by CloudShell | Use Configuration Store, Secrets Vault, and future protected resource-service clients through injected endpoints and `CloudShellResourceCredential`-style credentials. |

The layers are deliberately separate. A launcher declares resources and starts
or targets a host; it is not the Control Plane. A runtime service client runs
inside an application after CloudShell starts it; it does not declare resources
or manage host lifetime.

## Minimum User Expectations

A useful language integration should support these workflows before it is
treated as more than a proof of concept:

1. Author a resource graph in the target language with typed resource handles
   for references, dependencies, endpoints, configuration bindings, secrets
   bindings, identity, and common lifecycle options.
2. Emit the graph as a `ResourceTemplate` without applying it so users can
   inspect, review, test, or commit the desired state.
3. Apply the template to an already-running local, split, or remote Control
   Plane by URL and credential.
4. Run a project-contained local development host in the foreground, apply the
   template after readiness, print the CloudShell URL, and stop the host when
   the launcher exits.
5. Start or reuse a daemon-style local host only through an explicit
   automation path, not as the default direct project run.
6. Use the same Control Plane API and authorization model for resource
   operations, logs, traces, monitoring, usage, and template apply that
   Resource Manager uses.
7. Let workloads consume Configuration Store and Secrets Vault through
   service-specific SDK clients, injected service endpoints, and resource
   identity credentials.
8. Avoid copying secrets, tokens, provider credentials, or runtime-only
   handles into templates, resource attributes, logs, launcher output,
   diagnostics, or persisted daemon state.

## Launcher Expectations

Launcher packages live under `Launchers/`. They should expose the same
lifecycle vocabulary while using the idioms of their language:

| Verb | Meaning |
| --- | --- |
| `template` or `toJson` | Emit the `ResourceTemplate` without applying it. |
| `apply` | Apply the template to an already-running Control Plane. |
| `start` | Start or reuse a daemon-style local host, then apply the template. |
| `run` | Start a foreground host process, apply the template, and keep host lifetime tied to the launcher command. |

Running a launcher project without an explicit verb should converge on `run`.
The default path should be project-contained: host appsettings, generated
templates, data directories, process state, and other local artifacts default
under the launcher project or an explicit project-local path.

Launcher appsettings configure the host profile. They are not resource
declarations and should not be copied into `ResourceTemplate` entries. Use
appsettings-compatible keys for host configuration even when the launcher
language does not normally use .NET configuration.

For C# launchers, AppHost `appsettings.json` is a first-class convention:
the launcher reads it through normal .NET configuration, exposes it as
`app.Configuration`, and forwards it to the local development host as delegated
host configuration. `CloudShell:Launcher:*` keys are reserved for launcher
mechanics; host settings keep their normal host paths.

Launchers may call the CloudShell CLI or the Control Plane API directly, but
they should not validate provider-specific resource semantics. Provider and
resource validation belongs to the Control Plane and installed provider
packages.

## Resource Builder Expectations

Every user-facing resource type should have a builder story for supported
launcher ecosystems. The stable contract is the `ResourceDefinition` shape;
builders are language bindings that make that shape safer and easier to
author.

Builder APIs should:

- use CloudShell nouns such as resource, endpoint, reference, dependency,
  identity, capability, operation, and template
- return reusable handles so relationships can be expressed without repeating
  string IDs
- preserve the distinction between service-discovery references and startup
  dependencies
- cover common attributes, configuration payloads, capability payloads,
  operation declarations, endpoint requests, metadata, and lifecycle options
- keep provider runtime implementation details out of the public authoring
  surface
- emit semantically equivalent `ResourceTemplate` output across languages for
  the same graph

C# builders currently provide the broadest coverage through
`ResourceGraphBuilder` and provider extension methods. TypeScript and Java
builders are experimental and hand-authored for the first launcher scenarios.
As the provider surface grows, provider metadata should become rich enough to
generate common builder wrappers where practical. Hand-authored wrappers remain
appropriate when a resource needs a more ergonomic language-specific shape.

## Control Plane Client Expectations

Integrations that manage or inspect resources should use the domain-shaped
Control Plane client boundary:

- `CloudShell.ControlPlane.Client` for .NET remote clients
- `IControlPlane`, `IResourceManager`, `IResourceTemplateManager`,
  `ILogManager`, `ITraceManager`, and related manager facets for shell and
  extension code
- generated or hand-authored clients in other languages that map back to the
  same domain concepts

Client libraries should hide HTTP route details behind resource, template,
log, trace, monitoring, and action methods. They should also use credential
objects or language-equivalent credential abstractions rather than passing raw
tokens through domain method calls.

Remote clients must preserve the resource projection shape, including
identity, type, class, lifecycle state, actions, capabilities, relationships,
ownership metadata, observability, endpoint mappings, and non-secret
attributes. Runtime-managed resources and provider-created resources must
round-trip through API/client projections without losing source, management,
visibility, owner, or cleanup metadata.

## Runtime Service Client Expectations

Runtime service clients live under `sdk/` and are used by workloads that
CloudShell starts. They should be small service-specific packages unless they
intentionally expose the full Control Plane domain surface.

Minimum service-client expectations are:

- share the `CloudShellResourceCredential` pattern or a language-equivalent
  credential object
- read injected endpoint variables for the referenced resource service
- request or attach bearer tokens through the resource identity credential
  path
- send direct calls to the protected service endpoint, not through private
  provider internals
- fail with useful unavailable or access-denied diagnostics without exposing
  secret values

Configuration Store and Secrets Vault define the current service-client
baseline. Workloads receive endpoint variables when they reference those
resources, receive identity credential variables when their provider can bind a
resource identity, then call the injected protected collection URL. When a
provider has only the service base URL, it builds the protected collection URL
with this route shape:

```text
GET <configuration-service-base-url>/api/configuration/stores/{resource-id}/entries
GET <secrets-service-base-url>/api/secrets/vaults/{resource-id}/secrets
```

Applications should use the SDK clients instead of constructing those URLs by
hand when a package exists. For .NET applications, the clients also provide
`IConfiguration` integrations. Configuration keys and secret names should use
the portable `--` hierarchy convention when they need to map cleanly across
configuration systems.

## Current Integration Inventory

| Surface | Current status |
| --- | --- |
| C# launcher | `Launchers/CSharp/CloudShell.AppHost.Launcher` reuses `ResourceGraphBuilder`, emits templates, and can apply/start/run against a host profile. |
| TypeScript launcher | `Launchers/TypeScript/cloudshell` is experimental, hand-authored for initial resource types, and proves template/apply/start/run behavior. |
| Java launcher | `Launchers/Java/cloudshell-launcher` is experimental, hand-authored for Java launcher apps, and separates `toJson`, `apply`, `start`, and `run`. |
| Go launcher | `Launchers/Go/cloudshell` is experimental, hand-authored for Go launcher apps, and separates `template`, `apply`, `start`, and `run`. |
| Control Plane .NET client | `CloudShell.ControlPlane.Client` maps the HTTP API back to public domain managers. |
| Configuration Store .NET client | `CloudShell.Configuration.Client` provides direct service calls and `IConfiguration` integration. |
| Secrets Vault .NET client | `CloudShell.Secrets.Client` provides direct service calls and `IConfiguration` integration. |
| TypeScript Configuration Store client | `sdk/typescript/configuration-client` is experimental and separate from the TypeScript launcher package. |
| Java runtime clients | `sdk/java/cloudshell` provides experimental Configuration Store and Secrets Vault clients for Java workloads. |

## Consistency Work To Close

The current implementations prove the integration pattern, but these items
should be made consistent before broader SDK stability claims:

- package and document a default local-development host path that does not
  require a repository checkout or `--host-project`
- make appsettings-style host configuration pass-through consistent across
  C#, TypeScript, Java, and Go launchers
- converge no-argument launcher execution on foreground `run`
- add parity tests that compare emitted `ResourceTemplate` shapes for the same
  graph across C#, TypeScript, Java, and Go
- define generated Control Plane client expectations for non-.NET languages
- standardize profile and credential discovery shared by CLI commands and
  launcher packages without persisting raw tokens in daemon state
- expand provider metadata so common builder wrappers can be generated or at
  least audited for drift
- keep launcher samples focused on running the language project, with sample
  shell scripts treated as repository conveniences rather than product
  guidance

## Related Docs

- [Launchers](launchers-and-app-hosts.md)
- [CloudShell CLI](cli.md)
- [SDK clients](sdk-clients.md)
- [Resource templates](resource-templates.md)
- [Programmatic resources](programmatic-resources.md)
- [ResourceDefinition structure](resource-definition-structure.md)
- [Configuration services](configuration-services.md)
- [Control Plane API](control-plane-api.md)
- [Resource model providers](resource-model-providers.md)
