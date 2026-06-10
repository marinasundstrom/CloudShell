# Identity and Permissions Proposal

## Status

In progress.

CloudShell currently supports resources, actions, capabilities, providers, orchestrators, and runtime-managed resources. However, there is no platform-wide model for identity, authentication, authorization, or permission assignment.

As CloudShell evolves into a self-hostable platform capable of managing applications, infrastructure, and operational resources, a consistent identity and permission model becomes a foundational requirement.

## Problem

CloudShell resources expose actions and capabilities that may affect system state.

Examples include:

* starting resources
* stopping resources
* deleting resources
* updating configuration
* deploying revisions
* scaling workloads
* reading diagnostics
* reading logs
* accessing secrets
* creating runtime-managed resources

Today there is no standard mechanism for determining:

* who is performing an action
* what permissions are required
* whether the action should be allowed
* how permissions are assigned
* how workload-to-workload authorization should work
* how resources authenticate to platform services

Without a unified model:

* authorization becomes provider-specific
* resource actions cannot be secured consistently
* secret access becomes difficult to standardize
* auditability becomes fragmented
* workload identities become difficult to support
* platform services cannot reliably trust callers

CloudShell requires a platform-wide identity and permission model.

## Goals

* Introduce a platform-wide identity model.
* Introduce a platform-wide permission model.
* Authorize actions against resources.
* Support human users, workloads, services, providers, and orchestrators.
* Support workload-to-workload authentication.
* Support resource-to-service authentication.
* Support secret access authorization.
* Support delegated permissions.
* Remain compatible with OpenID Connect and OAuth 2.0.
* Allow alternative identity providers.
* Provide a foundation for auditing and traceability.

## Non-Goals

* Do not implement a complete IAM system in the first version.
* Do not define organization, tenant, or billing models.
* Do not require every provider to implement custom authorization logic.
* Do not couple CloudShell to a specific identity provider.
* Do not replace existing OpenID Connect or OAuth standards.
* Do not introduce resource-specific permission systems.
* Do not define every possible permission type in the first version.

## Identity Model

CloudShell should distinguish between identity and authorization.

Authentication answers:

> Who are you?

Authorization answers:

> What are you allowed to do?

CloudShell identities may include:

* user identities
* workload identities
* service identities
* provider identities
* orchestrator identities

Examples:

```text
User
```

```text
Container App
```

```text
Secret Service
```

```text
Default Orchestrator
```

```text
Container Provider
```

Every identity should have a stable identifier that can participate in permission assignments, auditing, and diagnostics.

## Permission Model

Permissions should be assigned against resources and actions.

Conceptually:

```text
Identity
    ↓
Permission
    ↓
Resource
    ↓
Action
```

Examples:

```text
User
 └── restart
      └── ContainerApp
```

```text
ContainerApp
 └── read
      └── Secret
```

```text
Orchestrator
 └── create
      └── Replica
```

Permissions should be independent of specific providers and resource implementations.

## Resource Actions

Resources already expose actions.

Examples:

* start
* stop
* restart
* delete
* update
* scale
* deploy
* rollback
* inspect
* read logs
* read metrics

Permissions should be evaluated against these actions.

Example:

```text
Identity: user/alice
Resource: container-app/api
Action: restart
Result: allowed
```

```text
Identity: workload/api
Resource: secret/database-password
Action: read
Result: allowed
```

## Permission Assignments

Permissions should be represented explicitly.

Suggested model:

```csharp
public sealed class PermissionAssignment
{
    public string IdentityId { get; init; }
    public string ResourceId { get; init; }
    public string Action { get; init; }
}
```

Future versions may support:

* scopes
* inheritance
* wildcard matching
* role mappings
* policy evaluation

The initial model should remain simple.

## Workload Identity

Resources may need identities.

Examples:

* Container App
* Executable
* Background Service
* Deployment Service

A resource identity allows workloads to authenticate to platform services.

