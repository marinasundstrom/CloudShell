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
- a Keycloak-provisioned ASP.NET Core workload that uses
  `DefaultCloudShellResourceCredential` to call a protected Configuration Store

## Run

Start Keycloak:

```bash
docker compose -f samples/ThirdPartyIdentity/docker-compose.yml up -d
```

To avoid a local port conflict, set `KEYCLOAK_PORT`:

```bash
KEYCLOAK_PORT=18080 docker compose -f samples/ThirdPartyIdentity/docker-compose.yml up -d
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

The reader and contributor roles in this sample include grouped
`observability.read`, so logs, traces, metrics, Dependencies, Service map, and
the Telemetry workspace are visible for resources the signed-in user can read.
CloudShell still filters observability rows to the user's readable resources;
the observability permission does not reveal resources outside the user's
resource or resource-group scope.

## Resource Identity Provisioning

The sample also declares the external resource identity boundary:

- `identity-provisioning:keycloak` is the provisioning resource boundary.
- `identity:keycloak` is the CloudShell resource identity provider definition.
- `application:keycloak-provisioned-api` declares a resource identity bound to
  that provider.
- `configuration:third-party-identity` grants that API identity configuration
  read access.

The sample also declares `identity-provisioning:graph-keycloak`, a
side-by-side graph-backed identity provisioning resource through the Resource
Definitions bridge and the provider-owned identity provisioning builder. It
proves Resource Manager projection and setup operation execution by attaching a
graph-specific identity provider definition and a sample-local adapter that
delegates the graph setup operation to the existing Resource Manager identity
setup service through an explicit bridge contract. The graph resource stores
the attached provider id as non-secret configuration, while the provider
definition remains registered in the Resource Manager declaration model. The
existing Keycloak integration remains responsible for real provider setup,
credential materialization, and protected configuration access.

The sample now also declares side-by-side graph-backed
`configuration.store:graph-third-party-identity` and
`application.aspnet-core-project:graph-keycloak-provisioned-api` resources.
Those declarations prove the graph shape for the protected configuration store,
the API project, the API endpoint, the graph startup dependency, the
provider-owned configuration reference, and the Resource Manager-declared
identity attached to the graph API. The graph-backed Configuration Store can
also run with Keycloak `Authentication:ServiceBearer` settings, and the
graph-backed API can start through the ASP.NET Core project runtime, receive
the graph API identity credentials from `identity:graph-keycloak`, and read
the protected graph-backed configuration entry.
The graph ASP.NET Core runtime also has a sample-local environment adapter
that resolves the graph API identity declaration from Resource Manager and
delegates credential environment creation to the existing Keycloak integration.
That keeps identity credential materialization in the runtime/Control Plane
boundary while the graph resource continues to store only declarative project,
endpoint, reference, and identity attachment information.
Set `Samples:ThirdPartyIdentity:GraphOnly` to `true` to omit the old
application/configuration provider registrations and the old
`identity-provisioning:keycloak`, `configuration:third-party-identity`, and
`application:keycloak-provisioned-api` resource records. Graph-only mode still
uses Resource Manager-owned identity provider declarations and the sample-local
graph identity credential adapter; those are runtime/Control Plane concerns,
not graph state. Docker-backed smoke coverage now runs the protected graph
Configuration Store and graph ASP.NET Core API in this mode, executes graph
identity-provider setup, and verifies the graph API can read protected graph
configuration with a Keycloak-issued resource identity token without old
provider records.

The sample registers `KeycloakResourceIdentityProvisioner` as the resource
identity provisioner, provider setup handler, and runtime credential
environment provider. Provider setup is separate from individual resource
identity provisioning: it uses the Keycloak admin API to ensure the
`cloudshell-ui` client emits realm roles in the configured `roles` claim so
CloudShell user authorization can read the same claim shape after realm import
or manual client changes.

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
- adds explicit resource-permission claim mappers for declared grants so
  service-account tokens carry the CloudShell access boundary even when the
  provider emits dotted claim names as nested JSON
- exposes the standard `CLOUDSHELL_IDENTITY_*` environment variables for
  workloads that use the provisioned identity
- reports provisioning status by checking whether the Keycloak client exists

This is intentionally still a reference integration. The provisioned Keycloak
client secret is deterministic for sample-created resource clients so the
application provider can inject it into a running workload.

The sample configures both the old and graph-backed Configuration Store
backing services with `Authentication:ServiceBearer` settings derived from the
Keycloak authority. That lets each service validate Keycloak-issued JWT bearer
tokens before it checks the `cloudshell.resource-permission` claim. Audience
validation is not set by default because the sample uses Keycloak's
local-development token shape; production-style hosts should register protected
API audiences and set `Authentication:ServiceBearer:Audience`.

## Workload validation

The sample declares `application:keycloak-provisioned-api` as the old ASP.NET
Core project resource and `application.aspnet-core-project:graph-keycloak-provisioned-api`
as the graph-backed ASP.NET Core project resource. They are not autostarted.
After Keycloak and the CloudShell host are running, start either API from
Resource Manager with its dependencies. The old API listens on
`http://localhost:5234` by default, and the graph API listens on
`http://localhost:5235` by default.

Open:

```text
http://localhost:5234/configuration
http://localhost:5235/configuration
```

The endpoint uses `DefaultCloudShellResourceCredential`, reads the injected
`CLOUDSHELL_IDENTITY_*` settings for the provisioned Keycloak client, requests
a Keycloak access token, and calls the protected Configuration Store service.
The expected response has `status` set to `connected` and includes
`Sample:Message`.

The sample smoke tests can start Keycloak with Docker Compose, run the
CloudShell host, verify that `identity-provisioning:keycloak` and
`identity-provisioning:graph-keycloak` are projected as resource boundaries,
execute graph provider setup, confirm provisioning status for the old and graph
workload identities, start the dependent old and graph Configuration Store/API
resources, and assert both APIs can read protected configuration entries with a
Keycloak-issued token. A separate graph-only smoke path starts only the graph
resources and verifies the same protected configuration read without old
application/configuration provider records.
