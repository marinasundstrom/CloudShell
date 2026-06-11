# Resource Identity and Permissions

Resource identity describes how a CloudShell resource represents itself when it
needs to call another resource, platform service, provider, or external
authority. Resource permissions describe which operations an identity or user
can perform on a resource.

User authentication is documented separately in
[Authentication and authorization](authentication-and-authorization.md). This
document focuses on resource-to-resource identity, resource identity provider
selection, projected identity metadata, and resource operation permissions.

## Resource Identity

Identity is part of the core resource model. It is not modeled only as a
`ResourceCapability`, because callers need a stable way to inspect a resource's
identity intent without first interpreting provider-specific capabilities.

A resource may still advertise identity-related capabilities when a provider
supports extra behavior such as token issuance, managed identity provisioning,
protected API registration, or permission assignment. The identity binding
itself remains resource metadata.

## Identity Providers

Resource identity providers are configured independently from the user sign-in
provider. A provider definition names the identity system that can resolve
workload identity metadata or eventually issue and validate tokens for a
resource.

```json
{
  "ResourceIdentity": {
    "DefaultProviderId": "identity:entra",
    "Providers": [
      {
        "Id": "identity:entra",
        "Name": "Microsoft Entra ID",
        "Kind": "Oidc",
        "Settings": {
          "Authority": "https://login.microsoftonline.com/{tenantId}/v2.0",
          "Audience": "api://cloudshell-control-plane"
        }
      }
    ]
  }
}
```

Supported provider kinds:

| Kind | Use |
| --- | --- |
| `BuiltIn` | CloudShell-owned or local built-in identity behavior. |
| `Managed` | Provider-managed identity systems. |
| `Oidc` | OIDC/OAuth providers such as Microsoft Entra ID, Keycloak, Auth0, or Okta. |
| `Custom` | Provider-specific or host-specific identity mechanisms. |

## Provider Selection

Resource identity bindings resolve through `ResourceIdentityProviderCatalog`.

| Binding kind | Selection rule |
| --- | --- |
| `Provider` | Resolve by `ProviderId`. |
| `Required` | Resolve to `ResourceIdentity:DefaultProviderId`. If exactly one provider is configured, that provider is the implicit default. |

When multiple providers are configured, set `DefaultProviderId` explicitly for
`Required` identity bindings. If a binding cannot resolve to a configured
provider, Resource Manager reports a `resourceIdentityProviderUnresolved`
resource model diagnostic.

## Identity Bindings

Resource identity metadata is projected through `Resource.IdentityBinding`.

```csharp
public enum ResourceIdentityBindingKind
{
    Provider,
    Required
}

public sealed record ResourceIdentityBinding(
    string? ProviderId,
    string? Subject = null,
    IReadOnlyList<string>? Scopes = null,
    IReadOnlyDictionary<string, string>? Claims = null,
    ResourceIdentityBindingKind Kind = ResourceIdentityBindingKind.Provider);
```

`Provider` means the resource names a concrete provider. `Required` means the
resource requires an identity, but provider-specific details are resolved by
the default provider selection path.

Identity binding metadata must be non-secret. Do not put tokens, client
secrets, certificates, passwords, or other credentials in identity binding
claims, scopes, or provider settings.

## API Projection

The Control Plane API projects identity metadata on `ResourceResponse.identity`.

| Field | Meaning |
| --- | --- |
| `kind` | `Provider` or `Required`. |
| `providerId` | Provider ID when the binding names a concrete provider. |
| `subject` | Provider-specific subject or workload name, when known. |
| `scopes` | Requested scopes or provider-specific permission hints. |
| `claims` | Non-secret provider-specific claim metadata. |

The remote Control Plane client maps this response back to
`Resource.IdentityBinding`.

## Operation Permissions

Resource actions use Azure RBAC-style operation names. Resource Manager checks
the required operation permission before executing an action. `resources.manage`
currently remains a compatibility superset for resource actions.

| Resource type or class | Action | Permission |
| --- | --- | --- |
| Any resource with standard lifecycle actions | `run`, `stop`, `pause`, `restart` | `CloudShell.Resources/resources/lifecycle/action` |
| Any resource with a custom action and no narrower declared operation | custom action execution | `CloudShell.Resources/resources/actions/execute/action` |
| `cloudshell.network` and `cloudshell.virtualNetwork` | `reconcileEndpointMappings` | `CloudShell.Network/networks/reconcileEndpointMappings/action` |
| `cloudshell.loadBalancer` | `applyLoadBalancerConfiguration` | `CloudShell.Network/loadBalancers/applyConfiguration/action` |

When adding a new resource action, document the operation permission in this
catalog. Prefer a resource-type-specific operation for meaningful provider or
platform actions. Use the generic custom-action execute permission only when no
narrower operation exists yet.

## Local Development

When `Authentication:Enabled` is `false`, CloudShell does not enforce
token-based user authorization at the Control Plane boundary. Resource identity
metadata can still be projected and diagnosed. This allows local hosts,
templates, providers, and Resource Manager UI surfaces to exercise the intended
identity shape before a production identity provider is configured.

Development identity providers should stay replaceable infrastructure. A local
provider can model deterministic subjects, scopes, and claims, while the same
resource identity model can later be backed by Microsoft Entra ID or another
provider.

## Related Design Work

The active proposal tracks unfinished design and implementation work:
[Resource Identity and Permissions Proposal](proposals/resource-identity-and-permissions.md).
