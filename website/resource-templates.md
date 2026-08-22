---
title: Resource templates
description: Describe CloudShell resources and their relationships as reviewable YAML desired state.
---

# Resource templates

A resource template is a YAML document that describes the resources you want CloudShell to know about. It is the portable authoring boundary used by the CLI and language launchers—not a dump of live processes, container IDs, logs, or provider credentials.

## The basic shape

```yaml
name: store-api
environment: local

resources:
  - type: configuration.store
    name: settings

  - type: application.javascript-app
    name: api
    dependsOn:
      - resourceId: configuration.store:settings
    project:
      path: ./api
```

Each entry has a resource `type` and `name`. Together they normally produce a stable ID such as `application.javascript-app:api`. Provider-owned fields such as `project.path`, `image`, or `version` describe desired resource state.

The readable YAML paths map to provider-owned attribute IDs. For example, the container app field `image` maps to the canonical container-image attribute understood by the provider. CloudShell reads the target Control Plane's resource-definition schema so the CLI can resolve those authored paths without carrying provider implementations itself.

## Relationships are part of the template

`dependsOn` records lifecycle ordering and graph intent. Endpoint or service references describe what a workload needs to discover at runtime. Storage mounts, identities, and access grants add other explicit relationships without hiding them inside application-specific configuration.

Keep the distinction clear: a dependency is not automatically a network reference, and a resource reference is not automatically permission to access the target.

## What templates should contain

- Stable resource identity and type
- Provider-owned non-secret configuration
- Endpoints and exposure intent
- Dependencies and references
- Storage, identity, and access intent
- Environment metadata that should be reviewed with the application

Templates should not contain runtime IDs, observed state, logs, generated credentials, or secret values. A Secrets Vault can be declared in a template while the protected values remain behind the service boundary.

## Apply behavior

The default apply mode creates missing resources and updates matching resources. Provider validation runs before accepted state is committed; runtime work happens afterward when the accepted change requires it. Starting, stopping, or deleting a resource remains an explicit operation rather than template syntax.

The current apply modes are:

- **Create or update:** create missing resources and incrementally update matches.
- **Create only:** reject entries whose resource identity already exists.
- **Update existing:** reject entries that do not already exist.

Applying desired state is deliberately separate from orchestration. The accepted graph passes through the owning resource providers; deployment planning then decides whether a process, container, replica group, route, storage attachment, or other runtime object must change.

## Export is desired state, not a runtime dump

Template export should contain resource definitions that can be reviewed and applied again. Provider runtime caches, process IDs, container IDs, health snapshots, activity, logs, trace data, and secret values do not belong in the document. Provider configuration that is part of the stable resource contract can be exported; observed or sensitive implementation state cannot.

Use the [resource catalog](resources/index.md) to find minimal YAML for each supported type, or follow [Build your first CloudShell app](tutorials/first-app.md) to apply a complete template.
