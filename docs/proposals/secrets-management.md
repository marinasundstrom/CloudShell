# Secrets Management Proposal

## Status

Proposed.

This proposal covers secret storage, secret references, and vault-style
resource integration. It intentionally leaves resource identity rules to the
separate identity-and-permissions proposal.

## Problem

CloudShell currently has no first-class resource model for secret references.
Providers must either embed secrets in configuration or invent ad-hoc wiring,
which makes secret handling hard to validate, secure, and export safely.

This blocks scenarios such as:

- passing a database password into an application resource
- resolving a secret from a local or provider-backed vault
- exporting resource templates without leaking secret material

## Goals

- Introduce a secret-reference abstraction for resources.
- Support a local vault-style resource as the first implementation target.
- Keep secret values out of the public resource model and out of exported
  templates by default.
- Allow providers to resolve secret references at runtime without exposing the
  value in UI or API projections.

## Non-Goals

- Do not define resource identity or permissions here.
- Do not introduce a full enterprise secret-management platform in the first
  version.
- Do not expose raw secret values through standard resource attributes.

## Proposed Model

### Secret references

A secret reference should be a non-secret pointer to provider-owned data:

```csharp
public sealed record SecretReference(
    string VaultResourceId,
    string SecretName,
    string? Version = null);
```

### Vault-style resources

A local or provider-backed vault resource should expose secret lookups:

```csharp
var vault = resources.AddVault("vault");

var api = resources.AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithEnvironment("DB_PASSWORD", vault.Secret("db-password"));
```

The vault resource is the stable abstraction; the provider owns the actual
secret store implementation.

## Proposed Fluent API

```csharp
var vault = resources.AddVault("vault");

resources.AddContainerApplication("api", "ghcr.io/example/api:latest")
    .WithEnvironment("DB_PASSWORD", vault.Secret("db-password"));
```

## Remaining tasks

- Define the first vault-resource contract and provider-owned storage model.
- Decide how secret references should be versioned or rotated.
- Add safe export behavior so templates use placeholders or references instead
  of secret material.
- Add runtime resolution, redaction, and failure diagnostics for secret
  references.
