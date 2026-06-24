# Resource Definitions, Capability Providers, and Operation Providers Proposal

## Status

Proposed.

CloudShell already distinguishes projected resources from declared resources in
the resource model documentation, and several providers already carry typed
definition records such as application, storage, volume, network, service, DNS,
and load-balancer definitions. This proposal tracks the next model step:
formalizing `ResourceDefinition` as resource intent and formalizing capability
providers and operation providers as attached behavior over that intent.

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

Resource definitions also need inherited expectations. A resource instance can
inherit attributes, capabilities, and commands from its `ResourceTypeDefinition`,
and that type definition can in turn inherit from a broader
`ResourceClassDefinition`. Raw property bags such as `.Attributes`,
`.Capabilities`, and projected command lists therefore cannot be treated as
the effective model. They are authored or projected inputs that need resolution
against class, type, preset, provider, and environment rules.

Capabilities have a related issue. A resource type may support a capability,
an individual resource definition may declare capability-owned intent, and a
projected resource may advertise a capability that downstream systems can
discover. Those are related, but they are not the same lifecycle phase.

Resource commands and operations have the same boundary concern. A projected
resource can advertise commands such as start, stop, restart, reconcile,
update-image, or a provider-specific command. A command is the thing a caller
performs. The operation is the provider-side work that happens behind that
command. The behavior that validates command availability and executes the
backing operation should not have to live in a single monolithic resource type
provider. The current implementation may continue mapping commands onto the
existing action-shaped API fields during migration, but the durable domain
language should distinguish commands from operations.

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
- Treat resource operation providers as attached behavior registered through
  dependency injection, so each provider can own the provider-side operation
  behind one defined resource command.
- Define `ResourceClassDefinition` and `ResourceTypeDefinition` inheritance so
  attributes, capabilities, commands, defaults, presets, and requirements can
  be resolved before validation or projection.
- Define attribute validators for common rules and provider/type-specific
  rules, including required attributes and broader value validation.
- Provide resolver APIs that compute effective attributes, capabilities, and
  commands instead of asking callers to trust raw property bags.
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
- Do not require the first implementation to settle the final resolver API
  shape. The durable requirement is that resolution exists and callers have a
  supported path to ask for effective values and diagnostics.
- Do not make capability providers UI actions. Capabilities may support UI
  workflows, but their model behavior belongs to the resource/domain layer.
- Do not make resource commands UI actions. They are resource-domain commands
  that UI or API surfaces may invoke after authorization and capability checks.
  Resource operation providers own what happens behind those commands.

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
- optional command declarations or operation configuration when a resource type
  allows authored command or operation policy
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
commands, health, lifecycle state, endpoints, materialization facts,
attributes, visibility, ownership, and authorization-filtered views.

The distinction should be kept explicit:

| Concept | Describes | Owned by |
| --- | --- | --- |
| `ResourceDefinition` | Desired resource intent before projection | Control Plane plus owning resource type provider |
| `Resource` | Current projected resource instance | Control Plane projection over provider state |
| definition configuration | Provider-owned desired configuration | Owning resource type provider |
| capability intent | Cross-cutting desired behavior attached to a definition | Capability provider |
| projected attributes | Stable non-secret facts about the current projection | Owning provider or Control Plane overlay |
| runtime state | Observed provider/runtime facts | Provider, orchestrator, or Control Plane operational store |

## Class and Type Definitions

Resource definitions should be resolved against two inherited definition
layers:

- `ResourceClassDefinition` describes broad expectations for a class such as
  executable, container, storage, network, configuration, service, or
  infrastructure.
- `ResourceTypeDefinition` describes precise type expectations such as
  `application.executable`, `application.container-app`, `cloudshell.volume`,
  or `cloudshell.storage`.

A resource definition instance then supplies concrete intent. Conceptually:

```text
ResourceClassDefinition
    -> ResourceTypeDefinition
        -> ResourceDefinition
            -> ResolvedResourceDefinition
```

Class and type definitions can contribute:

