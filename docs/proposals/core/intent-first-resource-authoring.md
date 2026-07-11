# Intent-first Resource Authoring

## Status

- Status: Proposed
- Strategy fit: Medium-high; it broadens authoring without making CloudShell
  code-centric or provider-centric.
- Canonical feature docs: None yet. The nearest current contracts are
  [Resource templates](../../resource-templates.md),
  [Programmatic resources](../../programmatic-resources.md),
  [ResourceDefinition structure](../../resource-definition-structure.md), and
  [Resource model providers](../../resource-model-providers.md).
- Remaining action: Define the first Resource Manager draft-template workflow
  and the minimal assistant context package needed to produce a starter app
  structure plus a validated `ResourceTemplate`.
- Out of scope: Autonomous deployment, provider-native infrastructure
  generation, secret-value creation, full application implementation
  generation, broad graph import/code generation, and replacing
  ResourceDefinition validation.

## Summary

CloudShell should support an intent-first authoring workflow where a user can
start building an app with AI assistance. The user describes the app and
environment they want in product language, CloudShell proposes the initial
application structure and resource model, and the assistant keeps helping as
the user refines the app. The durable CloudShell output should be a normal
`ResourceTemplate`/`ResourceDefinition` graph, not provider-native
infrastructure or a separate AI-owned model.

Conceptually, this is another CloudShell user interface: smarter and more
conversational than forms or YAML, but still a UI over the same Control Plane,
resource model, validation, authorization, and apply contracts.

The workflow is similar in spirit to AI-assisted design tools: the user starts
with a high-level idea, the product drafts a concrete starting point, and the
user reviews, adjusts, and iterates. For CloudShell, that starting point is a
small app workspace plus desired resource state that can be validated by
existing resource model providers and applied by the Control Plane.

## Problem

CloudShell currently has several authoring paths:

- YAML/JSON `ResourceTemplate` documents.
- `ResourceGraphBuilder` and language launcher builders.
- Resource Manager create/update forms.
- API and client-driven automation.

These are useful but still assume the user already knows the resource model,
resource type IDs, attribute names, relationship shape, endpoint concepts, and
provider capabilities. That is a poor starting point for a user who thinks in
application intent:

```text
I have an API, a React frontend, SQL Server, local storage, and I want the
frontend to call the API by name. Run it locally first, but keep the model
portable enough for a shared team environment later.
```

CloudShell should let that user start from the high-level description, get a
usable starting point, and continue refining the application structure without
turning the system into a free-form code generator or bypassing the Control
Plane.

## Goals

- Let users draft a starter app structure and resource graph from natural
  language, examples, repository context, or a short structured brief.
- Keep the generated artifact as ordinary CloudShell desired state:
  `ResourceTemplate` and `ResourceDefinition` entries.
- Support guided iteration after the first draft, such as adding a database,
  exposing an endpoint, introducing secrets, wiring service discovery, or
  converting a local-only resource into a shared-environment-ready shape.
- Show a topology preview, template diff, assumptions, unresolved questions,
  and provider validation diagnostics before apply.
- Preserve the same apply, validation, authorization, diagnostics,
  persistence, and provider boundaries used by hand-authored templates.
- Make the workflow useful for non-C# users and for users who do not want to
  start from code-first builders.
- Keep the feature provider-extensible: new resource types should become
  draftable by contributing schemas, examples, diagnostics, and authoring
  metadata rather than by hard-coding assistant behavior.

## Non-goals

- Do not let the assistant execute lifecycle actions, deploy workloads, or
  mutate runtime state without the normal apply and action paths.
- Do not make CloudShell an application-code authoring product. A first slice
  may create minimal starter project files or point to templates when that is
  required to make the resource graph runnable, but rich feature code belongs
  to developer tools and repositories, not the Control Plane.
- Do not emit secret values. The assistant may create secret references,
  identify missing secret decisions, or recommend a Secrets Vault resource,
  but values remain user- or provider-owned.
- Do not create a parallel AI-specific resource graph. The generated graph is
  still a ResourceTemplate.
- Do not generate provider-native artifacts such as Docker Compose,
  Kubernetes YAML, ARM/Bicep, Terraform, or cloud-specific deployment files as
  the first workflow. Those may be future import/export or deployment
  projection features.
- Do not treat this as MVP scope. It should wait until ResourceDefinition
  apply, Resource Manager validation, diagnostics, and supported samples are
  stable enough to make generated drafts trustworthy.

## Product Shape

The first user-facing workflow should be a Resource Manager authoring surface:

1. The user opens a "Start with AI" or "Draft from intent" flow.
2. The user provides a short description and optionally selects context:
   repository path, existing resources, current template, provider catalog, or
   sample pattern.
3. CloudShell proposes a starter app shape: projects, resource types,
   dependencies, endpoints, storage, configuration, secrets, and names.
