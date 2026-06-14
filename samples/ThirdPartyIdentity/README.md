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
- provider-neutral resource identity provisioning against an external `Oidc`
  identity provider

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

## Resource Identity Provisioning

The sample also declares the external resource identity boundary:

- `identity-provisioning:keycloak` is the provisioning resource boundary.
- `identity:keycloak` is the CloudShell resource identity provider definition.
- `application:keycloak-provisioned-api` declares a resource identity bound to
  that provider.
- `configuration:third-party-identity` grants that API identity configuration
  read access.

The application declaration calls `ProvisionIdentityOnStartup()`. The sample
registers `KeycloakResourceIdentityProvisioner`, which translates the
CloudShell provisioning request into Keycloak admin operations:

- creates or reuses a confidential client for the resource identity
- enables service-account style client credentials for that client
- creates client roles whose values match CloudShell
  `cloudshell.resource-permission` claim values
- assigns those roles to the client's service-account user
- adds a protocol mapper that emits assigned client roles as
  `cloudshell.resource-permission` claims in access tokens
- reports provisioning status by checking whether the Keycloak client exists

This is intentionally still a reference integration. It does not yet wire the
provisioned Keycloak client secret into a running CloudShell workload or
exercise a protected Configuration Store/Secrets Vault call with the issued
Keycloak token. That credential delivery and end-to-end service-call validation
is required before the Settings and Secrets resource identity flow can use
Keycloak instead of the built-in development authority.