Example:

```text
ContainerApp
 └── Identity
      └── read Secret
```

This enables secure service-to-service communication.

## Secret Access

Secret access should be authorized through the permission model.

Example:

```text
ContainerApp Identity
 └── read
      └── DatabasePasswordSecret
```

The Secret Service should evaluate permissions before returning secret values.

Secret access should never be granted solely because a resource references a secret.

## Provider and Orchestrator Permissions

Providers and orchestrators may require elevated permissions.

Examples:

```text
Default Orchestrator
 └── create Replica
```

```text
Container Provider
 └── create ContainerInstance
```

```text
Deployment Service
 └── create Revision
```

These permissions should be explicit and auditable.

## Identity Provider Integration

CloudShell should use OpenID Connect and OAuth 2.0 as the primary authentication protocols.

The platform should remain identity-provider agnostic.

Any standards-compliant identity provider should be usable.

Examples:

* IdentityServer
* Microsoft Entra ID
* Keycloak
* Auth0
* Okta

## IdentityServer Development Strategy

During development, CloudShell should use IdentityServer as the reference identity provider.

IdentityServer provides:

* OpenID Connect support
* OAuth 2.0 support
* access token issuance
* client credentials flow
* workload authentication
* development-time testing

IdentityServer is not part of the CloudShell domain model.

It is a protocol implementation used during development and testing.

CloudShell should interact with IdentityServer through standard OIDC and OAuth flows so that alternative identity providers can be substituted later without changing the platform authorization model.

## Authentication Flow

Example:

```text
User
    ↓
Identity Provider
    ↓
Access Token
    ↓
CloudShell API
    ↓
Authorization Evaluation
    ↓
Resource Action
```

Workload example:

```text
Container App
    ↓
Identity Provider
    ↓
Access Token
    ↓
Secret Service
    ↓
Permission Check
    ↓
Secret Value
```

## Authorization Evaluation

Authorization should occur before resource actions execute.

Example:

```text
Request
    ↓
Identity
    ↓
Permission Evaluation
    ↓
Resource Action
```

The Resource Manager should be responsible for coordinating authorization checks before dispatching operations to providers or orchestrators.

## Diagnostics and Audit

Identity and authorization decisions should be auditable.

Examples:

* who executed an action
* which resource was targeted
* whether access was granted
* whether access was denied
* which permission was evaluated

These events should integrate with future traceability and audit proposals.

## API and UI Projection

The API should expose:

* identities
* permission assignments
* authorization results
* effective permissions

Administrative views may display:

```text
ContainerApp
 ├── Identity
 ├── Permissions
 └── Access History
```

Normal users should only see information they are authorized to view.

## Implementation Plan

1. Define identity abstractions.
2. Define permission assignment abstractions.
3. Define resource action authorization model.
4. Introduce workload identities.
5. Introduce provider and orchestrator identities.
6. Integrate OpenID Connect authentication.
7. Integrate OAuth 2.0 token validation.
8. Add IdentityServer development integration.
9. Add authorization evaluation APIs.
10. Add secret-access authorization.
11. Add diagnostics and audit events.
12. Add API and UI projection support.
13. Add integration tests.

## Remaining Tasks

* Define permission inheritance.
* Define resource-scope permissions.
* Define wildcard permissions.
* Define service-to-service authorization flows.
* Define token claim mapping.
* Define workload identity lifecycle.
* Define secret-access patterns.
* Define audit event schemas.

## Open Questions

* Should permissions be modeled as resources?
* Should actions be standardized across all resource types?
* How should permissions be inherited across ownership relationships?
* How should workload identities be provisioned and rotated?
* How should authorization integrate with runtime-managed resources?
* How should provider and orchestrator permissions be granted?
* Which permission evaluation model should be used initially?
* Should permissions support policy-based evaluation in the future?
* Which identity provider should be used for local development by default?
* How should CloudShell expose effective permissions through APIs?
