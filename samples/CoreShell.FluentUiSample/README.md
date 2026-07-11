# CoreShell Fluent UI Sample

This sample is the reference CoreShell-only Fluent UI shell. It does not
reference `CloudShell.Hosting`, Resource Manager, or the Control Plane.

It proves the extraction boundary for common shell building blocks:

- CoreShell owns modules, pages, menus, targets, section outlets, sections,
  route resolution, navigation services, section-address services, and the
  minimal notification UI contract.
- `CoreShell.Blazor` maps CoreShell content references to Blazor component
  types.
- The sample owns the Fluent UI presenters for navigation, section stacks, and
  tabbed sections.
- The sample owns the reference notification UI surface: a topbar notification
  center, toast-style transient status cards, and a host-provided in-memory
  notification source behind `ICoreShellNotificationService`.
- Notification actions are optional. When an instance supplies actions, the
  toast renders them and the notification center renders them again if the
  user ignores the toast.
- Toast-only signals use `ICoreShellToastService`, render in the same toast
  stack, and do not create notification-center history.
- A second CoreShell module contributes a dashboard section and navigation
  item without using CloudShell product services.

Use this sample as the development testbed for CoreShell common building
blocks before moving them into CloudShell. A CoreShell app can own its own
Blazor layout and visual styling while still using the same CoreShell modules,
navigation services, section outlets, route targets, and extension model that
CloudShell depends on.

The dashboard work queue includes a simulated asynchronous create-resource
action. It publishes an in-progress notification and updates it to success,
showing the CoreShell notification contract, optional actions, loading
indicator, toast-only transient signal path, and Fluent UI presenter behavior
before the equivalent CloudShell notification rules and Control Plane event
integration are implemented.

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
