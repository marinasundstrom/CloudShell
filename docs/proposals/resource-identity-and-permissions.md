# Resource Identity and Permissions Proposal

## Status

Proposed.

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
- Support resource identity bindings for workload, service, and provider-owned
  scenarios.
- Extend the existing authorization model with resource-level permission concepts.

## Non-Goals

- Do not define secret storage, vaults, or secret references here.
- Do not replace the existing user authentication model.
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

The evaluation model should be able to reason about resource identity and
resource scope instead of relying only on user session claims.

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
- Decide how resource identity should inherit from a resource group or parent
  resource.
- Add resource-level permission names and policy evaluation rules.
- Wire the identity contract into at least one provider-backed workload type.