- default attributes
- required attributes
- attribute descriptors and validators
- supported capabilities
- required capabilities
- default capability payloads
- supported commands
- command requirements
- provider selection requirements
- presets or named partial definition overlays
- class/type-level diagnostics and compatibility rules

The instance definition supplies values, selects presets where allowed, and can
override values only within the constraints defined by the class and type. A
type definition should not be a passive label; it should be the contract that
explains what the definition must contain before the provider can accept it.

Presets should be modeled as named overlays rather than hidden provider
shortcuts. A preset can provide default configuration, attributes,
capabilities, and command policy, but it still resolves through the same class
and type validators. This keeps a preset reviewable and avoids a second path
that bypasses the resource-definition model.

## Resolution

Callers should avoid reading raw `.Attributes`, `.Capabilities`, or projected
command collections when they need the effective model. Those members can be
missing inherited values, can contain invalid authored values, or can represent
provider projection rather than accepted intent.

The exact API is still open, but the model needs supported methods or services
that can answer questions such as:

```csharp
ResolvedResourceDefinition resolved = resolver.Resolve(
    definition,
    new ResourceDefinitionResolutionContext(environmentId, principal));

string? executablePath = resolved.Attributes.GetString(
    ResourceAttributeNames.ExecutablePath);

bool consumesVolumes = resolved.Capabilities.Has(
    ResourceCapabilityIds.StorageVolumeConsumer);

bool canStart = resolved.Commands.Has(ResourceCommandIds.Start);
```

A resolved definition should expose effective values and diagnostics:

```csharp
public sealed record ResolvedResourceDefinition(
    ResourceDefinition Definition,
    ResourceClassDefinition ClassDefinition,
    ResourceTypeDefinition TypeDefinition,
    ResourceAttributeSet Attributes,
    ResourceCapabilitySet Capabilities,
    ResourceCommandSet Commands,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics);
```

The important requirement is not this exact API shape. The requirement is that
CloudShell has a deliberate resolution boundary that combines class
definitions, type definitions, presets, provider defaults, and authored
resource definitions before validation, projection, command availability,
deployment projection, or UI rendering relies on those values.

The same principle applies to projected resources. A `Resource` projection can
be checked against its known class/type expectations, but callers should use a
validation or resolution helper rather than assuming the projected attribute
dictionary is complete and valid.

## Resource Type Providers

A resource type provider should own the behavior for a precise resource type or
provider-backed family of resource types.

Responsibilities:

- declare supported resource type IDs
- declare the expected `ResourceClass`
- describe supported capabilities for the resource type
- describe supported commands for the resource type
- contribute or reference the `ResourceTypeDefinition`
- parse or adapt the provider-owned configuration payload
- apply defaults and normalize definition intent
- validate type-specific configuration
- apply changes, update persisted state, and tear down resource state
- project accepted definitions and observed provider state as `Resource`
  instances
- expose resource commands and command availability where applicable

Resource type providers may expose typed facades such as
`ExecutableApplicationResourceDefinition`, `ContainerApplicationDefinition`, or
`VolumeResourceDefinition`. Those facades should map to and from the common
definition envelope instead of replacing it as the platform model.

For example, an executable application resource type provider could own the
`application.executable` type while delegating storage mounts and start/stop
operations to DI-backed attached providers:

