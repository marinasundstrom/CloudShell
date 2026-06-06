# Authentication and authorization

CloudShell uses ASP.NET Core authentication schemes for identity and a common
claims contract for authorization. Authentication and persistence are selected
independently in `CloudShell.Host/appsettings.json`.

## Authentication modes

Set `Authentication:Mode` to one of the following values.

### Local Identity

`Identity` uses ASP.NET Core Identity with the configured persistence provider.
It is the default mode.

```json
{
  "Authentication": {
    "Enabled": true,
    "Mode": "Identity",
    "AllowLocalSetup": true
  }
}
```

When `AllowLocalSetup` is enabled and no users exist, `/account/setup` creates
the first administrator. Keep this option disabled in production after
provisioning accounts.

### Dashboard secret

`Secret` presents a dashboard-secret login and creates an administrator
session. Store the secret outside committed configuration.

```json
{
  "Authentication": {
    "Enabled": true,
    "Mode": "Secret"
  }
}
```

Configure the secret with user secrets, a secret store, or an environment
variable:

```bash
Authentication__Secret="replace-with-a-long-random-secret"
```

### OpenID Connect

`OpenIdConnect` works with Microsoft Entra ID (Azure AD) and other OIDC
providers. CloudShell uses an authorization-code flow with PKCE and a local
cookie session.

```json
{
  "Authentication": {
    "Enabled": true,
    "Mode": "OpenIdConnect",
    "RoleClaimType": "roles",
    "OpenIdConnect": {
      "Authority": "https://login.microsoftonline.com/TENANT_ID/v2.0",
      "ClientId": "CLIENT_ID",
      "CallbackPath": "/signin-oidc",
      "RequireHttpsMetadata": true,
      "Scopes": [ "openid", "profile", "email" ]
    }
  }
}
```

Set `Authentication__OpenIdConnect__ClientSecret` through a secure
configuration provider. `MetadataAddress` can be used instead of `Authority`.

### External ASP.NET Core scheme

`External` tells CloudShell to use schemes registered by custom host code or a
trusted extension:

```json
{
  "Authentication": {
    "Enabled": true,
    "Mode": "External",
    "DefaultScheme": "CustomCookie",
    "ChallengeScheme": "CustomProvider"
  }
}
```

The named authentication handlers must be registered with
`IServiceCollection.AddAuthentication()` before the application is built.
This mode supports providers that aren't OIDC without coupling CloudShell to a
provider-specific package.

## Roles and permissions

Roles map to permissions through configuration. The default roles are:

- `CloudShell.Administrator`: all permissions and all scopes.
- `CloudShell.Contributor`: read, create, and manage permissions.
- `CloudShell.Reader`: read permissions.

Available permissions are:

- `resource-groups.read`
- `resource-groups.create`
- `resource-groups.manage`
- `resources.read`
- `resources.create`
- `resources.manage`

Role permissions and group scopes can be replaced in configuration:

```json
{
  "Authentication": {
    "RolePermissions": {
      "Platform.Reader": [ "resource-groups.read", "resources.read" ]
    },
    "RoleResourceGroups": {
      "Platform.Reader": [ "RESOURCE_GROUP_ID" ]
    }
  }
}
```

The role claim type is configured with `Authentication:RoleClaimType`.

## Direct claims

Identity providers can issue permissions and scopes directly:

| Claim type | Value |
| --- | --- |
| `cloudshell.permission` | A permission name or `*` |
| `cloudshell.resource-group` | A resource-group ID, `__ungrouped`, or `*` |
| `cloudshell.resource` | A resource ID or `*` |

Access requires both an operation permission and a matching scope. A direct
resource claim can grant access to one resource without granting access to its
entire group. Creating resource groups requires wildcard resource-group scope
because the new group ID doesn't exist before creation.

Resources inherit access from their registered root resource or resource group.
Unauthorized registrations are filtered before Resource Manager builds the
resource tree, and write operations are checked again in secured store
decorators.

## Disabling authentication

For isolated development only, set `Authentication:Enabled` to `false`.
Authorization services then allow all operations and no authentication
fallback policy is installed.
