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