```csharp
public sealed class ExecutableApplicationResourceTypeProvider(
    IExecutableApplicationDefinitionStore definitions,
    IEnumerable<IResourceDefinitionCapabilityProvider> capabilityProviders,
    IEnumerable<IResourceOperationProvider> operationProviders)
    : IResourceTypeProvider
{
    public string TypeId => "application.executable";

    public ResourceClass ResourceClass => ResourceClass.Executable;

    public IReadOnlyList<ResourceCapabilityDescriptor> SupportedCapabilities =>
    [
        new("storage.volumeConsumer"),
        new("logs.sources"),
        new("monitoring")
    ];

    public ResourceDefinitionValidationResult Validate(
        ResourceDefinition definition,
        ResourceDefinitionValidationContext context)
    {
        var resolved = context.Resolve(definition);
        var executable = definition.GetConfiguration<ExecutableConfiguration>(
            "executable");

        var diagnostics = new List<ResourceDefinitionDiagnostic>(
            resolved.Diagnostics);

        if (string.IsNullOrWhiteSpace(executable.Path))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                definition.Name,
                "Executable path is required."));
        }

        foreach (var capability in definition.Capabilities)
        {
            var provider = capabilityProviders.FirstOrDefault(provider =>
                provider.CanValidate(definition, capability.Key));

            if (provider is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    definition.Name,
                    $"No provider is registered for capability '{capability.Key}'."));
                continue;
            }

            diagnostics.AddRange(provider.Validate(definition, context).Diagnostics);
        }

        return ResourceDefinitionValidationResult.FromDiagnostics(diagnostics);
    }

    public Resource Project(
        ResourceDefinition definition,
        ResourceProjectionContext context)
    {
        var executable = definition.GetConfiguration<ExecutableConfiguration>(
            "executable");

        var commands = operationProviders
            .Where(provider => provider.CanHandle(definition))
            .Select(provider => provider.ProjectCommand(definition, context))
            .ToArray();

        return new Resource(
            Id: definition.ResourceId,
            Name: definition.Name,
            Kind: TypeId,
            Provider: "applications.executable",
            Region: "local",
            State: context.GetLifecycleState(definition.ResourceId),
            Endpoints: context.GetEndpoints(definition.ResourceId),
            Version: definition.Version,
            LastUpdated: context.Now,
            DependsOn: definition.DependsOn,
            TypeId: TypeId,
            Commands: commands,
            ResourceClass: ResourceClass,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.ExecutablePath] = executable.Path,
                [ResourceAttributeNames.WorkingDirectory] =
                    executable.WorkingDirectory ?? "."
            },
            Capabilities: context.ProjectCapabilities(definition));
    }

    public Task<ResourceApplyResult> ApplyAsync(
        ResourceDefinition definition,
        ResourceApplyContext context,
        CancellationToken cancellationToken)
    {
        var executable = definition.ToTyped<ExecutableApplicationResourceDefinition>();
        definitions.Save(executable);

        return Task.FromResult(ResourceApplyResult.Accepted(definition.ResourceId));
    }
}
```

The resource type provider owns the type's configuration and projection shape.
It does not need to know every cross-cutting capability or every executable
operation implementation in detail. Capability and operation providers can be added
by capability packages through DI as long as they use stable resource type,
capability, and operation identifiers.

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

## Attribute Validators

Attribute validation should be explicit and reusable. Attributes are useful
only when callers can understand whether an attribute is required, inherited,
defaulted, supplied by the instance definition, projected by the provider, or
invalid for the resource's class/type.

Attribute validators should cover common rules:

- required value
- string, number, boolean, enum-like token, URI, path, resource reference, and
  structured payload validation
- allowed values
- range and length checks
- pattern checks
- case normalization
- invariant formatting
- secret-value rejection
- provider compatibility
- cross-attribute rules

They should also allow type-specific and capability-specific rules without
forcing every rule into a central switch. For example:

```csharp
public sealed class ExecutablePathAttributeValidator : IResourceAttributeValidator
{
    public string AttributeName => ResourceAttributeNames.ExecutablePath;

    public bool CanValidate(ResourceAttributeValidationContext context) =>
        context.TypeDefinition.TypeId == "application.executable";

    public ResourceAttributeValidationResult Validate(
        ResourceAttributeValue value,
        ResourceAttributeValidationContext context)
    {
        if (value.IsMissing)
        {
            return ResourceAttributeValidationResult.Error(
                AttributeName,
                "Executable path is required.");
        }

        if (!value.IsString)
        {
            return ResourceAttributeValidationResult.Error(
                AttributeName,
                "Executable path must be a string.");
        }

        return ResourceAttributeValidationResult.Valid(AttributeName);
    }
}
```

Validation should happen at two related boundaries:

- definition validation: does the authored `ResourceDefinition` satisfy its
  class/type/capability/command requirements?
- projection validation: does the projected `Resource` still satisfy the known
  `ResourceClassDefinition` and `ResourceTypeDefinition` expectations?

