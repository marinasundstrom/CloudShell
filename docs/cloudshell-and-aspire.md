# CloudShell and Aspire

CloudShell and .NET Aspire both help developers work with multi-resource
applications without turning every local environment into a pile of manual
setup steps.

The overlap is intentional. CloudShell should feel familiar to teams that like
Aspire's resource-oriented developer experience. The difference is that
CloudShell keeps going after the app is described: it gives the team a
self-hosted shell and Control Plane for registering, inspecting, operating, and
extending those resources.

## Shared value

Both CloudShell and Aspire help users:

- Describe an application as a set of related resources instead of scattered
  scripts and notes.
- Start from code-first declarations that can be checked in with the project.
- Connect applications to backing services through references and endpoints.
- Make local development easier by reducing repeated environment setup.
- Give developers a single place to see the pieces that make up an app.
- Support container-backed services and project-backed applications.
- Make common app topology easier to understand for new contributors.

In short, both products reduce the friction of getting a distributed
application running and understandable.

## CloudShell's additional value

CloudShell adds value when the team needs more than app composition:

| User need | What CloudShell adds |
| --- | --- |
| "I want a place to manage resources, not just declare them." | A resource manager shell for registering, grouping, inspecting, and operating resources. |
| "I want this to work beyond one developer's app host." | A Control Plane that can run separately from the WebUI and serve multiple shell clients. |
| "I want providers to plug in their own systems." | An extension model where providers can contribute resource types, actions, logs, views, templates, and capabilities. |
| "I want operational actions in the same place I inspect resources." | Resource actions such as run, stop, restart, image update, deployment, or provider-specific procedures. |
| "I want a UI for authored resources." | Generated resource details and provider-owned UI surfaces for richer workflows. |
| "I want resource state to survive beyond process startup." | Platform-owned registrations, groups, dependencies, and settings that can be persisted. |
| "I want local, team-owned, or on-prem platform tooling." | A self-hosted shell that can front local development, internal platforms, and environment-specific providers. |
| "I want networking to become a managed resource." | Network resources, endpoint assignment, endpoint mappings, and provider-backed networking capabilities. |
| "I want deployment workflows to be represented as resource operations." | Container app resources with image update and revision-oriented deployment APIs. |
| "I want the same model available through code, UI, and API." | Programmatic declarations, Resource Manager UI, and a domain-shaped Control Plane API over the same resource model. |

CloudShell is therefore not just an app launcher. It is a resource shell and
Control Plane that can use Aspire-like declarations as one entry point.

## Growth path

CloudShell lets a solution grow without forcing the user to choose the full
infrastructure model on day one.

1. Start with the application model. Declare a web app, another worker or API,
   and a database through the programmatic resource API. The workflow feels
   like Aspire: describe the distributed app, run it locally, and let the
   system handle the implied container and endpoint details.
2. Share the development environment. Host the CloudShell environment so other
   developers, testers, or stakeholders can inspect and operate the same
   resources without setting up separate infrastructure. Build pipelines can
   integrate with the Control Plane to update running resources.
3. Add infrastructure when it starts to matter. When the team wants more
   control over hosting, networking, registries, deployment targets, or
   provider configuration, those pieces can be added to the resource model and
   managed through CloudShell.
4. Run it as an environment platform. The same shell can grow into a full
   on-premise Control Plane for team-owned infrastructure, provider-backed
   operations, and environment-specific resource management.

The user can therefore begin with a simple distributed application and expand
the model only when the environment needs more explicit control.

## Positioning

Use Aspire-style composition when the immediate goal is to make an application
easy to run and wire together during development.

Use CloudShell when the user also needs a persistent resource view, operational
actions, provider extensibility, UI workflows, API access, or a Control Plane
that can be shared across environments.

The practical product position is:

- Aspire is great at making application composition approachable.
- CloudShell should preserve that approachable resource declaration model.
- CloudShell adds the management layer around those resources: shell,
  providers, operations, persistence, API, and environment control.

That lets CloudShell start with familiar local-development value while leaving
room for capabilities that do not naturally belong in a single app host.
