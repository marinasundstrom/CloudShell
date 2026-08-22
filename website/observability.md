---
title: Telemetry and observability
description: Use CloudShell to move between application telemetry, resource monitoring, health, and platform activity.
---

# Telemetry and observability

CloudShell keeps operational signals close to the resources that produce them. The telemetry workspace is the best overview when you are investigating application behavior across more than one service; resource pages keep the same signals anchored to one application or dependency.

## Telemetry

Application and runtime signals appear under **Telemetry**:

- **Logs** combine resource-addressed output and provider log sources.
- **Traces** correlate spans across requests and service boundaries.
- **Metrics** show time-series measurements emitted by applications and runtimes.
- **Dependencies** and **Service map** use relationships and trace context to explain request flow.

The current preview can retain traces and telemetry metric points in memory, with optional database-backed history when the host is configured for it. Source logs remain owned by their provider or backing log system.

## Monitoring, activity, and health

These signals answer different questions:

| Signal | Use it to understand |
| --- | --- |
| Monitoring | Current provider-observed values such as CPU, memory, network, process, or runtime state. |
| Activity | Resource actions, lifecycle milestones, deployment updates, and other Control Plane events. |
| Health | Whether declared probes and provider observations consider a resource ready or healthy. |
| Usage | Retained provider-observed samples intended for history and reporting. |

CloudShell keeps these categories distinct so an application metric is not confused with host monitoring or a platform action.

## A typical investigation

1. Start from a degraded or unhealthy application resource.
2. Check its dependencies and recent activity for lifecycle or provider failures.
3. Open traces to find the affected request path.
4. Correlate the selected trace with logs from the participating resources.
5. Compare application metrics with provider monitoring to separate code behavior from runtime pressure.

Telemetry access follows resource access. A user who cannot read a resource should not receive its logs, spans, or metric rows through a broader observability view.