Projection validation matters because provider projections can drift, omit
inherited values, or carry legacy attribute names. Resource Manager and API
clients should be able to surface diagnostics or normalized views instead of
silently trusting raw projected attributes.

## Resource Operation Providers

Resource operation providers are attached behavior for the provider-side work
behind a defined resource command. They should be registered with dependency
injection and resolved by the Control Plane when it projects resource commands,
computes command availability, or executes a requested command.

Responsibilities:

- declare the command ID they handle
- declare the resource types, resource classes, or capabilities they can
  handle
- project the command affordance when the command applies to a resource
- compute current command availability and user-displayable unavailable
  reasons
- execute the backing operation after Control Plane authorization and
  validation
- return resource procedure results, diagnostics, activity events, or
  reconciliation signals

Resource commands are not UI commands. A Resource Manager button, menu item, or
API route can invoke a resource command, but the operation provider owns the
domain behavior behind that command.

For example, an executable start operation provider can own the standard
`start` command for executable application resources:

```csharp
public sealed class ExecutableStartOperationProvider(
    IExecutableApplicationDefinitionStore definitions,
    ILocalProcessRunner processes,
    IResourceDefinitionCapabilityProvider<VolumeConsumerDefinition> volumes)
    : IResourceOperationProvider
{
    public string CommandId => ResourceCommandIds.Start;

    public bool CanHandle(ResourceDefinition definition) =>
        string.Equals(
            definition.Type,
            "application.executable",
            StringComparison.OrdinalIgnoreCase);

    public ResourceCommand ProjectCommand(
        ResourceDefinition definition,
        ResourceProjectionContext context) =>
        new(
            Id: ResourceCommandIds.Start,
            Label: "Start",
            Description: "Start the executable application.",
            RequiresConfirmation: false);

    public async Task<ResourceCommandAvailability> GetAvailabilityAsync(
        ResourceDefinition definition,
        ResourceCommandAvailabilityContext context,
        CancellationToken cancellationToken)
    {
        if (context.State is ResourceState.Running or ResourceState.Starting)
        {
            return ResourceCommandAvailability.Unavailable(
                ResourceCommandIds.Start,
                "The resource is already running or starting.");
        }

        var volumeDiagnostics = await volumes.ValidateAsync(
            definition,
            context.ToDefinitionValidationContext(),
            cancellationToken);

        if (volumeDiagnostics.HasErrors)
        {
            return ResourceCommandAvailability.Unavailable(
                ResourceCommandIds.Start,
                "One or more volume mounts cannot be materialized.");
        }

        return ResourceCommandAvailability.Available(ResourceCommandIds.Start);
    }

    public async Task<ResourceProcedureResult> ExecuteAsync(
        ResourceDefinition definition,
        ResourceCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var executable = definitions.Get(definition.ResourceId);
        if (executable is null)
        {
            return ResourceProcedureResult.Failed(
                $"Resource definition '{definition.ResourceId}' was not found.");
        }

        await processes.StartAsync(executable, cancellationToken);

        return ResourceProcedureResult.Completed(
            $"Started executable application '{definition.Name}'.");
    }
}
```

This lets a resource type support multiple commands without centralizing every
backing operation in the resource type provider. Standard lifecycle commands
can have shared policy in the Control Plane, while provider-specific operation
providers still own provider-specific checks and execution.

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
2. Resolve the resource class definition and resource type definition.
3. Apply selected presets and deterministic defaults.
4. Resolve inherited attributes, capabilities, and commands into an effective
   model.
5. Resolve the resource type provider.
6. Validate platform-owned identity, names, grouping, persistence, ownership,
   and references.
7. Run common and type-specific attribute validators.
8. Let the resource type provider normalize and validate type-specific
   configuration.
9. Resolve capability providers for declared capability intent.
10. Let capability providers validate capability-owned payloads and references.
11. Resolve resource operation providers for declared and type-supported
   commands.
12. Let operation providers validate command configuration, operation
   configuration, and availability policy that can be checked before projection
   or apply.
13. Run cross-definition graph validation, including dependencies,
   authorization, compatibility, and host/provider policy.
14. Return diagnostics and normalized accepted definitions without side
    effects.