4. CloudShell produces a draft template, topology preview, assumptions, and
   unresolved choices.
5. The Control Plane validates the draft through existing resource model
   providers.
6. Resource Manager shows diagnostics and a diff against current accepted
   state.
7. The user edits the draft, answers unresolved questions, or applies it
   through the normal template apply workflow.
8. As the app evolves, the user can ask for guided changes that produce
   additional template diffs and validation results instead of direct
   mutations.

The surface should feel like resource authoring, not chat as a destination.
The assistant is a drafting tool inside the authoring workflow.

## UI Boundary

Intent-first authoring should be treated as a smarter Resource Manager UI, not
as a new authority in the system. It can help users express intent, discover
available capabilities, propose a topology, and explain diagnostics, but it
does not own resource inventory, provider state, validation policy,
authorization, lifecycle actions, or deployment.

That boundary keeps the feature consistent with other CloudShell surfaces:

- Forms, YAML editors, launchers, CLI commands, SDK clients, and assistant
  drafting are all authoring surfaces over the same model.
- The Control Plane remains the owner of accepted state, validation,
  authorization, templates, apply plans, resource inventory, and operational
  records.
- Providers remain the owners of resource-specific validation, runtime
  materialization, diagnostics, and non-secret projection.
- Resource Manager remains responsible for review, diff, diagnostics, and
  user confirmation before apply.

## Assisted App-building Flow

Intent-first authoring has two related modes:

- **Starting point generation** creates a small app workspace and a first
  resource template. The workspace may use existing app templates or sample
  patterns, but CloudShell's durable concern is how that app is modeled,
  configured, connected, and operated as resources.
- **Guided evolution** helps the user change the app topology over time. A
  request such as "add durable SQL storage", "make the worker depend on the
  queue", or "expose the frontend with a local name" should produce a proposed
  resource-template diff, diagnostics, and any required app-configuration
  guidance.

The assistant should stay close to the resource model while still meeting the
user where they are. The user should be able to say what they want to build,
not which resource type ID or attribute name to use.

## Example

Input:

```text
Create a local development environment for an orders app. It has an ASP.NET
Core API, a React frontend, SQL Server with durable local storage, a
configuration store, and secrets. The frontend should call the API by name.
Expose the frontend locally.
```

Draft output:

- Optional starter workspace with API and frontend project skeletons, when the
  user is starting from an empty directory and has selected templates that
  CloudShell can safely create.
- `application.dotnet-app` resource for the API.
- JavaScript or container application resource for the frontend, depending on
  repository context and available providers.
- `sql.server` plus database child resource when the SQL provider is
  installed.
- `cloudshell.volume` for SQL durable storage.
- Configuration Store and Secrets Vault resources with references, not
  secret values.
- Endpoint requests and endpoint/name mappings for the frontend and API.
- Dependencies and service-discovery references between resources.
- Diagnostics for missing providers, ambiguous project paths, unresolved
  ports, or secrets that must be supplied by the user.

The exact type IDs, attributes, capabilities, and operations remain governed
by the installed provider catalog and resource model validation.

Follow-up input:

```text
Add a background worker that reads orders from the database and give it access
to the same secrets, but do not expose it publicly.
```

Follow-up output:

- A new worker resource definition.
- Dependency and identity/grant changes required for database and secret
  access.
- No public endpoint mapping because the worker is not user-facing.
- Diagnostics if the selected language/provider does not support the requested
  worker shape.
- A template diff and apply preview.

## Resource Model Boundary

Intent-first authoring must compile into the same desired-state shape used by
the rest of CloudShell:

- `ResourceTemplate` remains the user-facing desired-state envelope.
- `ResourceDefinition` remains the unit of authored resource intent.
- Resource type providers own type-specific validation and accepted
  attributes.
- Graph validators own cross-resource rules.
- Apply providers produce preview/apply plans.
- Resource Manager renders the proposed and accepted resource graph through
  existing projection surfaces.

The assistant may suggest names, dependencies, endpoints, capabilities,
references, and attributes. It does not decide whether those values are valid.
Provider validation and apply diagnostics decide that.

## Assistant Context

The assistant should receive a bounded context package, not unrestricted
access to the host:

- Installed resource classes and type definitions.
- Provider-authored examples and compact authoring hints.
- Allowed attribute names, value shapes, mutability, and secret-handling
  metadata.
- Existing resource graph and accepted template state when the user permits
  it.
- Repository inventory such as project files, package manifests, Dockerfiles,
  launch settings, and compose files when the user selects a repository.
- Starter template metadata for app project skeletons when the user starts
  from an empty directory.
- Current host capabilities, such as available container hosts, networking,
  identity providers, storage providers, and SQL providers.
- Product documentation snippets that define CloudShell concepts.

The context package should be explainable in the UI. Users should be able to
see which sources influenced the draft.

