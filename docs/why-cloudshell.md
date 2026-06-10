# Why CloudShell

## The problem

Modern software is rarely a single application. Most systems are distributed applications composed of multiple services, databases, APIs, and supporting infrastructure.

Even small distributed applications often depend on databases, APIs, containers, networking, storage, deployment workflows, observability tools, and other infrastructure services. While cloud platforms provide powerful solutions for these concerns, they also introduce complexity, cost, and a large number of provider-specific concepts.

Many developers learn application development long before they learn infrastructure. The transition between the two can be difficult because infrastructure is often presented as a collection of services, tools, and configuration systems rather than as a coherent model.

CloudShell exists to make that transition easier.

## A resource-oriented approach

CloudShell is built around the idea that applications and infrastructure can be described as resources and relationships.

Applications, databases, networks, storage, identities, permissions, endpoints, deployments, and operational capabilities are all represented through a common resource model.

Instead of focusing on individual products or implementation technologies, CloudShell focuses on what a system consists of and how its parts relate to each other.

This allows developers to reason about systems at a higher level while still retaining control over the underlying implementation.

## Learn cloud architecture without requiring a cloud

CloudShell intentionally adopts concepts that are familiar to users of modern cloud platforms.

Networks, endpoints, identities, permissions, deployments, storage, and operational actions are represented as first-class resources. These concepts are common across many cloud providers and infrastructure platforms.

Developers can therefore learn cloud-style architecture locally or in self-hosted environments before adopting public cloud services.

The goal is not to replace cloud platforms. The goal is to make cloud architecture easier to understand and experiment with.

## Start simple and grow gradually

Many platforms require users to understand infrastructure from the beginning.

CloudShell takes a different approach.

A project can begin as a simple distributed application model consisting of a few applications and backing services. As requirements grow, additional infrastructure resources can be introduced into the same model.

Networks, storage, identities, permissions, deployment targets, load balancing, observability, and operational workflows can be added when they become relevant.

Users do not need to commit to a full infrastructure model on day one.

## Experiment without unnecessary cost

Public cloud platforms are powerful, but experimentation can become expensive.

CloudShell can run on a developer workstation, a lab machine, a small server, or team-owned infrastructure. This allows developers to explore architectural concepts, deployment workflows, and infrastructure management without requiring cloud subscriptions or worrying about resource consumption costs.

The result is a platform where experimentation is encouraged rather than avoided.

## One model, multiple environments

CloudShell uses the same resource model for distributed applications across local development, shared development environments, testing environments, and self-hosted deployments.

Resources can be defined through code, managed through the user interface, and operated through the Control Plane API.

As environments evolve, the underlying concepts remain the same.

This reduces the gap between development and operations while making infrastructure easier to understand and manage.

## The vision

CloudShell exists to make cloud-inspired architecture accessible.

It provides a resource-oriented platform where developers can build distributed applications, learn infrastructure concepts, experiment safely, and gradually evolve from local development to fully managed environments using a consistent set of abstractions.

The goal is not to abstract infrastructure away. The goal is to make infrastructure understandable.