15. Apply, update, persist, or project only after validation succeeds.

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

Projected resources should still be validated against known
`ResourceClassDefinition` and `ResourceTypeDefinition` expectations when those
definitions exist. Projection validation can produce diagnostics, normalize
legacy provider output, or explain why generated details and command
availability are incomplete.

## Recommended First Slices

1. Document the terminology across the domain and resource model docs:
   `Resource` is instance projection; `ResourceDefinition` is intent.
2. Introduce a public preview `ResourceDefinition` envelope in
   `CloudShell.Abstractions` without migrating every provider immediately.
3. Add preview `ResourceClassDefinition` and `ResourceTypeDefinition` records
   with inherited attribute, capability, and command descriptors.
4. Add a resource-definition resolver that computes effective attributes,
   capabilities, commands, and diagnostics.
5. Add a resource-definition validation result and diagnostic model.
6. Add common attribute validators and one type-specific validator.
7. Add a resource type provider validation/normalization path for one narrow
   type, preferably `cloudshell.volume` or `application.executable`.
8. Add a capability-provider path for `storage.volumeConsumer` that validates
   `ResourceVolumeMount` intent outside application-specific code.
9. Add a resource-operation-provider path for one standard lifecycle command,
   preferably executable `start` or container app `restart`.
10. Map one existing programmatic builder into the definition envelope.
11. Update resource template export/import for the same narrow type to use the
   definition format.
12. Add Control Plane tests for valid definitions, invalid attributes, invalid
   capability payloads, missing capability providers, missing operation
   providers, projection validation, and diagnostics.
13. Add API/client projection only after the in-process definition model is
   stable enough to expose.

## Open Questions

- Should `ResourceDefinition` use resource `name` plus `type`, resource `id`,
  or both as the primary identity in the serialized format?
- Should capability payloads live only under `capabilities`, or can a resource
  type provider promote common capability payloads into typed configuration
  facades for ergonomics?
- How much normalized state should be persisted versus recomputed from the
  authored definition and current provider defaults?
- What is the precedence order between class defaults, type defaults, selected
  presets, provider defaults, and explicit resource-definition values?
- Should class/type definitions be public authoring artifacts, provider-only
  descriptors, or both?
- Should definition migrations be owned entirely by resource type providers, or
  should the Control Plane own a common migration registry?
- Which attribute validators belong in common abstractions versus provider
  packages?
- Should projection validation normalize invalid provider output, return
  diagnostics only, or support both modes?
- How should capability providers declare compatibility with resource types:
  type-provider metadata, capability-provider metadata, or both?
- Which validation belongs in capability providers versus graph-level Control
  Plane policy?
- How should operation providers declare compatibility with resource types:
  operation-provider metadata, type-provider metadata, capability requirements,
  or all of those?
- Should resource definitions be able to declare command or operation policy,
  or should commands always be inferred from type, provider, lifecycle state,
  and capability support?
- How should persisted definitions represent provider selection when several
  providers can handle the same resource type?
- What is the minimal API surface for remote clients to create, validate, and
  persist definitions without exposing unstable provider internals?

## Remaining Tasks

- Define the `ResourceDefinition` envelope and serialized field names.
- Define `ResourceClassDefinition` and `ResourceTypeDefinition`, including
  inheritance, presets, requirements, and descriptor precedence.
- Define resolver services or helper methods for effective attributes,
  capabilities, and commands.
- Define resource type provider contracts for definition parsing,
  normalization, validation, projection, apply, update, and tear down.
- Define common and provider-owned attribute validator contracts.
- Define capability provider contracts for capability-owned payload parsing,
  validation, diagnostics, and helper behavior.
- Define resource operation provider contracts for command projection, command
  availability, backing operation execution, diagnostics, and procedure
  results.
- Decide how existing provider-specific definitions map to the envelope.
- Decide how resource templates, persisted declarations, imports, and create
  requests converge on the definition model.
- Add documentation examples for executable apps, container apps, volumes, and
  volume consumers.
- Add focused tests around definition validation and capability-provider
  resolution.
- Update the roadmap when this becomes an active implementation track.
