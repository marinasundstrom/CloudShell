# Secrets Management Proposal

## Status

Proposed.

This proposal covers configuration references, secret references, and
Secrets Vault integration. CloudShell should provide the resource model,
resolver contracts, UI wiring, redaction, and template behavior needed to pass
settings and secrets safely. Configuration providers own configuration-entry
storage. Secrets providers own secret storage, retrieval, versioning, and
rotation behavior.

This proposal intentionally leaves resource identity rules to the separate
identity-and-permissions proposal.

## Problem

CloudShell currently has no first-class resource model for secret references.
Configuration services can mark entries as secret, but settings and secrets
are still represented by the same configuration entry shape. Providers must
either embed secrets in configuration or invent ad-hoc wiring, which makes
secret handling hard to validate, secure, and export safely.

This blocks scenarios such as:

- passing a database password into an application resource
- assigning an application setting from a secret reference in the UI
- assigning an application setting from a configuration-store entry
- passing selected host `IConfiguration` values, such as development
  `appsettings.json` values, into resources
- resolving a secret from a CloudShell Secrets Vault or another provider-backed
  secret source
- exporting resource templates without leaking secret material

## Goals

- Introduce a secret-reference abstraction for resources.
- Keep app settings separate from secrets. App settings are non-secret
  configuration values; secret references are non-secret pointers to
  provider-owned secret values.
- Let app settings use literal values or non-secret configuration-entry
  references.
- Support a separate host-configuration source provider for development
  scenarios where the CloudShell host explicitly passes selected
  `IConfiguration` values into resources.
- Support CloudShell Secrets Vault as the first built-in implementation target
  while keeping secret retrieval behind a secrets-provider integration point.
- Let users assign secret references in Resource Manager UI, similar to Azure
  application settings and Key Vault reference flows.
- Keep secret values out of the public resource model and out of exported
  templates by default.
- Allow providers to resolve secret references at runtime without exposing the
  value in UI or API projections.

## Non-Goals

- Do not define resource identity or permissions here.
- Do not make CloudShell itself the vault or introduce a full enterprise
  secret-management platform in the first version.
- Do not expose raw secret values through standard resource attributes.
- Do not require all resource providers to support secret references
  immediately.

## Proposed Model

### App settings

App settings are resource configuration values that are safe to project,
inspect, and export. They can be literal values or references to non-secret
configuration-store entries. They should remain separate from secrets even when
a provider eventually materializes both settings and secrets as environment
variables.

```csharp
public sealed record AppSetting(
    string Name,
    AppSettingValue Value);

public abstract record AppSettingValue;

public sealed record LiteralAppSettingValue(string Value) : AppSettingValue;

public sealed record ConfigurationEntryReference(
    string StoreResourceId,
    string EntryName,
    string? Version = null) : AppSettingValue;
```

Application-style resources should be able to declare settings separately from
environment variables:

```csharp
var settings = resources.AddConfigurationStore("configuration:app", "App Settings");

resources.AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithAppSetting("Database:Host", "postgres")
    .WithAppSetting("Database:Name", settings.Entry("database-name"));
```

Configuration-entry references are for non-secret settings. If an existing
configuration store entry is marked secret, new authoring surfaces should guide
the user to move it to a vault secret and bind a `SecretReference` instead.

### Host configuration sources

Development hosts should be able to expose selected values from the host
application's `IConfiguration`, such as `appsettings.Development.json` or user
secrets, through a separate provider. This should be an explicit opt-in source,
not a default bridge that exposes all host configuration to every resource.

For example:

```csharp
var hostSettings = resources.AddHostConfigurationSource("configuration:host-dev")
    .WithEntry("ExternalApi:BaseUrl")
    .WithEntry("FeatureFlags:UseMockPayments");

resources.AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithAppSetting("ExternalApi:BaseUrl", hostSettings.Entry("ExternalApi:BaseUrl"));
```

The host-configuration provider should use the same
`ConfigurationEntryReference` and resolver path as configuration service
entries. It should only resolve entries that the host explicitly exposes.
Secrets from host user-secrets or environment variables should still be modeled
as vault-backed secret references unless the host provider deliberately
declares a development-only secret source with redaction and export safeguards.

### Secret references

A secret reference is a non-secret pointer to provider-owned secret data:

```csharp
public sealed record SecretReference(
    string VaultResourceId,
    string SecretName,
    string? Version = null);
```

Secret references may be stored in provider-owned resource configuration,
projected in templates, and shown in UI as references. They must not be
resolved into secret values for resource projection, generated details, API
responses, logs, or template export.

### Secret-backed settings and environment variables

Resources should be able to bind an app setting from a configuration entry and
bind a setting or environment variable to a secret reference without embedding
the referenced value:

```csharp
public sealed record SecretBackedSetting(
    string Name,
    SecretReference Secret);
```

For application resources, the first-class authoring surface should support
both non-secret settings and secret-backed environment variables:

```csharp
var vault = resources.AddSecretsVault("secrets-vault:app");
var settings = resources.AddConfigurationStore("configuration:app", "App Settings");

resources.AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithAppSetting("Database:Host", settings.Entry("database-host"))
    .WithEnvironment("DB_PASSWORD", vault.Secret("db-password"));
```

The provider owns how the target platform receives those values. A local
process provider can inject the resolved secret as an environment variable at
start time. A container provider can create platform-native secret references
or pass the value through a protected runtime channel when no better native
mechanism exists.

### Consumption paths

