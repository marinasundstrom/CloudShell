# DNS Zone Reference Provider

## Overview

- Resource type: `cloudshell.dnsZone`
- Provider id: `cloudshell.dns`
- Purpose: declares a DNS zone in the Resource Graph.

## Ported

- Network class/type defaults.
- Zone and provider attributes.
- Passive DNS-zone capability marker.
- Reconcile name mappings operation.
- Typed wrapper plus Resource Manager bridge projection and execution.
- Manual `ResourceDefinitionGraphBuilder.AddDnsZone(...)` builder for
  code-first DNS zone definition authoring.

## Remaining

- Name-mapping child resource integration.
- Record/conflict/materialization views as capability members or operation plans.
- DNS publisher integration and UI registration/update flow.
