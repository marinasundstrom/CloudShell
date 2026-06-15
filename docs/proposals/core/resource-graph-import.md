# Resource Graph Import and Code Generation Proposal

## Status

Proposed.

Resource graph import should help users bring an existing application topology
into CloudShell without making provider-native files the long-term source of
truth. Docker Compose YAML is the proposed first source format because it is a
common local-development description for multi-container applications and maps
reasonably well to CloudShell container apps, volumes, networks, endpoints, and
dependencies.

The preferred first user experience is code generation: parse an external graph
file, show a reviewed CloudShell resource graph, and produce C# programmatic
declarations that can be pasted into the host application. Direct UI import and
programmatic import APIs should use the same translator and diagnostics, but
they should not be the only path.

## Problem

Many teams already have Docker Compose files or similar local environment
descriptions before they adopt CloudShell. Today, moving those graphs into
CloudShell requires hand-translating services, images, ports, volumes,
networks, environment variables, and dependencies into programmatic resource
declarations or Resource Manager create flows.

That creates several risks:

* users may model runtime containers instead of stable container applications
* provider-native concepts may leak into the CloudShell resource model
* UI-created resources and code-first declarations may diverge
* unsupported Compose features may be silently lost
* secrets may be copied into generated artifacts
* users may not understand which parts are CloudShell intent and which parts
  are provider-owned runtime detail

CloudShell needs a deliberate import path that treats external formats as input
dialects, not as first-class CloudShell resource models.

## Goals

* Import resource graph intent from external files, starting with Docker
  Compose YAML.
* Translate imported graphs into CloudShell resource concepts before creating
  resources or generating code.
* Make generated C# programmatic declarations the preferred first output.
* Support a Resource Manager UI flow that can preview, diagnose, edit, and then
  either copy generated declarations or create resources in the Control Plane.
* Support a programmatic API for tools, samples, tests, and future command-line
  workflows.
* Reuse the same translation, validation, and diagnostics across UI,
  programmatic, and API scenarios.
* Preserve provider-owned configuration boundaries and avoid storing raw
  external files as stable CloudShell state.
* Report unsupported or lossy mappings as diagnostics before any resource is
  created.

## Non-Goals

* Do not make Docker Compose YAML a stable CloudShell resource definition
  format.
* Do not require lossless round-tripping between Compose and CloudShell.
* Do not make imported Compose services the same thing as Docker runtime
  container resources.
* Do not copy secret values into generated code, resource attributes,
  templates, diagnostics, or logs.
* Do not introduce UI-only import concepts that bypass the resource domain
  model.
* Do not build a general-purpose Compose orchestrator replacement as part of
  import.
* Do not attempt automatic continuous synchronization from source files in the
  first version.

## Product Shape

The import flow should have three distinct stages:

1. Parse the source file into a format-specific model.
2. Translate that model into a CloudShell resource graph draft.
3. Choose an output: generated declarations, direct resource creation, or a
   resource group template.

The draft graph is the central artifact. It should contain CloudShell resource
intent, dependencies, warnings, errors, suggested IDs, and source-location
metadata for diagnostics. It should not be a projected `Resource`, because it
describes proposed state before creation. It also should not be a provider
configuration dump, because import needs to explain the CloudShell model before
committing anything.

```text
Docker Compose YAML
    -> Compose parser
    -> CloudShell graph draft
    -> diagnostics and review
    -> generated C# declarations
       or Resource Manager create
       or resource group template
```

## Recommended First Slice

The first slice should be a documentation and design-backed translator for
Docker Compose to generated CloudShell declarations. It should not create a new
resource type.

The minimal useful output is a code block like:

```csharp
controlPlane.Resources(resources =>
{
    var api = resources
        .AddContainerApplication("application:api", "API", "ghcr.io/example/api:latest")
        .WithEndpoint("http", targetPort: 8080)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

    var redis = resources
        .AddContainerApplication("application:redis", "Redis", "redis:7")
        .WithEndpoint("redis", targetPort: 6379);

    api.DependsOn(redis);
});
```

The exact builder calls should follow the current provider APIs. If an imported
feature has no CloudShell declaration helper yet, the generator should emit a
diagnostic and either omit that feature or use the lowest-level supported
declaration API with an explicit comment.

## Docker Compose Mapping

The first translator should prefer these mappings:

| Compose concept | CloudShell graph intent |
| --- | --- |
| `services.*.image` | `application.container-app` image |
| `services.*.build` | unsupported diagnostic first; later project/build descriptor when available |
| `services.*.container_name` | display-name hint only, not stable runtime identity |
| `services.*.ports` | resource endpoints and optional host/public endpoint intent |
| `services.*.expose` | internal endpoint intent |
| `services.*.environment` | app environment variables; secret-looking values produce warnings |
| `services.*.env_file` | diagnostic or configuration-store import hint |
| `services.*.depends_on` | `DependsOn(...)` relationships |
| `services.*.volumes` | volume mounts; named volumes become `cloudshell.volume` candidates |
| top-level `volumes` | `cloudshell.volume` resources where a portable filesystem intent can be inferred |
| top-level `networks` | CloudShell network or virtual-network candidates |
| `services.*.networks` | network membership or dependency intent |
| `services.*.healthcheck` | future health-check intent; diagnostic until modeled |
| `secrets` | Secrets Vault placeholders or diagnostics, never literal secret export |
| `configs` | configuration-store placeholders or diagnostics |
| `profiles`, `extends`, `include`, anchors | diagnostics unless the parser fully resolves them |
| labels | ignored by default unless a known CloudShell/import label namespace is defined |

