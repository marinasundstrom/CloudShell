# MVP Sample Seam Audit

This tracker records user-visible or smoke-critical seams in the supported MVP
samples. It is intentionally narrower than the roadmap and proposals: each row
must describe a concrete seam in a sample that either affects the local
application-management loop or explains an accepted MVP bridge.

Classification:

| Classification | Meaning |
| --- | --- |
| Fix now | The seam makes a supported MVP path confusing, unreliable, unsafe, or misleading enough to address in the active tie-off queue. |
| Accepted MVP bridge | The seam is deliberate for local-development MVP and should be documented clearly until a later provider/runtime implementation replaces it. |
| Post-MVP deferred | The seam belongs to on-premise, distributed hosting, advanced provider parity, or richer portal behavior and should not block the local MVP. |

## Verification Baseline

The broad sample proof is currently green on the local Docker host:

```bash
dotnet test CloudShell.Sample.Tests/CloudShell.Sample.Tests.csproj --no-restore --logger "trx;LogFileName=cloudshell-sample-tests.trx"
```

Result from July 15, 2026: 115 passed, 0 failed, 0 skipped, 31m22s.
Generated TRX and sample runtime data were removed after the run, and no
CloudShell Docker containers were left running.

## Supported Sample Audit

| Sample | Proven path | Exposed seam | Classification | Next action |
| --- | --- | --- | --- | --- |
| Application Topology | Graph-backed SQL Server, SQL database, storage volume, Configuration Store, Secrets Vault, identity grants, ASP.NET Core API/frontend, service discovery, local DNS mapping, logs/traces, SQL cleanup, and graceful host shutdown. | The sample still names focused runtime seams for SQL credentials, Configuration Store, Secrets Vault, ASP.NET Core project runtime, and DNS/name mapping. These are provider-owned or Control Plane runtime concerns, but the user-facing path should read as one app topology. | Fix now | Tie off DNS/name mapping, local exposure, identity/config/secrets explanations, and readable diagnostics from the app resource context before broadening scope. |
| ReplicatedContainerHealth | Container app declaration, project-backed image publish, three replicas, Traefik ingress, session-affinity routing metadata, image rollout, replica updates, runtime telemetry, and cleanup. | The local Docker/Traefik bridge is reusable development plumbing, not the final orchestrator. Runtime replicas still have limited state detail and hidden-resource telemetry views need runtime-specific detail handling. | Accepted MVP bridge | Keep the local bridge documented, but make start, update, replica change, route rebinding, readiness, and cleanup diagnostics feel like one provider path. Defer rich rollout history and durable orchestrator UI. |
| ContainerAppDeployment | Graph-backed local registry resource, container app state updates, image tag and replica apply paths, optional Docker registry materialization, registry cleanup on graceful shutdown. | The container app runtime is intentionally deferred in this sample: lifecycle reports a warning instead of starting app replicas, and image push/deploy scripts remain sample helpers. | Accepted MVP bridge | Keep as a switch-readiness and graph-apply sample. Do not make it the runtime proof; use ReplicatedContainerHealth and SignalR container app paths for actual local materialization. |
| HostVirtualNetwork | Local host networking resource, virtual network, endpoint mapping reconciliation, CoreDNS zone-file publishing, private names, public ingress to the API health endpoint. | The MVP provider creates a local TCP proxy and generated DNS files, not OS-native adapters, firewall/NAT rules, or isolation. UI create/update flow, live mapping count projection, and runtime diagnostics are still limited. | Fix now | Improve local/default diagnostics and generated-link clarity where Resource Manager exposes mappings or names. Defer OS-native networking providers. |
| LoadBalancer | Traefik-backed load balancer resource, declared HTTP/TCP routes, local DNS name mappings, public frontend endpoints, and provider-owned dynamic configuration writing. | The sample still uses a sample-local Traefik adapter to translate declared routes into the existing provider context, and Traefik runtime container management has not fully moved into the new provider structure. | Fix now | Move only the user-visible readiness/routing diagnostics needed by the MVP path. Treat deeper Traefik runtime restructuring as provider cleanup unless a smoke path breaks. |
| ThirdPartyIdentity | OIDC sign-in with Keycloak, role mapping, CloudShell authorization, external resource identity provisioning, protected Configuration Store reads with Keycloak-issued workload tokens, and compose cleanup. | Keycloak is a reference integration. Runtime credential environment creation and deterministic sample client secrets are sample-local/provider-owned bridges. | Accepted MVP bridge | Keep the bridge documented and stable. Improve setup/provisioning diagnostics where users would otherwise see generic identity or configuration failures. |
| SettingsAndSecrets | Configuration Store and Secrets Vault resource declarations, app setting and secret references, service discovery, built-in identity grants, protected configuration/secret reads, and old-provider removal. | Service-specific endpoint variables still coexist with service discovery for the first client integration, and runtime backing services are local C# projects rather than alternate provider runtimes. | Accepted MVP bridge | Keep values redacted and references visible. Improve app-context explanations for runtime-impacting references and grant status before adding broader editor workflows. |
| SplitHosting | Separate Control Plane and UI processes, remote Control Plane client adapter, protected API access with client credentials, remote Resource Manager projection, and remote environment settings storage. | The UI host itself is unauthenticated and uses a local-development client secret. The sample proves split hosting and remote projection, not production auth hardening. | Accepted MVP bridge | Keep split-hosting smoke coverage green. Do not expand split-hosting security UX unless it blocks the local MVP or API/client contract stability. |

