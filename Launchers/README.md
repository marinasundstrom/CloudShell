# CloudShell Launchers

Launchers are language-specific App Host packages and samples for declaring
CloudShell resources from application code.

A launcher builds a ResourceTemplate, then applies it to a target CloudShell
host profile through the CLI or Control Plane API. That target can be the
local development host, a custom host profile, or a separate remote Control
Plane. The launcher itself is not the Control Plane and should not reference
Control Plane stores, UI hosting packages, or provider runtime implementations.

Current launcher packages:

- `CSharp/CloudShell.AppHost.Launcher`
- `TypeScript/cloudshell`
- `Java/cloudshell-launcher`

Runtime service clients stay under `sdk/`. For example,
`sdk/java/cloudshell` is used by Java applications after CloudShell starts
them, while `Launchers/Java/cloudshell-launcher` is used by Java App Host code
that declares resources.

See [Launchers and app hosts](../docs/launchers-and-app-hosts.md) for the
terminology, host profile boundary, and cross-language parity expectations.