Service entries should normally become container app resources, not Docker child
container resources. Runtime containers remain provider-owned implementation
details unless the user explicitly chooses a low-level Docker inspection model.

## UI Scenarios

Resource Manager should eventually provide an import page or action that lets a
user upload or paste a supported source file. The UI should show:

* detected source format and version
* translated CloudShell resources
* dependencies, endpoints, volumes, networks, and secret/configuration hints
* warnings and errors grouped by source location
* generated C# declarations as the primary output
* an optional create-resources path when the environment allows UI mutation

The UI should make code-first ownership clear. In a host where resources are
normally declared in code, the safest path is to generate declarations and let
the operator add them to the host. In a mutable environment, the same draft may
be applied through Resource Manager after review.

Read-only Resource Manager mode should still allow parse, preview, diagnostics,
and code generation. It should not allow direct resource creation.

## Programmatic and API Scenarios

Programmatic import should expose a service or manager shape that accepts
source content, source format, and import options, then returns a graph draft
with diagnostics and optional generated declaration text.

The Control Plane API can project the same operation for split-hosted UI and
remote tooling. A remote UI should not need file-format parser code locally if
the Control Plane owns installed translators. The API should return stable
diagnostics and draft graph data instead of throwing for expected unsupported
input.

Direct apply should be a separate command from translation. That keeps preview,
code generation, and validation side-effect free.

## Relationship to Resource Templates

Resource templates are CloudShell-to-CloudShell group import/export artifacts.
Resource graph import is external-format-to-CloudShell translation.

The two features can meet at the graph draft stage:

* a Compose file may translate to a resource group template for direct import
* a resource group template may generate C# declarations in the future
* both flows should share diagnostics where they validate the same resource
  graph rules

Templates should remain the native portable CloudShell exchange format. Docker
Compose should remain an input dialect.

## Relationship to Deployment Projection

Deployment projection transforms a CloudShell graph into provider-specific
deployment artifacts such as Docker Compose, Kubernetes manifests, or Bicep.
Resource graph import does the reverse direction for selected source formats.

The two directions should share terminology and compatibility diagnostics where
possible, but they should not promise round-trip fidelity. A Compose file
imported into CloudShell and later projected back to Compose may produce a
different but equivalent deployment artifact because CloudShell is the source
model after import.

## Diagnostics

Import diagnostics should be source-aware and actionable:

* `Error` blocks generation or apply when a required mapping is impossible.
* `Warning` allows generation but calls out lossy, unsafe, or unsupported
  behavior.
* `Info` explains inferred CloudShell choices such as generated resource IDs or
  default network selection.

Diagnostics should include source path information where possible, such as
`services.api.ports[0]`, without requiring the UI to understand the source file
format.

## Implementation Notes

The first implementation should use a maintained YAML parser rather than
hand-written parsing. The parser and source-format model belong behind a
translator boundary, likely in a Docker Compose capability package or import
provider. The public abstraction should describe source formats, graph drafts,
diagnostics, generated declarations, and apply commands in CloudShell terms.

Generated code should use the current public declaration builders, stable
resource IDs, invariant formatting, and explicit references between builder
variables where possible. It should avoid writing secrets and should include
small comments only where an imported feature was intentionally skipped or
needs manual completion.

## Remaining Tasks

* Define the graph draft abstraction and diagnostic model.
* Decide whether the public entry point is an `IResourceGraphImportManager`, a
  resource-template extension, or a narrower preview service.
* Define translator registration and source-format discovery.
* Design Docker Compose parsing and mapping options.
* Add generated C# declaration output for the supported mapping subset.
* Add Resource Manager preview/code-generation UI.
* Add a direct apply path only after preview and code generation are proven.
* Decide whether generated resource group templates should be part of the first
  supported output.
* Add tests for Compose mapping, diagnostics, generated declarations, and API
  projection when the transport surface exists.

## Open Questions

* Should generated declarations be emitted as a full `controlPlane.Resources`
  block, a method body, or smaller snippets grouped by resource type?
* Should direct UI apply default to transient declarations, persisted provider
  state, or resource group template import?
* How should Compose `build` entries map once CloudShell has a richer build and
  deployment story?
* Which Compose features should become hard errors instead of warnings?
* Should import provenance be stored after direct apply, or only shown during
  preview?
* Should external-format import live in the Control Plane only, or can some
  translators run in client tooling without drifting from Control Plane
  validation?