## Authoring Metadata

Provider packages need a way to contribute assistant-friendly authoring
metadata without making the assistant depend on provider implementation
classes. The first design pass should evaluate whether existing
`ResourceClassDefinition`, `ResourceTypeDefinition`, attribute definitions,
capabilities, operation declarations, and examples are enough. If not, add a
small provider-owned metadata surface that can describe:

- concise resource type purpose
- common authoring scenarios
- required and optional attributes
- relationship patterns
- endpoint and capability patterns
- security warnings
- examples that map user intent to ResourceDefinition shape
- known diagnostic remedies

This metadata should serve Resource Manager forms, CLI help, docs generation,
and assistant drafting. It should not exist only for AI.

## Validation And Apply

Generated drafts should pass through the same pipeline as hand-authored
templates:

1. Parse generated YAML/JSON into `ResourceTemplate`.
2. Resolve and normalize definitions.
3. Run type, capability, operation, and graph validators.
4. Produce diagnostics and an apply preview.
5. Show a resource diff and topology preview.
6. Apply only after explicit user confirmation and authorization.

Assistant output should be treated as untrusted input. Invalid or unsupported
suggestions become diagnostics, not exceptions or hidden automatic fixes.

## UI Projection

The first Resource Manager UI should include:

- intent input
- selectable context sources
- generated template editor
- topology preview
- starter files or template choices when a new app workspace is being created
- assumptions and unresolved questions
- validation diagnostics grouped by resource
- diff against current state
- apply button disabled until required diagnostics are resolved

The workflow should use normal Resource Manager navigation and Fluent UI
patterns. It should not introduce a separate assistant workspace unless later
shell-composition work proves a broader need.

## Security

- Never emit secret values into templates, logs, diagnostics, prompts, or
  assistant transcripts.
- Mark secret-like user input before it is sent to an assistant service.
- Prefer references to Secrets Vault entries and Configuration Store entries.
- Require explicit user consent before repository inventory, existing graph
  state, or logs are included in assistant context.
- Apply existing authorization checks before reading resources, templates,
  logs, traces, provider diagnostics, or repository-backed host context.
- Record provenance for generated drafts so users can audit what context was
  used.

## Diagnostics

Diagnostics should be first-class because the assistant will often draft a
nearly-correct model:

- missing provider
- ambiguous project or Dockerfile
- unsupported resource type
- missing container host
- missing identity provider
- unresolved endpoint or occupied port
- invalid name or provider-owned name restriction
- secret value required but not supplied through a safe path
- generated attribute rejected by provider validation
- dependency reference cannot be resolved

The diagnostic model should reuse existing resource model diagnostics where
possible. Assistant-specific diagnostics should explain draft assumptions and
missing context, not provider runtime behavior.

## Provider And Extension Parity

Future providers should be considered compatible with intent-first authoring
when they provide:

- resource type definitions and validation diagnostics
- examples or authoring hints for common intent
- safe secret and sensitive-attribute metadata
- relationship and dependency guidance
- Resource Manager projection and apply preview behavior
- CLI/template examples where relevant
- tests proving generated or example templates validate through the same
  provider contracts as hand-authored templates

Language launchers and SDKs should remain optional convenience layers. The
assistant may generate a ResourceTemplate first and later offer language-native
builder snippets as a derived view, but the builder snippet must not become
the source of truth.

## Implementation Slices

1. Document the context package and provider metadata requirements.
2. Add a non-AI "start from brief" prototype that maps structured choices to
   a starter app workspace and ResourceTemplate for one supported sample
   topology. This proves the Resource Manager review, validation, diff, and
   apply flow.
3. Add a guided-change path that can propose a template diff for one
   follow-up change, such as adding SQL storage or exposing an endpoint.
4. Add provider-authored examples/hints for built-in resource types used by
   Application Topology.
5. Add assistant-backed starting-point and guided-change generation behind an
   explicit preview flag.
6. Add repository-context inventory for selected project files and manifests.
7. Add contract tests using fixed assistant fixtures so validation behavior is
   deterministic.
8. Expand provider parity guidance as more resource types participate.

## Open Questions

- Should assistant context be assembled by a Control Plane service, a shell
  service, or a separate authoring service that calls the Control Plane
  through public managers?
- What is the minimal provider metadata surface that benefits forms, docs,
  CLI help, and assistant drafting without duplicating type definitions?
- Should generated drafts be stored as transient workspace artifacts before
  apply, or only as client-side editor state?
- Which starter app files, if any, should CloudShell create directly, and
  which should be delegated to language-native project templates?
- How should provenance and prompt history be persisted, if at all?
- Which model/provider abstraction should CloudShell use for self-hosted or
  air-gapped environments?
- Should the first version generate only YAML, or should it also generate a
  language launcher builder snippet as a secondary artifact?
