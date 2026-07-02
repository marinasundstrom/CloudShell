# Deployment Projection Future Direction

## Status

Deferred strategic direction, not an active proposal.

Deployment projection has strong long-term product fit, but it is not an
active implementation track. It should wait until ResourceDefinition apply,
container app orchestration, networking, storage, identity, and initial
on-premise target boundaries are stable enough to project without inventing a
parallel deployment model.

CloudShell currently supports resources, providers, orchestrators,
runtime-managed resources, dependencies, and resource templates. However,
there is no platform-wide model for projecting resource graphs into
provider-specific deployment representations.


As CloudShell evolves into a self-hostable platform capable of managing
applications, infrastructure, and operational resources, deployment projection
becomes a foundational capability.

## Purpose

Deployment projection supports one of CloudShell's long-term goals:
allowing applications and resources to evolve from local development and
self-hosted environments toward cloud-hosted deployments without requiring a
complete redesign of the application model.

By treating the CloudShell resource graph as the source model and generating
provider-specific deployment artifacts, applications can gradually migrate to
supported cloud platforms when desired while preserving the same logical
resource definitions.

This capability also aligns CloudShell with the application-model projection
experience provided by Aspire. Developers can describe an application using
CloudShell resources and then generate deployment artifacts for supported
platforms, while infrastructure concerns may be inferred or introduced by the
projection provider.

The goal is not to make CloudShell dependent on Aspire or any specific cloud
platform. Instead, the goal is to provide a compatible projection model that
supports portability, gradual adoption of cloud services, and future provider
integration.

## Problem

CloudShell resources describe applications, services, infrastructure, and
relationships.

Examples include:

* services
* containers
* executables
* endpoints
* settings
* secrets
* dependencies
* runtime-managed resources

Today there is no standard mechanism for:

* converting a resource graph into provider-specific deployment artifacts
* generating infrastructure representations from application models
* validating target-platform compatibility
* reporting unsupported resource mappings
* selecting between alternative target implementations
* projecting resources into cloud-provider deployment formats

Without a unified projection model:

* deployment generation becomes provider-specific
* deployment artifacts cannot be generated consistently
* infrastructure generation becomes difficult to standardize
* deployment diagnostics become fragmented
* provider compatibility becomes difficult to evaluate
* application portability becomes difficult to support

CloudShell requires a platform-wide deployment projection model.

## Goals

* Introduce a platform-wide deployment projection model.
* Project CloudShell resource graphs into target deployment formats.
* Support application-model projection as the initial implementation focus.
* Support infrastructure projection where portable abstractions exist.
* Support provider-specific deployment artifacts.
* Support target compatibility diagnostics.
* Support projection warnings and errors.
* Support alternative target mappings.
* Allow provider-specific projection implementations.
* Provide a foundation for future deployment workflows.

## Non-Goals

* Do not model every infrastructure concept in the first version.
* Do not require every provider capability to have a CloudShell abstraction.
* Do not guarantee lossless projection.
* Do not attempt to standardize provider-specific infrastructure features.
* Do not replace provider-native deployment systems.
* Do not define every possible infrastructure abstraction.
* Do not require deployment targets to expose identical capabilities.
* Do not decide whether deployments are triggered from CLI, Resource Manager
  UI, or another automation surface in the first projection model.

## Projection Model

CloudShell should distinguish between resource serialization and deployment
projection.

Serialization answers:

> How is the CloudShell graph represented?

Projection answers:

> How is the CloudShell graph deployed to a target platform?

An on-premise CloudShell environment is a deployment target in this sense: a
standalone CloudShell cloud environment, potentially for shared hosting,
similar in role to future targets such as Azure or AWS. The target is backed by
CloudShell's own on-premise Control Plane and orchestrator; external provider
targets may use their own provider projections. The trigger surface is a
separate product decision; the deployment model should first define the target,
compatibility, projection, and orchestrator API contract.

CloudShell projections may include:

* Bicep
* ARM templates
* Terraform
* Kubernetes manifests
* Docker Compose
* provider-specific deployment artifacts

Examples:

```text
CloudShell Graph
    ↓
Azure Projection
    ↓
Bicep
```

```text
CloudShell Graph
    ↓
Kubernetes Projection
    ↓
YAML
```

```text
CloudShell Graph
    ↓
Docker Projection
    ↓
Compose
```

The CloudShell graph should remain the source model.

## Application Model Projection

The first implementation focus should be application-model projection.

Examples:

* services
* containers
* endpoints
* settings
* secrets
* dependencies

CloudShell should be able to project application resources into target
deployment artifacts even when additional infrastructure must be inferred by
the target platform.

Example:

```text
Service
 └── Endpoint
```

Azure projection may generate:

```text
Container App
Ingress
Environment
Identity
```

The generated infrastructure is projection-owned and not necessarily represented
as authored CloudShell resources.

## Infrastructure Projection

Some infrastructure concepts have clear cross-platform meaning.

Examples:

* networking
* secret stores
* storage
* identities
* permissions
* ingress
* configuration stores

These abstractions may be projected differently depending on the target
platform.

Example:

```text
Secret
```

May become:

```text
Azure Key Vault Secret
```

```text
Kubernetes Secret
```

```text
Docker Secret
```

```text
Local Development Secret
```

Infrastructure projection should be introduced incrementally.

## Projection Providers

Projection logic should be implemented by projection providers.

Examples:

```text
Azure Projection Provider
```

```text
Kubernetes Projection Provider
```

```text
Docker Projection Provider
```

A projection provider is responsible for:

* resource mapping
* infrastructure generation
* compatibility validation
* deployment artifact generation
* diagnostics

Projection providers may support multiple output formats.

## Resource Mapping

Resources may map differently depending on the target platform.

Examples:

```text
Cache
```

May become:

```text
Azure Cache for Redis
```

```text
Containerized Redis
```

```text
Existing External Redis
```

The mapping should remain explicit and inspectable.

Projection providers should determine which mappings are available.

## Projection Diagnostics

Projection is not guaranteed to be lossless.

Projection providers should report:

* supported mappings
* unsupported mappings
* partial mappings
* inferred infrastructure
* compatibility warnings
* deployment errors

Examples:

```text
✓ Secret projected to Azure Key Vault
```

```text
⚠ Custom network policy not supported
```

```text
✗ Runtime dependency cannot be represented
```

Users should be able to understand the consequences of deploying to a target
platform.

## Compatibility Evaluation

Projection compatibility should be evaluated before deployment artifacts are
generated.

Example:

```text
Resource Graph
    ↓
Compatibility Evaluation
    ↓
Projection Diagnostics
    ↓
Deployment Artifact
```

Compatibility evaluation should identify:

* unsupported resources
* unsupported capabilities
* required configuration
* projection fallbacks

## Target-Specific Infrastructure

Projection providers may introduce infrastructure that is not explicitly
represented in the source graph.

Example:

```text
Application Service
```

May require:

```text
Managed Environment
Identity
Monitoring
Ingress
```

These resources are deployment concerns rather than authored resources.

The projection provider should be responsible for creating them.

## Aspire Compatibility Strategy

The first deployment target should focus on Aspire-compatible application
projection.

CloudShell should be able to project an application graph into provider-specific
deployment artifacts while allowing infrastructure to be inferred by the target
projection provider.

The goal is not to model all infrastructure immediately.

The goal is to prove that:

```text
CloudShell Application Graph
    ↓
Projection Provider
    ↓
Deployable Artifact
```

can work consistently across supported targets.

Explicit infrastructure abstractions can then be introduced incrementally.

## Projection Flow

Example:

```text
Resource Graph
    ↓
Projection Provider
    ↓
Resource Mapping
    ↓
Infrastructure Generation
    ↓
Deployment Artifact
```

Example:

```text
Application Graph
    ↓
Azure Projection
    ↓
Bicep
```

## Projection Evaluation

Projection should occur before deployment.

Example:

```text
Resource Graph
    ↓
Compatibility Evaluation
    ↓
Projection
    ↓
Deployment Artifact
```

The Deployment Manager should be responsible for coordinating projection
operations.

## Diagnostics and Reporting

Projection operations should be auditable.

Examples:

* which target was selected
* which mappings were used
* which infrastructure was generated
* which warnings were produced
* which errors were encountered

These events should integrate with future traceability and audit proposals.

## API and UI Projection

The API should expose:

* projection targets
* projection mappings
* compatibility results
* diagnostics
* generated deployment artifacts

Administrative views may display:

```text
Application
 ├── Projection Target
 ├── Resource Mappings
 ├── Generated Infrastructure
 └── Diagnostics
```

Normal users should only see information they are authorized to view.

## Possible Later Implementation Plan

1. Define projection abstractions.
2. Define projection-provider abstractions.
3. Define projection diagnostics.
4. Define compatibility evaluation.
5. Implement application-model projection.
6. Implement Aspire-compatible projection workflows.
7. Implement Azure Bicep projection.
8. Add inferred infrastructure generation.
9. Add projection APIs.
10. Add projection UI support.
11. Add projection diagnostics and reporting.
12. Add deployment integration.
13. Add compatibility testing.
14. Add integration tests.

## Possible Later Tasks

* Define projection-provider contracts.
* Define resource-mapping contracts.
* Define compatibility-report formats.
* Define projection diagnostic schemas.
* Define deployment artifact lifecycles.
* Define generated-infrastructure ownership.
* Define fallback mapping behavior.
* Define projection extensibility.

## Open Questions

* Should projection mappings be represented as resources?
* Should generated infrastructure be visible in the resource graph?
* How should alternative mappings be selected?
* How should projection warnings affect deployment?
* How should projection providers declare capabilities?
* How should projection interact with runtime-managed resources?
* How should deployment artifacts be versioned?
* Which infrastructure abstractions should be standardized first?
* How should CloudShell expose compatibility results through APIs?
