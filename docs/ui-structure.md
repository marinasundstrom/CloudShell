# UI Structure and Component Organization

CloudShell UI should be built from named, maintainable components without
turning every local fragment into a global abstraction.

This document defines the default rules for structuring Blazor UI, organizing
component files, and deciding when a component belongs next to a view, inside a
feature area, or in a shared component package.

## Principles

- Prefer Fluent UI components for standard controls, commands, form fields,
  anchors, menus, checkboxes, selectors, and other established UI primitives.
- Create CloudShell components for distinct CloudShell layout elements,
  resource representations, status summaries, cards, grids, selectors, and
  repeated view patterns.
- Give custom components domain or presentation names that describe what they
  are, not just how they are styled.
- Keep styles and layout logic close to the component that owns them. Prefer
  component-scoped `.razor.css` for component-owned appearance.
- Keep global CSS for application chrome, design tokens, broad page layout,
  shared reset/compatibility rules, and intentionally global utility classes.
- Do not promote a component to a shared folder only because markup is long.
  A component should be shared when it is reused, expected to be reused, or
  represents a stable CloudShell concept.

## Placement

Use the narrowest reasonable location for a component.

### View-local components

Place a component next to the page or view that consumes it when:

- it is used by only one page or view;
- it exists mainly to make that view readable;
- it depends on that view's private state or workflow;
- it is unlikely to be reused outside the feature.

For example, a helper component for one settings section should live near that
settings section until another real consumer appears.

### Feature-area components

Place a component in a feature or provider folder when:

- it is reused by several views within the same feature;
- it represents a provider-specific or feature-specific concept;
- it should not become part of the shared shell component language.

Provider-owned components should usually live under the provider feature area
or its Resource Manager UI package, such as a
`<provider-package>/<resource-type>/Pages/` folder, instead of in a global
shared folder.

### Shared shell components

The common shell infrastructure uses CoreShell as its logical boundary:
framework-neutral shell contracts and services in CoreShell, Fluent UI
presenters over those contracts, and CloudShell as the product shell that
assembles the default presenters, CloudShell-specific services, and predefined
integrations. Some Fluent presenters still live in `CloudShell.Hosting`; use
the CoreShell boundary when deciding whether a component is generic shell
infrastructure, CloudShell product UI, or Resource Manager UI.

Place a component in `CloudShell.Components` only when:

- it is reused across more than one product area, provider, or host surface;
- it is intentionally a shared CloudShell representation, such as an empty
  state, resource icon, generated list item, resource table identity, metric
  card, or resource selector;
- it does not depend on Hosting-only services or Resource Manager internals;
- it can be consumed by provider packages without creating an ownership leak.

Extension UI packages should be able to reference abstractions and stable
shared components without referencing the concrete CloudShell UI host package.
Do not place extension-facing UI contracts or generally reusable component
building blocks in `CloudShell.Hosting` merely because that is where the
current built-in shell implementation lives.

CloudShell UI should consume extension contributions through extension points
and services. Extension components can render inside shell-owned outlets, but
they should not require direct access to shell internals or host-only helper
types unless the integration package explicitly owns that adapter boundary.

Avoid making Fluent UI or the current CloudShell host implementation part of
an extension-facing contract unless the package is explicitly a Fluent
presenter package. A future dedicated CoreShell Fluent UI package can carry
the default CloudShell look and feel while the CoreShell contracts remain the
framework-neutral extension surface. Another CloudShell UI implementation
should be able to use the same public abstractions and services, then provide
its own presenters.

When the extension point is behavioral rather than visual, prefer a shared
abstraction plus an optional CloudShell UI integration package over exposing a
component as the contract. Notifications are the model case: extensions should
publish notifications through a service abstraction, while CloudShell UI owns
the toast/off-canvas presenters that render those notifications.

Place components in `CloudShell.Hosting/Components/ResourceManager` when they
are shared within Resource Manager but need hosting services, shell catalog
lookups, navigation helpers, or Resource Manager-specific context.

Place components in `CloudShell.Hosting/Components/Layout` only when they are
current shell presenter components for the built-in CloudShell host. If they
become durable shell concepts, they are candidates for a future CoreShell
presenter package rather than Resource Manager or provider-owned code.

## Component Boundaries

Custom components should own one clear responsibility:

- layout components own arrangement and spacing;
- representation components own a repeated visual shape;
- control components own interaction state and events;
- feature components own a feature-specific workflow.

Avoid components that only wrap one HTML element with a class unless the class
represents a stable CloudShell element. Prefer plain Fluent UI or semantic HTML
when there is no CloudShell concept to name.

When a component needs customization, prefer parameters and templates over
requiring callers to recreate internal markup. Use `@attributes` when the
component can safely pass through accessibility attributes, test hooks, or
normal HTML attributes to its root or primary element.

## Styling

Use component-scoped CSS when the style belongs to the component:

- card internals;
- local grid layout;
- component-specific hover/focus behavior;
- component-owned text truncation;
- icon placement inside the component.

Use global CSS only for:

- shell layout regions;
- app-wide panels and page shells;
- design tokens and color variables;
- typography rules;
- intentionally shared utility classes;
- third-party compatibility or reset styles.

If several views share the same class because they share a concept, consider
whether that concept should become a component. For example, a repeated metric
card and metric summary grid should be represented by `SummaryMetricCard` and
`SummaryMetricGrid` rather than repeated `div` markup plus global classes.

## Localization

Shared layout and representation components should usually receive already
localized strings from the caller. Components that are owned by the shell or a
specific feature may inject their own localizer when they own the displayed
text.

Do not hide user-facing strings inside generic shared components unless the
component owns the wording as part of its contract.

## Refactoring Rules

Before extracting a component, ask:

1. What concept does this component name?
2. Is the component view-local, feature-local, Resource Manager-shared, or
   product-shared?
3. Does the component own styling or interaction logic that should be
   contained?
4. Can Fluent UI already express this without a CloudShell wrapper?
5. Will moving it create a dependency from a shared package back into Hosting,
   Resource Manager, or a provider?

Start local. Promote only when reuse or a stable concept justifies it.
