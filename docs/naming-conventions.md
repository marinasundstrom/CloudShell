# Naming Conventions

CloudShell follows cloud-platform naming terminology:

```csharp
Resource.Id          // immutable platform identity or derived resource path
Resource.Name        // scoped unique resource name, such as "api" or "orders--api"
Resource.DisplayName // optional presentation label, such as "Orders API"
```

The current runtime model still has transitional surfaces where `Resource.Name`
is used as the display label. New authoring, UI, and documentation should use
the cloud-platform distinction above. A future model cleanup should make
`DisplayName` explicit on projected resources and keep `Name` as the
addressable scoped name.

## Resource ID

`Resource.Id` is the immutable identity CloudShell uses internally and across
Control Plane APIs. It may be a GUID, a provider-derived resource path, or a
typed path derived from resource type plus name.

Examples:

```text
application:api
configuration:sample-app
secrets-vault:sample-app
docker:engine
```

Use resource IDs for durable references:

- dependencies
- permissions
- activity logs
- DNS/name mapping targets
- provider state
- Control Plane API calls
- automation

## Resource Name

`Resource.Name` is the scoped unique name users and programmatic declarations
normally provide. For example:

```text
api
orders--api
sample-app
```

Provider and resource-type APIs derive the internal resource ID from the name
when needed. For example, an application named `api` can become
`application:api`, while a Configuration Store named `sample-app` can become
`configuration:sample-app`.

The Resource Manager create UI should ask for **Name** first, not Resource ID.
Display name comes after it and remains optional.

## Display Name

`Resource.DisplayName` is an optional presentation label. Display names are
useful during local development and demos when the scoped name is terse:

```text
Name: orders--api
DisplayName: Orders API
```

Display names must not be used as durable addresses. They can change without
changing the resource identity, dependencies, permissions, provider state, or
automation targets.

Programmatic declarations should use `.WithDisplayName(...)` only when a
friendly label adds value.

## Optional Hierarchy Separator

When a name needs to map cleanly into JSON configuration, environment
variables, DNS-safe names, or systems where `:` has special meaning or is not
accepted, `--` is a useful optional hierarchy separator.

Examples:

```text
orders--api
orders--worker
orders--configuration
```

This fits well with configuration paths because a setting or secret can be
defined in `appsettings.json` using normal JSON hierarchy and later resolved
or overridden by Configuration Store or Secrets Vault clients.

For example:

```json
{
  "Orders": {
    "Api": {
      "BaseUrl": "http://localhost:5080"
    }
  }
}
```

A team may choose names such as:

```text
Resource name: orders--api
Configuration key: Orders--Api--BaseUrl
Secret name: Orders--Api--ClientSecret
```

When Configuration Store and Secrets Vault values are loaded through the
CloudShell `IConfiguration` integrations, `--` maps to the .NET configuration
path delimiter `:`. This keeps service-backed values compatible with the
hierarchical JSON shape and the Azure-style secret-name convention.

For example, this persisted entry or secret name:

```text
Orders--Api--BaseUrl
```

is available to application code as:

```csharp
Configuration["Orders:Api:BaseUrl"]

var baseUrl = Configuration.GetValue<string>("Orders:Api:BaseUrl");

var baseUrl = Configuration
    .GetSection("Orders:Api")
    .GetValue<string>("BaseUrl");
```

The convention is user-controlled guidance, not a platform requirement.

## Provider-Owned Restrictions

CloudShell does not impose one global character policy for every resource
name, provider, cloud, or deployment target. A provider can apply stricter
validation when its backing system needs it. For example, an Azure deployment
provider may need different rules than a local development provider, and a DNS
provider has different constraints than a configuration provider.

The built-in Configuration Store and Secrets Vault intentionally follow
different conventions:

- Configuration Store keys are broad, App Configuration-style keys. The
  built-in provider rejects empty names, `%`, `.`, `..`, and control
  characters, while allowing `:` for direct .NET configuration paths and `--`
  for portable hierarchy names.
- Secrets Vault secret names are Key Vault-style names. The built-in provider
  accepts 1-127 ASCII letters, digits, and hyphens only. Use `--` for
  hierarchical configuration names because `:` is not a valid Secrets Vault
  secret-name character.

References:

- [Azure App Configuration key-value store](https://learn.microsoft.com/en-us/azure/azure-app-configuration/concept-key-value)
- [Azure Key Vault configuration provider for ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/key-vault-configuration)
- [Azure Key Vault keys, secrets, and certificates overview](https://learn.microsoft.com/en-us/azure/key-vault/general/about-keys-secrets-certificates)

## Typed Resource IDs

CloudShell provides a `ResourceId` value object for resource-ID construction
and validation. New code should prefer that value object at normalization
boundaries instead of passing raw strings through unrelated layers. Existing
manager interfaces still accept strings while the model migrates.
