# Third-party Identity

This sample validates CloudShell user authentication and authorization against
a standards-based OpenID Connect provider. It uses Keycloak as local
development infrastructure, but CloudShell still consumes normal ASP.NET Core
claims and the existing CloudShell permission model.

The sample proves:

- OIDC sign-in through `Authentication:Mode = OpenIdConnect`
- role claim mapping through `Authentication:RoleClaimType = roles`
- CloudShell role-to-permission mapping for administrator, contributor, and
  reader users
- Resource Manager access using the same authorization service used by the
  built-in Identity and dashboard-secret modes

## Run

Start Keycloak:

```bash
docker compose -f samples/ThirdPartyIdentity/docker-compose.yml up -d
```

Run CloudShell:

```bash
dotnet run --project samples/ThirdPartyIdentity/CloudShell.ThirdPartyIdentity.csproj -- --urls http://localhost:5011
```

Open `http://localhost:5011` and sign in with one of the imported users:

| User | Password | Role |
| --- | --- | --- |
| `admin` | `local-development-password` | `CloudShell.Administrator` |
| `contributor` | `local-development-password` | `CloudShell.Contributor` |
| `reader` | `local-development-password` | `CloudShell.Reader` |

The Keycloak admin console is available at `http://localhost:8080` with
`admin` / `admin`.

## Mapping

Keycloak imports a `cloudshell` realm and a confidential `cloudshell-ui`
client. A protocol mapper emits realm roles as a multivalued `roles` claim.
CloudShell is configured to read that claim through:

```json
{
  "Authentication": {
    "RoleClaimType": "roles"
  }
}
```

The role names are not provider-specific. They are CloudShell roles mapped to
CloudShell permissions in `appsettings.json`.

This sample intentionally validates user-facing authorization first. Resource
identity provisioning against a third-party authority remains the next identity
step: the same resource identity and grant model should be mapped to external
OIDC/OAuth clients or service principals without changing authored resources.
