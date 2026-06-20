# Composition Sandbox

This is a clean Blazor application that uses the composition engine without
hosting CloudShell UI, Resource Manager, Control Plane services, or CloudShell
extension APIs.

The sample proves the first boundary:

- `CloudShell.UI.Composition` owns core facilities: typed IDs, registration,
  graph lookup, section ordering, and target-to-link resolution.
- `CloudShell.UI.Composition.Blazor` owns plain Blazor components that render
  registered menus, links, page titles, section containers, and section
  outlets.
- The app owns visual styling. This sample uses plain Bootstrap CSS plus a
  small app stylesheet; it does not use a Blazor Bootstrap component package.
- Page navigation uses normal Blazor routes. The menu and `CompositionLink`
  resolve registered page IDs to links, and `CompositionHost` resolves the
  active composition page from the current route.
- Layout patterns are explored in the sample app itself. The `/dashboard`
  route uses a sample-owned Bootstrap grid outlet over the same composition
  registry, while the other pages use the plain stacked section outlet from
  `CloudShell.UI.Composition.Blazor`.
- The `/settings` route uses the reusable composition tab outlet from
  `CloudShell.UI.Composition.Blazor`, styles it with Bootstrap classes, and
  stores selected named-section state in the `section` query parameter.

Future CloudShell extension integration should adapt extension contributions
into the core composition model after this standalone API shape is proven.

## Run

From the repository root:

```bash
dotnet run --project samples/CompositionSandbox --urls http://localhost:5102
```

Open:

```text
http://localhost:5102
```

The sample also exposes:

```text
http://localhost:5102/reports
http://localhost:5102/dashboard
http://localhost:5102/settings
```
