# CoreShell Fluent UI Sample

This sample is the reference CoreShell-only Fluent UI shell. It does not
reference `CloudShell.Hosting`, Resource Manager, or the Control Plane.

It proves the extraction boundary for common shell building blocks:

- CoreShell owns modules, pages, menus, targets, section outlets, sections,
  route resolution, navigation services, and section-address services.
- `CoreShell.Blazor` maps CoreShell content references to Blazor component
  types.
- The sample owns the Fluent UI presenters for navigation, section stacks, and
  tabbed sections.
- A second CoreShell module contributes a dashboard section and navigation
  item without using CloudShell product services.

Use this sample as the development testbed for CoreShell common building
blocks before moving them into CloudShell. A CoreShell app can own its own
Blazor layout and visual styling while still using the same CoreShell modules,
navigation services, section outlets, route targets, and extension model that
CloudShell depends on.

## Run

From the repository root:

```bash
dotnet run --project samples/CoreShell.FluentUiSample --urls http://localhost:5103
```

Open:

```text
http://localhost:5103
```

Useful routes:

- `/`
- `/operations`
- `/settings`
- `/settings/appearance`
