# Resource Definitions and Capability Providers Proposal

## Status

Proposed.

CloudShell already distinguishes projected resources from declared resources in
the resource model documentation, and several providers already carry typed
definition records such as application, storage, volume, network, service, DNS,
and load-balancer definitions. This proposal tracks the next model step:
formalizing `ResourceDefinition` as resource intent and formalizing capability
providers as attached behavior over that intent.

## Problem

`Resource` is the current known projection of a managed artifact. It is what
Resource Manager, the Control Plane API, providers, and remote clients can
inspect after provider behavior has accepted, normalized, or observed resource
state.

Resource declarations, templates, persisted state, imports, and create flows
need a different artifact. They describe desired resource intent before the
provider projects it as a `Resource`. Today that intent exists in several
partly overlapping forms:

- programmatic `ResourceDeclaration`
- provider-specific typed records such as `ApplicationResourceDefinition`,
  `VolumeResourceDefinition`, and `NetworkResourceDefinition`
- resource template entries with `JsonElement Configuration`
- create requests with `JsonElement Configuration`
- projected `Resource.Attributes`

This makes it easy for definition, projection, configuration, and diagnostics
to blur together. It also tempts the platform to put complex resource
configuration into projected attributes, even though attributes are currently
documented as stable, non-secret projected facts.

Capabilities have a related issue. A resource type may support a capability,
an individual resource definition may declare capability-owned intent, and a
projected resource may advertise a capability that downstream systems can
discover. Those are related, but they are not the same lifecycle phase.

## Goals

- Distinguish `Resource` instances from `ResourceDefinition` intent in public
  domain language, docs, APIs, persistence, templates, imports, and provider
  contracts.
- Keep `Resource` as a passive projection of current resource state.
- Define a plain serialized resource-definition format that can be stored,
  exchanged, reviewed, imported, and projected through templates without
  becoming provider-native configuration.
- Let resource types expose typed facades over definition payloads without
  requiring every consumer to understand every provider-specific type.
- Treat capability providers as attached behavior registered through
  dependency injection, so they can resolve provider or platform services while
  validating and interpreting capability-owned intent.
- Separate resource-type validation from cross-cutting capability validation.
- Preserve provider ownership over runtime behavior, apply/update/delete
  behavior, and provider-specific configuration.
- Prevent secrets from being serialized into resource definitions, projected
  attributes, diagnostics, logs, templates, or generated code.

## Non-Goals

- Do not subclass projected `Resource` for executable apps, container apps,
  volumes, databases, networks, services, or other resource types.
- Do not make projected `Resource.Attributes` a structured provider
  configuration schema.
- Do not require every provider-owned runtime artifact to be authorable as a
  resource definition.
- Do not require lossless round-tripping from every provider projection back
  into a resource definition.
- Do not replace provider-specific typed definitions immediately; the first
  step is an envelope and validation model that existing definitions can map
  into.
- Do not make capability providers UI actions. Capabilities may support UI
  workflows, but their model behavior belongs to the resource/domain layer.

## Proposed Model

CloudShell should use `ResourceDefinition` for authored or persisted resource
intent.

A definition should include:

- stable resource name or ID
- resource type
- optional provider ID when the type can be handled by more than one provider
- optional display name
- dependencies and references
- optional definition version
- provider-owned configuration payload
- capability-owned intent payloads
- non-secret platform metadata needed for registration, ownership, visibility,
  persistence, or grouping

A serialized projection might look like:

```jsonc
{
  "apiVersion": "cloudshell.resource/v1",
  "name": "api",
  "type": "application.executable",
  "provider": "applications.executable",
  "displayName": "API",
  "dependsOn": ["volume:data"],
  "configuration": {
    "executable": {
      "path": "dotnet",
      "arguments": "run",
      "workingDirectory": "./src/Api"
    }
  },
  "capabilities": {
    "storage.volumeConsumer": {
      "mounts": [
        {
          "volume": "volume:data",
          "targetPath": "App_Data",
          "readOnly": false,
          "name": "data"
        }
      ]
    }
  }
}
```

The serialized form is only one projection of the definition. Code-first
builders, Resource Manager create flows, resource templates, imports, and
future API clients can all produce the same definition model.

## Resource vs ResourceDefinition

`ResourceDefinition` describes intended resource shape. It is the input to
validation, persistence, apply/update/delete planning, import, template export,
and deployment projection.

`Resource` describes the current known resource instance. It is the output of
provider projection, provider observation, Control Plane overlays, current
actions, health, lifecycle state, endpoints, materialization facts, attributes,
visibility, ownership, and authorization-filtered views.

