# CoreShell platform boundary

CoreShell is the reusable shell platform underneath CloudShell. It should
provide product-neutral shell contracts, default Fluent UI reference
presenters, and a reference sample app that future shells can copy from
without inheriting CloudShell Resource Manager or Control Plane concepts.

CloudShell is one domain shell built on CoreShell. It can customize the UI,
install Resource Manager and other CloudShell product areas, and adapt
Control Plane-owned services into CoreShell contracts.

## Status

- Status: Proposed
- Strategy fit: Medium-high; this is the layer boundary that keeps common
  shell infrastructure reusable while letting CloudShell remain a product
  shell over the Control Plane.
- Canonical feature docs:
  [Architecture](../../architecture.md),
  [Shell customization](../../shell-customization.md),
  [UI composition](../../ui-composition.md), and
  [UI structure](../../ui-structure.md)
- Remaining action: use `samples/CoreShell.FluentUiSample` as the reference
  app and migrate only proven common shell building blocks from
  `CloudShell.Hosting` into CoreShell contracts, Blazor adapters, or Fluent UI
  presenters. The first contract boundary is UI-facing services for
  notifications, transient toasts, and settings that are consumed or managed
  through the shell UI.
- Out of scope: persisted shell configuration, extension marketplace policy,
  user-personalized shell composition, multi-tenant shell governance, and a
  full Resource Manager project split.

## Goals

- Make CoreShell useful without CloudShell. A CoreShell app should compose its
  own Blazor layout, navigation, pages, settings, notifications, toasts, and
  extensions without referencing Resource Manager, the Control Plane, or
  CloudShell Hosting.
- Keep CloudShell customizable. CloudShell should consume CoreShell contracts
  but remain free to implement a richer product shell with custom Resource
  Manager views, Control Plane adapters, provider integrations, settings, and
  notification templates.
- Make `samples/CoreShell.FluentUiSample` the default sample and testbed for
  CoreShell changes. New CoreShell shell behavior should be proven there
  before CloudShell adopts or customizes it.
- Keep Fluent UI as the default presenter layer, not the contract. CoreShell
  contracts should stay product-neutral and framework-neutral where practical;
  Fluent UI should sit in a host-usable presenter package or adapter layer.
- Preserve split hosting. CoreShell should not assume the UI and Control Plane
  run in the same process. CloudShell adapters can choose in-process,
  HTTP/client, SignalR, polling, or custom implementations for shell services.

## Non-goals

- Do not move CloudShell domain concepts into CoreShell. CoreShell should not
  know about resources, providers, deployments, artifacts, container apps,
  health probes, recovery policies, or the Control Plane.
- Do not make `CoreShell.Composition` the normal extension API for product
  integrations. Composition is the lower-level graph substrate; CoreShell is
  the shell-facing contract layer above it.
- Do not expose current CloudShell Hosting internals as the new shell
  contract. Extract only patterns that are already proven by the CloudShell
  UI and the CoreShell Fluent UI sample.
- Do not require every extension to use Fluent UI. Shell-owned surfaces can
  have Fluent presenters, but extension-owned pages may render their own UI
  inside the shell host area.

## Layer model

```text
CoreShell contracts
    product-neutral shell services and contribution contracts

CoreShell.Blazor
    Blazor content, layout, route, and section projection over CoreShell

CoreShell Fluent UI presenters
    default Fluent UI navigation, settings, notification, toast, and layout
    presenters over CoreShell contracts

CloudShell UI
    CloudShell product shell, Resource Manager, Observability, Usage,
    settings, Control Plane service adapters, and provider UI integrations

CloudShell Control Plane
    resource domain, operations, persistence, authorization, providers,
    resource events, diagnostics, and APIs
```

CoreShell should depend downward on reusable shell abstractions and the
composition substrate. CloudShell should depend upward on CoreShell and adapt
CloudShell-specific services into CoreShell contracts.

## CoreShell should own

- Shell contribution contracts for modules, pages, menus, menu groups, menu
  items, section outlets, sections, targets, and shell-owned layout areas.
- Shell services for navigation, route resolution, page materialization,
  section address resolution, notification querying, notification production,
  toast-only feedback, and UI-managed settings.
- Product-neutral notification and toast data: severity, status, target,
  actions, template key, attributes, time-to-live, auto-dismiss behavior, and
  change signals.
