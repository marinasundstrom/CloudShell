# Resource Identity and Permissions Proposal

## Status

Current implementation focus.

This proposal is the first concrete resource-level slice of the broader
[Identity and Permissions Proposal](identity-and-permissions.md). It should be
worked with that platform foundation rather than as a separate late-stage
feature.

This proposal covers resource-to-resource identity and permission handling.
It intentionally does not define secret storage or secret references.

## Problem

CloudShell already has a user/session authentication and authorization model,
but it does not yet define how resources should authenticate to one another,
or how resource-level permissions should be evaluated.

That makes it difficult to model scenarios such as:

- an API resource that uses a workload identity or managed identity
- a provider that needs a stable identity binding for service-to-service access
- resource-scoped authorization that is independent from the current user session

## Goals

- Introduce a resource identity-provider contract for resources and resource groups.
- Let Resource Manager select or inherit a default identity provider.
- Provide a separate development identity server instance that implements
  standard OIDC and OAuth 2.0 flows for local development and testing.
- Support Microsoft Entra ID (Azure AD) as a required external provider target
  through the same OIDC/OAuth contract.
- Support resource identity bindings for workload, service, and provider-owned
  scenarios.
- Extend the existing authorization model with resource-level permission concepts.

## Non-Goals

- Do not define secret storage, vaults, or secret references here.
- Do not replace the existing user authentication model.
- Do not make the development identity server part of the CloudShell domain
  model or a required production dependency.
- Do not require authentication to be enabled just to declare, inspect, or test
  resource identity shape in local development.
- Do not require every provider to implement all identity modes immediately.

## Proposed Model

### Resource identity providers

CloudShell should introduce a small identity-provider abstraction for resources:

```csharp
public sealed record ResourceIdentityProviderDefinition(
    string Id,
    string Name,
    ResourceIdentityProviderKind Kind,
    IReadOnlyDictionary<string, string> Settings);
```

Supported kinds can include:

- `BuiltIn`
- `Managed`
- `Oidc`
- `Custom`

For local development, CloudShell should host a separate reference identity
server instance and register it as an OIDC-compatible provider. That server is
development infrastructure for issuing tokens, exercising client credentials,
and validating workload authentication. Production and team environments
must be able to use Microsoft Entra ID (Azure AD) through the same provider
contract, including issuer and audience validation, claim mapping, group or app
role mapping, and client-credentials/service-principal flows for automation.
Other standards-compliant providers such as Keycloak, Auth0, and Okta should
remain replaceable options without changing CloudShell's resource identity
model.

When CloudShell authentication is disabled for isolated local development,
resource identity declarations should still be accepted and projected. Runtime
authorization is bypassed in that mode, but the declared identity shape remains
valuable because applications, providers, templates, and Resource Manager can
exercise the same identity metadata they will use later with a real provider.

Local development should support a mock or development identity provider. That
provider can issue deterministic subjects, scopes, and claims or simply project
the declared binding without acquiring tokens. This lets a team start an app
locally, verify the intended identity contract, and gradually wire the same
resource to Microsoft Entra ID or another provider before publishing.

### Resource identity bindings

Resources should be able to declare which identity provider they rely on:

```csharp
public sealed record ResourceIdentityBinding(
    string ProviderId,
    string? Subject = null,
    IReadOnlyList<string> Scopes = null,
    IReadOnlyDictionary<string, string> Claims = null);
```

This gives providers a stable contract for identity selection, workload identity,
scopes, and provider-specific metadata.

The first projection slice adds `ResourceIdentityProviderDefinition` and
`ResourceIdentityBinding` public contracts. A resource can project an optional
identity binding with provider ID, subject, scopes, and non-secret claim
metadata. The Control Plane API and remote client map that binding through
`ResourceResponse.identity`. Default provider selection, inheritance, and
provider-backed token behavior remain separate implementation work.

### Resource permissions

Resource permissions should extend the existing resource-group and resource
authorization model:

- `resource.identity.read`
- `resource.identity.manage`
- provider-specific permission names
- Azure RBAC-style resource action operations such as
  `CloudShell.Resources/resources/lifecycle/action`

The evaluation model should be able to reason about resource identity and
resource scope instead of relying only on user session claims.

The first implementation slice maps standard CloudShell resource actions to
operation permissions:

- `run`, `stop`, `pause`, and `restart` ->
  `CloudShell.Resources/resources/lifecycle/action`
- custom actions with a declared permission -> that declared Azure-style
  operation
- custom actions without a declared permission ->
  `CloudShell.Resources/resources/actions/execute/action`

The existing `resources.manage` permission remains a compatibility superset
while the model moves toward resource operation permissions.

## Proposed Fluent API

```csharp
resources.AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithIdentity("identity:dev")
    .WithIdentity(identity =>
    {
        identity.Subject = "application:api";
        identity.Scopes.Add("db.read");
        identity.Claims["appRole"] = "Api";
    });
```

Programmatic identity declarations should work before the production identity
provider exists. A local declaration can bind to `identity:dev` or another
mock provider first, then switch the provider ID and provider-specific scopes
or claims when the app is wired to Microsoft Entra ID.

## Remaining tasks

- Define default resource identity-provider selection rules.
- Add the reference development identity server hosting path and OIDC/OAuth
  configuration.
- Add a mock/development identity-provider mode that works when CloudShell
  authentication is disabled for local development and still projects the
  declared identity binding.
- Add Microsoft Entra ID (Azure AD) provider configuration and compatibility
  tests for tokens, claim mapping, groups or app roles, and service-principal
  automation.
- Decide how resource identity should inherit from a resource group or parent
  resource.
- Add resource-level permission names and policy evaluation rules.
- Add authoring APIs for resource identity bindings.
- Add programmatic identity declaration helpers that can target a mock provider
  first and later switch to Microsoft Entra ID or another production provider.
- Wire the identity contract into at least one provider-backed workload type.
