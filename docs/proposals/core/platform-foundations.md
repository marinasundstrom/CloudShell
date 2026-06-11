# Platform Foundations Proposal

## Status

In progress.

CloudShell already provides a resource model, Resource Manager, orchestration abstractions, runtime-managed resources, deployments, revisions, networking abstractions, and runtime execution capabilities.

Several foundational platform capabilities remain undefined or only partially specified.
Resource identity and permissions are the current first foundation focus. The
initial provider model must support a separate development identity server for
local work and Microsoft Entra ID (Azure AD) as a required external
OIDC/OAuth provider target.

This proposal identifies the platform areas that should be treated as foundational and prioritized before introducing more advanced platform features.

## Problem

Many CloudShell features depend on platform-wide concepts that are not yet fully defined.

Examples include:

* secret access
* service-to-service authorization
* storage ownership
* audit history
* deployment traceability
* resource metrics
* usage reporting
* reconciliation behavior

Without clear platform foundations:

* providers may implement incompatible behavior
* resource capabilities become difficult to standardize
* diagnostics become fragmented
* operational tooling becomes harder to build
* platform portability becomes harder to achieve

CloudShell should establish a common foundation for these concerns.

## Foundation Areas

### Identity and Permissions

CloudShell requires a platform-wide identity model.

Areas include:

* resource identity
* workload identity
* service identity
* provider identity
* authentication
* authorization
* secret access
* delegated access

This foundation enables secure interaction between resources and infrastructure services.

### Storage

CloudShell requires a portable storage model.

Areas include:

* volumes
* persistent storage
* storage ownership
* attachment lifecycle
* access modes
* storage capabilities
* storage provisioning

Resources should be able to express storage requirements without depending on a specific storage implementation.

### Traceability and Audit

CloudShell requires a platform-wide traceability model.

Areas include:

* resource changes
* deployment history
* revision history
* lifecycle operations
* ownership changes
* reconciliation events
* audit records

The platform should be able to explain how the system reached its current state.

### Usage Monitoring and Metrics

CloudShell requires a standard mechanism for exposing operational information.

Areas include:

* resource metrics
* workload metrics
* storage usage
* network usage
* health signals
* custom metrics
* usage reporting

Resources should be able to expose usage information through a common platform abstraction.

### Resource Operations and Reconciliation

CloudShell requires a consistent model for resource reconciliation.

Areas include:

* desired state
* observed state
* reconciliation loops
* drift detection
* runtime synchronization
* lifecycle transitions

This foundation supports orchestration, runtime-managed resources, deployments, and operational tooling.

## Relationship to Existing Proposals

This proposal does not define the detailed behavior of these areas.

Instead, it identifies them as foundational platform concerns that require dedicated proposals.

Future proposals are expected for:

* Resource Identity and Permissions
* Storage Resources and Volume Management
* Traceability and Audit
* Usage Monitoring and Metrics
* Resource Reconciliation

The current foundation order starts with identity and permissions, then uses
that model to secure secret access and audit decisions before broadening into
host/runtime ownership, runtime-managed resources, and deployments.

## Implementation Plan

1. Identify platform foundation areas.
2. Define boundaries and responsibilities for each area.
3. Create dedicated proposals for each foundation.
4. Align existing proposals with the resulting platform model.
5. Add common APIs and abstractions where appropriate.

## Remaining Tasks

* Identify cross-cutting dependencies.
* Define common terminology.
* Define shared platform services.

## Open Questions

* After identity and permissions, which foundation should be implemented next?
* Which areas require Resource Manager support?
* Which areas belong to providers versus platform services?
* Which capabilities should be mandatory for providers?
* How should foundational services be exposed to resources?
