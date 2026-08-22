---
title: CloudShell concepts
description: Understand environments, resources, providers, the Control Plane, Resource Manager, and the resource graph.
---

# CloudShell concepts

CloudShell is a tool for building, developing, and hosting distributed applications. It was designed around a hosted, cloud-like management model from the start, while providing an integrated local workflow for application developers. These are the few concepts you need to begin.

## Environment

An environment is the resource graph, Control Plane, installed providers, and UI that together describe one application environment. A developer can run an environment locally; a team can compose the same building blocks into a standing self-hosted installation.

## Resource

A resource is something CloudShell can identify, relate, inspect, and sometimes operate. Applications, databases, message brokers, configuration stores, container hosts, networks, volumes, identities, and DNS names are all resources.

Every resource has a stable type and can expose capabilities such as lifecycle operations, endpoints, health, logs, telemetry, or monitoring. A resource only offers an operation when its provider and current state support it.

## Resource graph

Resources form a graph rather than a flat list. Relationships express dependencies, containment, connections, exposure, storage attachment, identity access, and runtime materialization. Resource Manager uses that graph to explain how an application fits together and what may be affected by an operation.

## Provider

A provider translates a resource definition into behavior for an underlying implementation. The built-in local providers can work with processes, Docker, host networking, and CloudShell service runtimes. Provider-specific details stay available for diagnosis, but the primary model remains portable and resource-centered.

## Control Plane

The Control Plane owns accepted resource state, registrations, grouping, operations, authorization, orchestration, and operational data. The CLI, Resource Manager, launchers, and remote integrations use the same domain-shaped model.

## Resource Manager

Resource Manager is the browser UI for the environment. Use it to filter resources, open endpoints, invoke supported actions, inspect relationships and generated details, and move between resource context and telemetry.

## Preview boundaries

CloudShell is early preview software. The local CLI and YAML app-host workflow are the primary entry point today. C# launchers are the most complete code-first authoring path; launchers for other languages and hosted topologies are still evolving. Do not treat current APIs, packages, or resource shapes as stable compatibility commitments.

Continue with [development and hosting](development-and-hosting.md), the [CloudShell and Aspire comparison](cloudshell-and-aspire.md), [resource templates](resource-templates.md), or [launchers](launchers.md).