The distinction should be kept explicit:

| Concept | Describes | Owned by |
| --- | --- | --- |
| `ResourceDefinition` | Desired resource intent before projection | Control Plane plus owning resource type provider |
| `Resource` | Current projected resource instance | Control Plane projection over provider state |
| definition configuration | Provider-owned desired configuration | Owning resource type provider |
| capability intent | Cross-cutting desired behavior attached to a definition | Capability provider |
| projected attributes | Stable non-secret facts about the current projection | Owning provider or Control Plane overlay |
| runtime state | Observed provider/runtime facts | Provider, orchestrator, or Control Plane operational store |

## Resource Type Providers

A resource type provider should own the behavior for a precise resource type or
provider-backed family of resource types.

Responsibilities:

- declare supported resource type IDs
- declare the expected `ResourceClass`
- describe supported capabilities for the resource type
- parse or adapt the provider-owned configuration payload
- apply defaults and normalize definition intent
- validate type-specific configuration
- apply changes, update persisted state, and tear down resource state
- project accepted definitions and observed provider state as `Resource`
  instances
- expose resource actions and action availability where applicable

Resource type providers may expose typed facades such as
`ExecutableApplicationResourceDefinition`, `ContainerApplicationDefinition`, or
`VolumeResourceDefinition`. Those facades should map to and from the common
definition envelope instead of replacing it as the platform model.

## Capability Providers

Capability providers are attached behavior for capability-owned intent. They
should be registered with dependency injection and resolved by the Control
Plane validation/apply pipeline, so a provider can depend on platform or
provider services such as volume managers, identity managers, networking
managers, policy services, catalogs, or stores.

Responsibilities:

- declare the capability ID they handle
- parse or adapt the capability payload for that capability
- validate capability-owned intent against the definition and current
  environment
- report diagnostics for invalid, unsupported, unsafe, or unresolved intent
- provide typed helper behavior to resource type providers, orchestrators, or
  projection services where appropriate
- optionally contribute projected capabilities, dependencies, attributes, or
  diagnostics after the definition has been accepted

Capability providers should validate `ResourceDefinition`, not projected
`Resource`, because projected resources already mix accepted intent, runtime
state, provider observations, and Control Plane overlays.

For example, a storage volume consumer provider can own the
`storage.volumeConsumer` capability:

```csharp
public sealed class VolumeConsumerCapabilityProvider(IVolumeManager volumes)
    : IResourceDefinitionCapabilityProvider
{
    public string CapabilityId => "storage.volumeConsumer";

    public ResourceDefinitionValidationResult Validate(
        ResourceDefinition definition,
        ResourceDefinitionValidationContext context)
    {
        var volumeConsumer = definition.GetCapability<VolumeConsumerDefinition>(
            CapabilityId);

        // Validate mount shape, referenced volume resources, access mode,
        // permissions, and host/storage compatibility.
    }

    public IEnumerable<Volume> GetVolumes(
        ResourceDefinition definition)
    {
        var volumeConsumer = definition.GetCapability<VolumeConsumerDefinition>(
            CapabilityId);

        return volumeConsumer.Mounts
            .Select(mount => volumes.GetVolume(mount.VolumeReference));
    }
}
```

This keeps storage behavior reusable across executable apps, ASP.NET Core
projects, container apps, SQL Server resources, or future provider-owned
service resources without pushing volume semantics into each resource type.

## Capability Lifecycle

CloudShell should distinguish these phases:

| Phase | Meaning |
| --- | --- |
| Resource type capability support | The type can accept definitions that use the capability. |
| Definition capability intent | This resource definition declares capability-owned desired behavior. |
| Accepted capability | Validation and normalization accepted the capability intent. |
| Projected resource capability | The current `Resource` advertises the capability for discovery. |
| Runtime materialization | A provider, orchestrator, or runtime has applied or observed the capability in the environment. |

For example, `application.container-app` may support
`storage.volumeConsumer`; a specific container app definition declares two
mounts; validation accepts the mounts; the projected resource advertises
`storage.volumeConsumer`; and runtime materialization later reports whether the
mounts are active.

## Persistence and Plain Format

Persisted resource definitions should be plain enough to inspect and review.
The format should avoid making C# builder types, generated DTO names, or
provider-native files the durable source of truth.

Suggested principles:

- Use stable, lower-camel or dotted identifiers for resource type,
  capability, and configuration keys.
