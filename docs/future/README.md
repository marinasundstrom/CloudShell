# Future Directions

Future direction documents capture product ideas that fit CloudShell's long
term strategy but are not active implementation proposals. They should not
drive MVP work or appear in the proposal order until a concrete near-term slice
is accepted.

Use these documents for strategic fit, vocabulary, and later proposal
extraction. When a direction becomes actionable, create or move a focused
proposal under `docs/proposals/` with a narrow problem, goals, non-goals,
implementation plan, and remaining tasks.

## Current Future Directions

| Direction | Strategy fit | Action |
| --- | --- | --- |
| [Deployment projection](deployment-projection.md) | Strong long-term fit for portability from local development to on-premise and provider-backed environments. | Defer until ResourceDefinition apply, container app orchestration, networking, storage, identity, and on-premise target boundaries are stable enough to project. |
| [Resource graph import and code generation](resource-graph-import-and-code-generation.md) | Strong adoption fit because existing Docker Compose users need an onboarding path. | Defer active implementation; revisit when container apps, volumes, networking, and read-only/import UX are stable. |
| [Shell composition](shell-composition.md) | Strong post-MVP fit for turning CloudShell UI into an independently useful extensible shell platform. | Defer broad shell-platform contracts; use current MVP work only to extract proven shell, Settings, and Resource Manager patterns. |
| [Resource Manager project structure](resource-manager-project-structure.md) | Useful structural direction after CoreShell and Resource Manager UI boundaries settle. | Defer physical project/assembly restructuring until the current UI path is stable. |
| [IoT device provisioning](iot-device-provisioning.md) | Plausible later fit for edge/device environments because it reuses resource identity, access, telemetry, and provisioning concepts. | No product action now. Keep as strategic note until the local/on-premise control-plane story is credible. |
