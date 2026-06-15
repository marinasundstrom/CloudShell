# Naming Conventions

CloudShell resource IDs are canonical addresses. Display names are optional
presentation labels.

CloudShell does not require one global resource ID naming scheme. Teams can
choose the scheme that fits their environment, provider, and operating model.
For resources that benefit from a logical hierarchy, use a stable structure
that can be projected into configuration, logs, DNS/name mappings, and
automation without relying on display names.

## Resource IDs

Use resource IDs for durable references:

- dependencies
- permissions
- activity logs
- DNS/name mapping targets
- provider state
- Control Plane API calls
- automation

Common examples:

```text
application:api
configuration:sample-app
secrets-vault:sample-app
docker:engine
```

## Optional Hierarchy Separator

When a name needs to map cleanly into JSON configuration, environment
variables, DNS-safe names, or other systems where `:` has special meaning or
is not accepted, `--` is a useful optional hierarchy separator.

Examples:

```text
application:orders--api
configuration:orders--app
secrets-vault:orders--app
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
configuration:orders--api
Orders--Api--BaseUrl
secrets-vault:orders--api
Orders--Api--ClientSecret
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
```

The convention is user-controlled guidance, not a platform requirement.

## Provider-Owned Restrictions

CloudShell resource IDs are canonical platform addresses, but CloudShell does
not impose one global character policy for every provider, cloud, or deployment
target. A provider can apply stricter validation when its backing system needs
it. For example, an Azure deployment provider may need different rules than a
local development provider, and a DNS provider has different constraints than a
configuration provider.

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

## Display Names

Display names are useful during local development when resource IDs are less
important to the immediate workflow. Programmatic declarations should apply
them explicitly with `.WithDisplayName(...)`, and samples should use them
sparingly. Display names must not be used as durable addresses.
