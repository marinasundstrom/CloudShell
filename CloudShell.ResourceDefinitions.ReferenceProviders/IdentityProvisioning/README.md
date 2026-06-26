# Identity Provisioning Reference Provider

## Overview

- Resource type: `cloudshell.identity-provisioning`
- Provider id: `identity.provisioning`
- Purpose: declares identity provisioning support as graph configuration while leaving actual identity provider setup to runtime integrations.

## Ported

- Infrastructure class/type defaults.
- Provider and provider-kind attributes.
- Passive identity-provisioning capability marker.
- Setup operation with an injected provider-owned setup handler seam.
- Typed wrapper plus apply planning and Resource Manager bridge projection/execution.

## Remaining

- Real identity provider setup.
- Directory/client materialization, credential issuance, grant reconciliation, authorization, diagnostics, and UI registration/update flow.
