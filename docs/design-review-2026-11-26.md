# Theme and layout review — 26 November 2026

This review keeps the existing boxed configuration layouts and shared default
background. Light panels use white surfaces, soft shadows, and faint borders;
dark panels use contrasting surfaces. See [design guidelines](system-design-guidelines.md).

## Coverage

Inspected available states in both themes: Resources and dashboards, resource
configuration and operational panes, Environment, Health, Telemetry overview,
Logs, Dependencies, Service map, Traces, Metrics, Usage, Add resource, Create
group, Templates, resource graph, Resource Manager settings, Settings sections,
Extensions, Users, Login, Setup, Access denied, Not found, and Error.
Configuration-store provider views and the sample provider were included.

These checks cover available local fixtures, not every possible populated state.
Live RabbitMQ/SQL telemetry, device data, authenticated user-management states,
and third-party extension pages still need representative runtime environments.
The ResourceHost design preview adds simulated mixed kinds, groups, nested
resources, long names, and stopped states; it is not a live-provider substitute.

## Changes in this pass

- Replace the inventory Expand label with reversible, accessible icons.
- Distinguish Resource Group sections from parent/child resource nesting.
- Correct resource telemetry routing and refresh content when switching panes.
- Reduce repeated operational headings while retaining configuration boxes.
- Use shared inset surfaces for nested rows, filters, and health history.
- Replace fixed activity/status colors and the permanently dark metric table
  with theme-aware tokens.
- Correct Environment full-width sections across component CSS isolation.
- Let empty telemetry panels fit their messages instead of reserving a stream viewport.
- Align account branding, fallback pages, and Settings copy with the shell.
- Avoid stretching Extensions panels to the height of unrelated neighboring content.

## Remaining review

- Exercise populated provider-specific charts, tables, and failure states with
  real runtime services in both themes.
- Exercise authenticated user-administration forms inside Settings; the local preview currently exposes the unavailable state.
- Extend the static relationship component to more complex graph layouts; it now
  shares the full graph's node/edge styling and provider-aware icons.
- Expand narrow-screen and keyboard-only checks across provider forms.


## Follow-up review

- Rechecked embedded Extensions in light and dark mode: removed its duplicate
  outer section heading and wired its Reload command to refresh the current page.
- Checked Settings General at 700px: hide the decorative summary tiles at narrow
  widths while retaining the section picker and the settings form. Removed the
  generic “Settings section” subtitle.
- Checked provider registration guidance (RabbitMQ) and the .NET package form
  in both themes. The package-upload-disabled state is the available host state.
  Provider registration guidance now links directly to Templates; stretched empty
  panels keep their heading, explanation, and action together.
- Existing live-provider and authenticated-state coverage limits above still apply.

Validation: both preview hosts build successfully with hot reload disabled;
five Hosting tests pass. Full-solution validation remains blocked by stale
.NET preview hot-reload assets in ReplicatedContainerHealth and SignalRContainerApp
samples (scoped CSS endpoint / missing generated-file errors).
