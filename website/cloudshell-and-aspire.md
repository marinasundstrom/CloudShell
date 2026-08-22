---
title: CloudShell and Aspire
description: Compare CloudShell's self-hosted control-plane model with Aspire's application composition and developer workflow.
---

# CloudShell and Aspire

CloudShell and [Aspire](https://learn.microsoft.com/dotnet/aspire/get-started/aspire-overview) both describe distributed applications as graphs of resources. Both can start application processes and containers, connect services, surface endpoints, and collect logs and traces. The difference is their center of gravity.

Aspire is a code-first orchestration, observability, and deployment toolchain organized around an application and its AppHost. CloudShell was designed for hosting distributed applications from the start, around an independently hosted, resource-oriented control plane: an environment remains available so people and tools can inspect, operate, and govern its resources after the authoring process exits. Its development flow connects source code and developer tools to that hosting model rather than defining a separate product boundary.

CloudShell is in preview. This comparison describes its intended architecture and the capabilities implemented so far; it is not a claim of current feature parity with Aspire or a public cloud platform.

## The short version

| Concern | Aspire | CloudShell |
| --- | --- | --- |
| Authoring unit | A code-first AppHost in C# or TypeScript | A language-neutral resource template, or a language-specific launcher that produces one |
| Primary scope | Compose, run, observe, and deploy a distributed application | Register, relate, inspect, and operate resources in an environment |
| Local execution | The AppHost delegates development-time orchestration to the Developer Control Plane | The CLI starts a local CloudShell host and applies the template to its Control Plane |
| Deployment | Publishers generate deployment artifacts, and deployers can apply them to a target | Providers apply accepted resource definitions to their configured runtimes |
| Runtime authority | The AppHost and its orchestration model anchor the application workflow | The Control Plane owns accepted state, operations, authorization, and operational data |
| Persistence | The application model normally lives with the source and deployment workflow | A standing environment can retain resource inventory and operational state independently of its author |
| User interface | Aspire Dashboard for application resources and diagnostics | Resource Manager for environment inventory, relationships, provider views, actions, and telemetry |
| Intended boundary | A toolchain that bridges development and deployment | A self-hosted environment that can resemble a small cloud control plane |

## Where the models overlap

The common shape is useful. A resource graph is a better description of a distributed system than a list of processes: an API depends on a database, publishes to a broker, exposes endpoints, and emits telemetry. Both projects make those relationships visible and give developers one place to reason about the system.

Aspire has a mature code-first integration model and a strong inner loop. Its AppHost expresses resources and relationships, development-time orchestration starts the graph, and the dashboard presents resources, logs, traces, and commands. Publish and deploy workflows then translate the same application model for a target environment. Aspire explicitly is not itself a cloud provider or production runtime.

CloudShell's local workflow intentionally feels familiar: declare a graph, run it, and inspect the result. That overlap does not require the products to have the same operating boundary.

## Why CloudShell is closer to a hosting environment

In CloudShell, the Control Plane is intended to outlive any individual CLI or launcher invocation. A template is a request to change an environment, rather than the process that owns the environment. Once accepted, the Control Plane and its providers become responsible for resource identity, current state, relationships, lifecycle operations, and diagnostics.

That changes the questions the platform needs to answer:

- Which resources exist in this environment, including resources not created by the current application?
- Which provider owns each resource type, and which runtime object materializes it?
- Who may view a resource, change it, or invoke one of its actions?
- How are networks, identities, storage, placement, and secrets represented across workloads?
- What desired and observed state should survive a disconnected authoring client or a UI restart?
- How can the Resource Manager run separately from the authoritative Control Plane?

These are cloud-control-plane questions, even when every component runs on one developer machine. A shared CloudShell installation moves the same model toward a team-owned or on-premises environment: persistent inventory, explicit trust boundaries, provider policy, and an independently deployable management UI.

## Authoring is separate from authority

A CloudShell resource template is a language-neutral document. Launchers provide strongly typed builder DSLs for supported programming languages, but their output is still a normal template. The CLI applies that template through the Control Plane API.

This separation is deliberate:

```text
launcher or YAML
      |
      v
resource template
      |
      v
Control Plane ----> provider ----> runtime
      |
      +-----------> Resource Manager and API clients
```

An authoring tool can stop without becoming the source of truth for the running environment. A provider can also expose operational state that was not present in the template—for example container replicas, SQL databases, RabbitMQ exchanges, or a trace composed of spans from several services.

## Choosing between them

Choose Aspire when the application repository and its AppHost should be the center of the workflow, especially when you want its established integrations, local orchestration, dashboard, and deployment toolchain.

Explore CloudShell when you need the management environment itself to be the product boundary: a durable resource inventory, a separately hosted Control Plane and UI, provider-defined operational views, language-neutral templates, and policy set by the environment operator.

The distinction is not absolute. Both projects can support multi-language workloads, extensible resource types, telemetry, local containers, and deployment-related workflows. The useful question is who owns the resource after it has been declared: the application toolchain driving a run or deployment, or a standing control plane accepting and operating resources on behalf of multiple clients.

Next, read about [development and hosting](development-and-hosting.md), [resource templates](resource-templates.md), and [launchers](launchers.md).