- Include a definition version so providers can migrate payloads.
- Keep provider-owned configuration under `configuration`.
- Keep cross-cutting capability intent under `capabilities`.
- Keep secrets out of definitions. Store references to secret resources,
  configuration entries, or identity-backed access grants instead.
- Normalize before persistence only when normalization is deterministic and
  reviewable.
- Preserve enough source metadata for diagnostics when definitions are created
  from imports or templates.

Resource templates can become one serialized projection over this model rather
than a separate concept with unrelated provider configuration. Resource graph
imports can translate external dialects into resource definitions or graph
drafts before apply.

## Validation Pipeline

The Control Plane should eventually validate definitions through a predictable
pipeline:

1. Parse the definition envelope.
2. Resolve the resource type provider.
3. Validate platform-owned identity, names, grouping, persistence, ownership,
   and references.
4. Let the resource type provider normalize and validate type-specific
   configuration.
5. Resolve capability providers for declared capability intent.
6. Let capability providers validate capability-owned payloads and references.
7. Run cross-definition graph validation, including dependencies,
   authorization, compatibility, and host/provider policy.
8. Return diagnostics and normalized accepted definitions without side effects.
9. Apply, update, persist, or project only after validation succeeds.

Expected validation failures should be returned as diagnostics or result
objects. Exceptions should remain for programmer errors or boundary adapters
that must translate invalid input into API errors.

## Relationship to Existing Concepts

### Resource declarations

Programmatic declarations should become one authoring surface for
`ResourceDefinition`. Existing builders can continue producing provider-typed
definitions internally while the common envelope is introduced.

### Resource templates

Resource templates should eventually store resource definitions instead of
provider-specific configuration records that must be interpreted separately.
Template import/export providers may remain during migration, but their target
shape should converge on the common definition model.

### Resource graph import

External imports, such as Docker Compose, should translate into CloudShell
resource definitions or graph drafts. External formats remain input dialects,
not native CloudShell definition formats.

### Deployment projection

Deployment projection should consume accepted resource definitions and current
graph context. It should not infer desired intent solely from projected
`Resource.Attributes` when the original definition is available.

### Projected resources

Provider-created and runtime-managed resources may be projected as `Resource`
instances without having user-authored definitions. If they later become
authorable, their provider should introduce a definition shape deliberately.

## Recommended First Slices

1. Document the terminology across the domain and resource model docs:
   `Resource` is instance projection; `ResourceDefinition` is intent.
2. Introduce a public preview `ResourceDefinition` envelope in
   `CloudShell.Abstractions` without migrating every provider immediately.
3. Add a resource-definition validation result and diagnostic model.
4. Add a resource type provider validation/normalization path for one narrow
   type, preferably `cloudshell.volume` or `application.executable`.
5. Add a capability-provider path for `storage.volumeConsumer` that validates
   `ResourceVolumeMount` intent outside application-specific code.
6. Map one existing programmatic builder into the definition envelope.
7. Update resource template export/import for the same narrow type to use the
   definition format.
8. Add Control Plane tests for valid definitions, invalid capability payloads,
   missing capability providers, and diagnostics.
9. Add API/client projection only after the in-process definition model is
   stable enough to expose.

## Open Questions

- Should `ResourceDefinition` use resource `name` plus `type`, resource `id`,
  or both as the primary identity in the serialized format?
- Should capability payloads live only under `capabilities`, or can a resource
  type provider promote common capability payloads into typed configuration
  facades for ergonomics?
- How much normalized state should be persisted versus recomputed from the
  authored definition and current provider defaults?
- Should definition migrations be owned entirely by resource type providers, or
  should the Control Plane own a common migration registry?
- How should capability providers declare compatibility with resource types:
  type-provider metadata, capability-provider metadata, or both?
- Which validation belongs in capability providers versus graph-level Control
  Plane policy?
- How should persisted definitions represent provider selection when several
  providers can handle the same resource type?
- What is the minimal API surface for remote clients to create, validate, and
  persist definitions without exposing unstable provider internals?

## Remaining Tasks

- Define the `ResourceDefinition` envelope and serialized field names.
- Define resource type provider contracts for definition parsing,
  normalization, validation, projection, apply, update, and tear down.
- Define capability provider contracts for capability-owned payload parsing,
  validation, diagnostics, and helper behavior.
- Decide how existing provider-specific definitions map to the envelope.
- Decide how resource templates, persisted declarations, imports, and create
  requests converge on the definition model.
- Add documentation examples for executable apps, container apps, volumes, and
  volume consumers.
- Add focused tests around definition validation and capability-provider
  resolution.
- Update the roadmap when this becomes an active implementation track.