## Progress Notes

- DNS/name-mapping reconcile now reports available target endpoint names when a
  mapping references a missing endpoint. This is the first routing/name
  diagnostics tie-off for the LoadBalancer and Application Topology paths.
- DNS/name-mapping reconcile now reports the requested DNS publishing provider
  and the registered provider names when provider selection fails.
- Network endpoint-mapping reconcile now reports projected resource IDs,
  available endpoint names, available endpoint-mapper resources, and registered
  materializer count for the HostVirtualNetwork path when a mapping cannot be
  resolved or materialized.
- Generated endpoint details now load related network and provider resources
  from endpoint-network mappings before rendering mapped-address topology and
  provider labels, reducing raw-ID fallback in the local exposure views.
- Name-mapping generated links now honor explicit target endpoint names: if the
  declared endpoint is missing, Resource Manager does not synthesize a link
  from the target resource's first endpoint.
- Resource action capabilities now report the provider, action, and resource
  when a resource advertises an action but the provider does not support
  procedure execution, improving disabled-action titles and readiness
  diagnostics before dispatch.
- Local Docker container app runtime failures now include the runtime
  operation and target resource for lifecycle, image, replica, and orchestrator
  paths while preserving the original provider error details.
- Endpoint-mapping reconciliation now turns provider-returned error signals
  into graph operation diagnostics or Resource Manager procedure signals
  instead of reporting those mappings as successfully provisioned.
- Graph-backed Traefik load-balancer apply now turns unresolved route targets
  into provider-execution diagnostics, keeping routing readiness failures
  visible at the operation boundary instead of surfacing as raw exceptions.
- Graph-backed load-balancer apply now reports a provider-execution diagnostic
  when no configuration applier is registered, so missing provider packages do
  not look like successful configuration writes.
- Graph-backed load-balancer apply readiness now uses the same missing
  configuration-applier reason before dispatch, keeping Resource Manager
  action state aligned with execution diagnostics.
- Graph-backed DNS/name-mapping reconciliation now reports missing reconciler
  readiness before dispatch and returns the same provider-execution diagnostic
  if invoked directly, avoiding silent no-op name publishing.
- Graph-backed endpoint-mapping reconciliation now reports missing reconciler
  readiness for network, virtual-network, local host-network, and macOS
  host-network resources before dispatch and returns matching
  provider-execution diagnostics if invoked directly.
- Graph-backed volume provisioning now reports missing provisioner readiness
  for `cloudshell.volume` and the older `storage.volume` local-volume type
  before dispatch and returns matching provider-execution diagnostics if
  invoked directly.
- Graph-backed SQL Server access reconciliation now reports missing reconciler
  readiness before dispatch and returns a matching provider-execution
  diagnostic if invoked directly.
- Graph-backed RabbitMQ access reconciliation now reports missing reconciler
  readiness before dispatch and returns a matching provider-execution
  diagnostic if invoked directly.

## Active Tie-Off Order

1. Tie off Application Topology explanations and diagnostics first.
2. Make container-app runtime operations read as one provider-backed local path.
3. Close routing, name, endpoint, and readiness rough edges for the
   local/default path.
4. Smooth Resource Manager labels, action capability reasons, generated
   details, and app-scoped observability around the supported samples.
5. Keep accepted MVP bridges documented in sample READMEs and proposal status
   until a later provider/runtime slice replaces them.
