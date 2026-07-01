# Resource Templates

Resource templates are the user-facing desired-state authoring format for the
Resource model. A template is a small envelope around one or more
`ResourceDefinition` entries. It is not an orchestrator deployment artifact and
it is not a second provider-specific template system.

The Resource Manager apply path owns template orchestration: it validates the
envelope, applies each resource definition through the resource graph and the
owning resource type provider, then lets deployment planning decide whether the
accepted resource-state change requires runtime materialization.

## Template Shape

The preferred authoring format is YAML:

```yaml
resources:
  - type: application.container-app
    name: api
    attributes:
      container.image: api:latest
      container.replicas: 3
```

YAML resource entries use the author-friendly `type` alias for
`ResourceDefinition.TypeId`. The equivalent JSON projection keeps the
contract property name:

```json
{
  "name": "local",
  "resources": [
    {
      "name": "api",
      "typeId": "application.container-app",
      "attributes": {
        "container.image": "api:latest",
        "container.replicas": 3
      }
    }
  ]
}
```

The file-level envelope may later grow apply metadata such as environment,
mode, validation options, or parameters. The inner resource entries should stay
based on `ResourceDefinition` so add-resource flows, edit-resource flows,
exports, imports, and file-based apply all use the same resource-state model.
YAML and JSON are both supported through the shared Resource model serializer;
YAML is the default when a file extension does not explicitly select JSON.

## Apply Semantics

Applying a template means applying resource intent:

1. Resource Manager receives one or more `ResourceDefinition` entries.
2. The Resource Graph resolves identities, references, class/type defaults,
   attributes, capabilities, and operations.
3. The owning resource type provider validates and accepts or rejects the
   proposed resource state.
4. Accepted state is committed to the Resource Graph.
5. Deployment planning decides whether runtime state must change.
6. The orchestrator materializes runtime changes when needed.

This keeps resource authoring separate from runtime orchestration. Users
declare resources and the state they want those resources in. They do not need
to author orchestrator deployments, orchestrator services, replica groups, or
runtime replicas for normal workflows.

For example, changing a container app image should be an incremental resource
definition:

```yaml
resources:
  - type: application.container-app
    name: api
    attributes:
      container.image: ghcr.io/example/api:20260629.1
```

Changing the requested replica slots is the same resource-state operation:

```yaml
resources:
  - type: application.container-app
    name: api
    attributes:
      container.replicas: 4
```

The container app provider and Resource Manager deployment controller decide
how those accepted changes affect the internal orchestrator service, replica
group, routing bindings, load balancer, runtime replicas, readiness gates, and
cleanup policy.

The public apply contract is `ResourceTemplateApplyRequest`. It carries the
template and an apply mode:

- `CreateOrUpdate` is the default for template apply. Missing resources are
  created, and matching resources are updated incrementally.
- `UpdateExisting` only updates resources already present in the graph. Missing
  targets produce diagnostics and do not mutate graph state.
- `CreateOnly` only creates missing resources. Existing targets produce
  diagnostics and do not mutate graph state.

In all modes, provider validation runs before graph state is committed.
Runtime reconciliation runs only after accepted state has been committed.

## Export And Portability

Exporting resources produces a resource template containing
`ResourceDefinition` entries. Export should prefer the graph-owned resource
state that can be reviewed, moved, edited, and applied again. It should not
dump provider runtime caches, live container IDs, logs, health snapshots,
secret values, or internal orchestrator deployment records.

Provider-owned configuration that is part of the resource contract can appear
as resource attributes, typed capability payloads, or provider-owned
configuration fields when the resource type defines them. Secret material must
not be exported. References to configuration entries or secrets can be
exported when they are non-secret intent.

## Relationship To Orchestration

Resource templates are desired resource state. Orchestrator deployments are
internal runtime materialization records derived from accepted resource state.

The boundary is:

```text
Desired state
(ResourceTemplate)
    |
    v
Resource Graph
    |
    v
Resource Providers
    |
    v
Deployment Planning
    |
    v
Orchestrator
    |
    v
Running System
```

Do not wrap normal resource templates in `ResourceDeploymentDefinition` or any
deployment-shaped user authoring envelope. If an internal orchestration API
needs a deployment definition, it should use an orchestration-specific
contract such as `ResourceOrchestratorDeploymentDefinition` and it should be
produced by Resource Manager or a provider-owned planner after resource
definitions have been accepted.

## Provider Contract

The old provider-specific template engine is being replaced by graph-backed
resource definitions. Providers should move template support toward these
responsibilities:

- define the resource type, attributes, capabilities, operations, and value
  shapes they accept
- validate a proposed `ResourceDefinition`
- normalize provider-owned defaults into accepted graph state
- render accepted graph state back to `ResourceDefinition`
- plan runtime deployment changes when accepted resource state affects the
  running system

Resource Manager should no longer need provider-specific serializer and
deserializer logic for each resource type just to export or import a resource.
The resource definition shape is the serialization boundary; provider logic is
validation, normalization, and runtime planning.

## Commands And Delete

Templates are not the only way to operate resources.

- Start, stop, pause, restart, and provider-specific operations remain resource
  commands.
- A provider may also treat an accepted resource-state change as implying a
  runtime transition, but the direct command surface remains available for
  operational actions.
- Delete remains a Resource Manager delete operation. It is not modeled as a
  user-authored deployment template.

## Migration Notes

Near-term migration work should:

- keep graph-backed ResourceTemplate import/export focused on
  `ResourceDefinition` entries
- keep the old provider-specific template serializer path deleted instead of
  reintroducing compatibility wrappers
- avoid preserving `ResourceDeploymentDefinition` compatibility wrappers unless
  an internal orchestration API still explicitly needs an orchestration-shaped
  DTO
- document non-parity for old provider templates while samples finish
  switching to the graph path
