# Identity Provisioning Reference Provider

## Overview

- Resource type: `cloudshell.identity-provisioning`
- Provider id: `identity.provisioning`
- Purpose: declares identity provisioning support as graph configuration while leaving actual identity provider setup to runtime integrations.

## Ported

- Infrastructure class/type defaults.
- Provider display-name, provider-id, and provider-kind attributes.
- Passive identity-provisioning capability marker.
- Setup operation with an injected provider-owned setup handler seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.
- ThirdPartyIdentity sample adapter that delegates graph setup execution to the
  Resource Manager identity setup service for the provider attached to the
  graph provisioning resource.
- Resource Manager action execution surfaces successful provider setup
  diagnostics, so the sample can verify the Keycloak runtime setup result
  through the graph operation path.
- Manual `ResourceDefinitionGraphBuilder.AddIdentityProvisioning(...)`
  builder for code-first identity provisioning definition authoring, now used
  by the ThirdPartyIdentity sample graph provisioning declaration.

## Remaining

- Full identity provider setup/materialization belongs to the runtime integration
  attached through the setup handler seam.
- Directory/client materialization, credential issuance, grant reconciliation,
  authorization, provider-owned diagnostics, and UI registration/update flow.
