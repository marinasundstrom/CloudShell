---
title: Platform resources
description: Model container hosts, storage, networking, DNS, load balancing, identity, and provider-created runtime state.
---

# Platform resources

Platform resources describe where workloads run and how they connect, store data, receive names, and obtain identity. They keep provider-specific runtime details behind a provider-neutral graph wherever possible.

## Containers and logical services

| Resource | Type ID | Use it for |
| --- | --- | --- |
| Logical service | `cloudshell.service` | An optional service facade or imported target when an application resource is not the right boundary. |
| Container host | `cloudshell.container-host` | A generic container runtime that can place and operate container workloads. |
| Docker host | `docker.host` | Docker-specific host discovery and diagnostics. |
| Docker container | `docker.container` | Low-level container state, usually provider-created; prefer a Container app for managed workloads. |

## Storage

| Resource | Type ID | Use it for |
| --- | --- | --- |
| Storage | `cloudshell.storage` | A storage provider or storage location that can own volumes. |
| Volume | `cloudshell.volume` | A CloudShell volume with provider, medium, location, access, and persistence intent. |
| Local volume | `storage.volume` | The earlier simple local-volume shape retained for compatible scenarios. |

Attach volumes to applications, databases, or brokers through graph relationships. The provider decides how authored storage intent becomes a host directory, Docker volume, or another backing implementation.

## Networking and names

| Resource | Type ID | Use it for |
| --- | --- | --- |
| Network | `cloudshell.network` | A logical network and connection boundary. |
| Virtual network | `cloudshell.virtualNetwork` | Provider-backed virtual network intent. |
| Local host networking | `cloudshell.hostNetworking.local` | Host-projected local networking capabilities. |
| macOS host networking | `cloudshell.hostNetworking.macos` | macOS-specific host networking capabilities. |
| Load balancer | `cloudshell.loadBalancer` | Routing from one exposed endpoint to one or more targets. |
| DNS zone | `cloudshell.dnsZone` | A named DNS or development-name scope. |
| Name mapping | `cloudshell.nameMapping` | A name-to-target mapping inside a DNS/name zone. |

CloudShell separates endpoint contracts, resolved addresses, exposure, routing, and names so a provider can implement local or self-hosted networking without making Docker or a public-cloud vocabulary the stable model.

## Identity

| Resource | Type ID | Use it for |
| --- | --- | --- |
| Identity provisioning | `cloudshell.identity-provisioning` | A provider-backed boundary for creating and binding workload identity. |

Identity and permission relationships should express which resource may access another resource. Credentials and secret values remain outside the resource graph.