CloudShell should support two complementary consumption paths:

- Resource assignment: the host or Resource Manager assigns literal values,
  configuration-entry references, or secret references to resource environment
  variables. CloudShell resolves the references at start/deploy time and passes
  the materialized value through the resource execution boundary.
- In-process configuration: the application uses CloudShell client providers
  to load values at runtime. Non-secret settings can use
  `CloudShell.Configuration`; secrets should use a separate secrets client so
  secret-specific authentication, diagnostics, redaction, caching, and rotation
  behavior do not get hidden inside the settings client.

Resource assignment is useful when the target application expects environment
variables or when the provider can map references to a native platform
setting/secret feature. In-process configuration is useful when the application
opts into CloudShell-aware configuration loading and wants settings to flow
through the standard .NET configuration stack.

Both paths should use the same reference concepts and redaction rules. The
resource-assignment path must not require the application to reference the
CloudShell client library. The in-process path must not require every setting
or secret to be copied into the resource's environment. The settings client and
secrets client should remain separate even if an application composes both into
its final runtime configuration.

### Secrets Vault resources

A Secrets Vault resource exposes secret references and lookup capability.
CloudShell Secrets Vault is the built-in implementation, and other providers
can implement the same secrets-provider integration point for external stores.
Multiple vault resources can exist in the same CloudShell environment; a
`SecretReference` identifies the vault resource ID plus the secret name and
optional version.

```csharp
var vault = resources.AddSecretsVault("secrets-vault:app");

resources.AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithEnvironment("DB_PASSWORD", vault.Secret("db-password"));
```

The built-in Secrets Vault implementation can reuse lessons from the existing
configuration service's secret-entry behavior, but it should remain a separate
provider-owned secrets model rather than another configuration entry flag.
Configuration services should continue to provide non-secret settings.

### Runtime resolution

CloudShell should define a provider integration point for resolving secret
references at runtime:

```csharp
public interface ISecretReferenceResolver
{
    ValueTask<SecretReferenceResolutionResult> ResolveSecretAsync(
        SecretReference secret,
        SecretResolutionContext context,
        CancellationToken cancellationToken = default);
}
```

The resolution context should include the target resource ID, resource group,
operation, and any future resource identity information. Resolvers return a
short-lived resolved value or a diagnostic. Missing required references should
block start/deploy operations with a clear resource procedure result.
Optional references can be omitted when the provider explicitly supports that.

Resolved values should stay inside the execution boundary. They should not be
written to persistent provider configuration unless the target platform's
native secret store requires it.

### Resource Manager UI

Resource Manager should let users assign literal values, configuration-entry
references, and secret references anywhere an application resource supports app
settings or environment variables.

The UI should provide an Azure-like editing flow:

- choose whether a setting value is a literal value, a configuration entry, or
  a secret reference
- for configuration entries, select a configuration store resource and entry
  name, with optional version if the provider supports versions
- select a vault resource visible to the current resource group or enter a
  provider-supported vault resource ID
- select or type the secret name
- optionally select or type a version
- show the saved value as a reference such as
  `@CloudShell.Configuration(storeResourceId=configuration:app; entryName=database-host)`
  or
  `@CloudShell.Secret(vaultResourceId=secrets-vault:app; secretName=db-password)`
- never display resolved secret values
- show diagnostics when the selected vault cannot resolve the reference

The UI should not require users to paste secret values into application
settings when a vault reference is available. For the built-in Secrets Vault
provider, a separate vault management view can create or rotate the secret
value, while the application settings UI only binds to the reference.

## Proposed Fluent API

```csharp
var vault = resources.AddSecretsVault("secrets-vault:app");
var settings = resources.AddConfigurationStore("configuration:app", "App Settings");

resources.AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithAppSetting("Database:Host", settings.Entry("database-host"))
    .WithEnvironment("DB_PASSWORD", vault.Secret("db-password"));
```

The `settings.Entry(...)` helper creates a `ConfigurationEntryReference`; it
does not copy the entry value into the application resource definition. The
`vault.Secret(...)` helper creates a `SecretReference`; it does not resolve the
secret value. Passing either reference to an app-setting or environment API
records a reference that the provider resolves at runtime.

## Template and Export Behavior

Application resources should export literal app settings, configuration-entry
references, and secret references, but never resolved secret values. Secrets
Vault resources should export secret names and placeholders, not secret
material. External secrets providers should export only non-secret locator
metadata needed to rebind the reference.

Imported templates should preserve references when the referenced vault
resource exists in the template or target environment. When a required
reference cannot be rebound, import should return diagnostics instead of
silently creating a resource that cannot start.

## Migration from Configuration Services

Configuration services currently represent settings and secrets as entries
with an `IsSecret` flag. The implementation should split that responsibility:

- configuration services remain the source for non-secret app settings and can
  be referenced by entry name
- Secrets Vault resources own secret values and expose secret references
- applications can depend on configuration services for settings and on vault
  resources for secret resolution

Existing secret entries in configuration stores can be migrated to Secrets
Vault resources or kept behind compatibility behavior until the vault resource
type is broadly available. New authoring surfaces should guide users toward
app settings for non-secret values and vault-backed references for secrets.

## Remaining tasks

- Add Resource Manager UI support for assigning literal app settings and
  configuration-entry or vault-backed secret references.
- Decide how secret references should be versioned, rotated, and refreshed for
  already-running resources.
- Add a separate secrets client/provider for in-process secret loading.
