# Platform Foundations Proposal

## Status

In progress.

CloudShell already provides a resource model, Resource Manager, orchestration abstractions, runtime-managed resources, deployments, revisions, networking abstractions, and runtime execution capabilities.

Several foundational platform capabilities remain undefined or only partially specified.
MVP convergence is the current foundation focus: keep supported samples,
Resource Manager behavior, diagnostics, lifecycle activity, settings/secrets,
and built-in identity flows reliable before broadening into larger platform
features. The identity provider model must still support a separate
development identity server for local work and Microsoft Entra ID (Azure AD)
as a required external OIDC/OAuth provider target.

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
* UI mutability policy

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

### Endpoint Sources and Exposure Defaults

CloudShell should keep endpoint source precedence explicit across resource
types. A simple default order is:

1. explicit resource endpoint declarations
2. explicit opt-in conventions, such as ASP.NET Core launch settings
3. provider or resource-type defaults

Provider defaults should be treated as local development helpers unless a
resource type explicitly documents otherwise. An Aspire-compatible helper can
declare a resource endpoint and produce an endpoint mapping to the implied
default local network, where the current local topology resolves that mapping
to `localhost` or loopback. Public exposure, ingress, network-level mapping,
and DNS naming should be explicit resource declarations or operator choices,
not surprising side effects of implicit endpoint discovery. Future work can
decide whether hosts may configure convention-based endpoint loading globally,
but that should include diagnostics or UI warnings when a convention is
ignored because explicit endpoints are present.

### UI Mutability and Read-Only Mode

CloudShell needs a consistent way to decide when the Resource Manager UI can
mutate resources.

Local development and programmatic-declaration scenarios may want the UI to be
inspection-only so users do not accidentally override resources owned by code.
Team or production-like environments may allow UI-created resources and
operator-driven actions. The policy should be explicit, permission-aware, and
visible in the UI.

Read-only mode should disable create, update, delete, and resource-action
workflows while preserving Resource Manager inspection, diagnostics, logs,
activity, topology, identity, configuration reference display, and network
exposure views.

## Relationship to Existing Proposals

This proposal does not define the detailed behavior of these areas.

Instead, it identifies them as foundational platform concerns that require dedicated proposals.

Future proposals are expected for:

* Resource Identity and Permissions
* Logging Infrastructure
* Storage Resources and Volume Management
* Traceability and Audit
* Usage Monitoring and Metrics
* Resource Reconciliation
* Endpoint Sources and Exposure Defaults
* UI Mutability and Read-Only Mode

The current foundation order starts with MVP convergence, then uses targeted
identity and permission work to secure secret access and audit decisions before
broadening into host/runtime ownership, runtime-managed resources, on-premise
hosting, and deployments.

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
* Define cross-resource endpoint source precedence and exposure-default policy.
* Define Resource Manager read-only mode and mutability policy.

## Open Questions

* Which foundation should be implemented next after MVP convergence?
* Which areas require Resource Manager support?
* Which areas belong to providers versus platform services?
* Which capabilities should be mandatory for providers?
* How should foundational services be exposed to resources?