- Product-neutral settings contracts for shell UI preferences and
  extension-owned UI settings. CoreShell should define how UI components read,
  write, and observe settings, while the active host chooses local,
  Control Plane-backed, remote, or custom storage.
- Blazor adapter contracts that map CoreShell content and layout references to
  renderable components without requiring CloudShell Hosting.
- Default presenter behaviors that should be consistent across CoreShell hosts
  when using the Fluent UI reference layer.
- Validation for duplicate IDs, invalid targets, missing pages/sections, and
  other shell graph problems that should fail during startup.

## CloudShell should own

- Resource Manager and all resource, provider, deployment, artifact,
  observability, health, recovery, and Control Plane domain workflows.
- Control Plane-backed implementations of CoreShell notification, settings,
  and service adapters when CloudShell needs central state or remote updates.
- CloudShell environment preference policy, including which settings are
  user-scoped, local-host-scoped, Control Plane-backed, or product-managed.
- CloudShell-specific template keys, notification attributes, and rich
  notification renderers for resource operations.
- Resource Manager UI extension points such as resource tabs, provider-owned
  details, generated resource views, action placement, diagnostics, and
  resource-owned navigation.
- CloudShell shell chrome decisions where the product needs a richer or more
  opinionated UI than the reference CoreShell sample.

## Reference sample contract

`samples/CoreShell.FluentUiSample` should remain intentionally small but
feature-complete for CoreShell behavior. It should prove:

- a CoreShell-only host can own its own Blazor app layout;
- Fluent UI presenters can render CoreShell menus, pages, navigation targets,
  settings-like sections, notifications, and toasts without CloudShell
  Hosting;
- a sample extension can contribute navigation and page/section content;
- UI settings can be consumed and managed through a CoreShell service contract
  without depending on CloudShell environment settings providers;
- notification and toast behaviors work without a Control Plane, including
  passive facts, in-progress feedback, actions, targets, time-to-live, and
  auto-dismiss behavior;
- the sample can be used as the first smoke test for future CoreShell changes.

CloudShell should not be the first place a new generic shell behavior is
proven. Add or adjust the reference sample first, then adapt CloudShell if the
behavior is still useful for the product shell.

## Migration approach

1. Keep the CoreShell Fluent UI sample green and useful.
2. Identify common behavior currently in `CloudShell.Hosting`.
3. Classify it as CoreShell contract, CoreShell.Blazor adapter, Fluent
   presenter, CloudShell product UI, Resource Manager UI, or Control Plane
   adapter.
4. Extract only one proven behavior at a time.
5. Verify the behavior in `samples/CoreShell.FluentUiSample`.
6. Adapt CloudShell to consume the extracted contract or presenter.
7. Record CloudShell-specific behavior in CloudShell docs instead of pulling
   it into CoreShell.

## First extraction candidates

| Candidate | Target layer | Why |
| --- | --- | --- |
| Notification and toast presenter contracts | CoreShell / Fluent presenter | Already proven in both the CoreShell sample and CloudShell; should remain product-neutral. |
| UI settings service contract | CoreShell | CloudShell currently has `ICloudShellUserSettingsProvider`; the reusable contract should move to CoreShell so shell UI preferences and extension-owned UI settings are not CloudShell-specific. |
| Navigation menu presenter patterns | Fluent presenter | The CoreShell sample and CloudShell both need the same basic menu rendering while CloudShell can still customize chrome. |
| Settings-style section host | CoreShell.Blazor / Fluent presenter | Settings are a common shell surface and should not depend on Resource Manager. |
| Shell target/action link helpers | CoreShell.Blazor | Targets, links, and action navigation are common shell primitives. |
| Sample extension package shape | CoreShell sample | Future CoreShell consumers need a minimal extension example without CloudShell services. |

## Open questions

- Should the default Fluent UI presenters live in a new package such as
  `CoreShell.FluentUI`, or remain in sample/CloudShell Hosting until more
  behavior is extracted?
- What is the exact CoreShell settings interface shape for reading, writing,
  deleting, and observing UI-managed settings?
- Which settings are pure CoreShell UI settings versus CloudShell environment
  preferences backed by CloudShell policy or the Control Plane?
- How much route ownership should CoreShell take before it becomes a custom
  router rather than a Blazor projection layer?
- What is the minimum smoke test suite that should run for every CoreShell
  contract change?
- When should the future shell-composition document be retired into this
  active proposal and feature docs?
