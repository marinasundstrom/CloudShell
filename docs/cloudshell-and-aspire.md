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

- Encourage developers to think in terms of resources and relationships rather than individual tools and configuration files.

In short, both products reduce the friction of getting a distributed
application running and understandable.

## CloudShell's additional value

CloudShell adds value when the team needs more than app composition:

| User need | What CloudShell adds |
| --- | --- |
| "I want a place to manage resources, not just declare them." | A resource manager shell for registering, grouping, inspecting, and operating resources. |
| "I want this to work beyond one developer's app host." | A Control Plane that can run separately from the WebUI and serve multiple shell clients. |
| "I want providers to plug in their own systems." | An extension model where providers can contribute resource types, actions, logs, views, templates, and capabilities. |
| "I want operational actions in the same place I inspect resources." | Resource actions such as start, stop, restart, image update, deployment, or provider-specific procedures. |
| "I want a UI for authored resources." | Generated resource details and provider-owned UI surfaces for richer workflows. |
| "I want resource state to survive beyond process startup." | Platform-owned registrations, groups, dependencies, and settings that can be persisted. |
| "I want local, team-owned, or on-prem platform tooling." | A self-hosted shell that can front local development, internal platforms, and environment-specific providers. |
| "I want networking to become a managed resource." | Network resources, endpoint assignment, endpoint mappings, and provider-backed networking capabilities. |
| "I want deployment workflows to be represented as resource operations." | Container app resources with image update and revision-oriented deployment APIs. |
| "I want the same model available through code, UI, and API." | Programmatic declarations, Resource Manager UI, and a domain-shaped Control Plane API over the same resource model. |
| "I want to learn infrastructure concepts without immediately adopting a public cloud." | Cloud-style resource abstractions for networking, storage, identity, deployment, and operations that can be explored locally or on self-hosted environments. |
| "I want to experiment without worrying about cloud costs." | A self-hosted resource platform that can run on a developer workstation, lab environment, or team-owned infrastructure. |
| "I want infrastructure concepts to feel familiar when I later move to Azure or another cloud." | Resource-oriented concepts that intentionally align with common cloud patterns such as networks, endpoints, storage, identities, permissions, and managed services. |

CloudShell is therefore not just an app launcher. It is a resource shell and
Control Plane that can use Aspire-like declarations as one entry point.

CloudShell should also preserve Aspire's ecosystem direction without copying
its implementation boundary. A developer should be able to use TypeScript or
JavaScript as the app-host authoring language, start or attach to a .NET
CloudShell host from that workflow, and then use the same Resource Manager and
Control Plane API as a C# host. The language SDK owns local ergonomics; the
Resource model and Control Plane remain the shared product boundary.

## Terminology alignment

Aspire separates the AppHost that composes the application from workloads that
add hosting and resource capabilities. CloudShell has a related but not
identical separation because the product models a cloud environment, not only a
distributed application host:

| Aspire-oriented term | CloudShell term | Meaning |
| --- | --- | --- |
| AppHost | CloudShell host application | The ASP.NET Core application that chooses deployment shape, configuration, authentication, persistence, and installed capabilities for a CloudShell environment. |
| Dashboard | CloudShell UI | The Blazor shell surface for Resource Manager, navigation, extension views, and operational UI. |
| Hosting/resource services | Control Plane | The service boundary that owns resource inventory, provider coordination, lifecycle operations, authorization, logs, templates, and the API. |
| Workload | CloudShell capability package | The closest equivalent: a NuGet-distributed environment capability that can contribute Control Plane providers, resource types, declarations, provider-owned services, UI integrations, shell views, and client helpers. |
| Resource | Resource | A projected CloudShell artifact that can be inspected or operated through the resource model. |

Avoid using "CloudShell workload" for package-level extensibility. In
CloudShell, workloads are things that run inside an environment, such as
application, process, project, or container-backed resources. Package-level
extensibility should use "capability package" or a more specific term such as
provider package, UI extension, or resource provider.

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
   control over hosting, networking, registries, storage, identities,
   permissions, deployment targets, or provider configuration, those pieces
   can be added to the resource model and managed through CloudShell.
4. Run it as an environment platform. The same shell can grow into a full
   on-premise Control Plane for team-owned infrastructure, provider-backed
   operations, and environment-specific resource management.


The user can therefore begin with a simple distributed application and expand
the model only when the environment needs more explicit control.

## Familiar cloud concepts

CloudShell intentionally uses resource-oriented concepts that are common across
modern cloud platforms. Applications, networks, endpoints, storage, identities,
permissions, deployments, and operational actions are represented as resources
rather than provider-specific implementation details.

This makes CloudShell useful both as a development platform and as a learning
environment. Developers can explore cloud-style architecture using local or
self-hosted infrastructure before adopting public cloud services.

The goal is not to replace cloud platforms. The goal is to make cloud
architecture more approachable by exposing familiar concepts through a
consistent resource model.

## Positioning

Use Aspire-style composition when the immediate goal is to make an application
easy to run and wire together during development.

Use CloudShell when the user also needs a persistent resource view, operational
actions, provider extensibility, UI workflows, API access, or a Control Plane
that can be shared across environments.

The practical product position is:

- Aspire is great at making application composition approachable.
- CloudShell should preserve that approachable resource declaration model.
- CloudShell extends the model beyond application composition into resource
  management, operations, networking, deployment, and environment control.
- CloudShell provides a path from local development to self-hosted and
  cloud-connected environments without requiring a different mental model.

That lets CloudShell start with familiar local-development value while leaving
room for capabilities that do not naturally belong in a single app host.
