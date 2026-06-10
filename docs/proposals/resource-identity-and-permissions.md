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
    .WithIdentity("managed")
    .WithIdentity(identity =>
    {
        identity.Scopes.Add("db.read");
    });
```

## Remaining tasks

- Define the resource identity-provider contract and default selection rules.
- Add the reference development identity server hosting path and OIDC/OAuth
  configuration.
- Add Microsoft Entra ID (Azure AD) provider configuration and compatibility
  tests for tokens, claim mapping, groups or app roles, and service-principal
  automation.
- Decide how resource identity should inherit from a resource group or parent
  resource.
- Add resource-level permission names and policy evaluation rules.
- Wire the identity contract into at least one provider-backed workload type.
