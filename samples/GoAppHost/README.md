# Go App Host Launcher Sample

This sample shows the launcher-style Go hosting pattern. The Go program in
`AppHost` uses the experimental Go launcher package from
`Launchers/Go/cloudshell` to declare a ResourceTemplate, then uses the
CloudShell CLI to apply it to the local development host profile.
This is the preferred sample shape for ordinary app/resource authoring:
declare the graph in the app host language, then target
`CloudShell.LocalDevelopmentHost`. Do not create a full CloudShell host unless
the sample is demonstrating host composition or a host-local extension point.

The launcher declares a Go app plus Configuration Store and Secrets Vault
references so the running Go process can consume the same service-binding
variables as other language integrations. It also seeds development
Configuration Store settings and Secrets Vault secrets through create-only
resource-definition attributes.

The Go launcher package is intentionally separate from runtime service
clients. Runtime clients run inside workloads after CloudShell starts them;
launcher builders are used by App Host programs that define resources and
apply them to a host profile.

Generate the template:

```bash
./cloudshell.sh template
```

Run the local-development host in the foreground, apply the declarations, and
keep the host tied to the launcher command lifetime:

```bash
Authentication__Enabled=false ./cloudshell.sh run
```

Apply to an already-running Control Plane:

```bash
./cloudshell.sh apply
```

Start or reuse the daemon-style local-development host profile, then apply the
declarations:

```bash
Authentication__Enabled=false ./cloudshell.sh start
```

Open the Web UI:

```bash
./cloudshell.sh open
```

The Go app resource is declared but not auto-started. Start it from Resource
Manager or through the helper. The helper enables dependency startup, so the
Configuration Store and Secrets Vault resources are started before the Go app:

```bash
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5187
```

Generated daemon state, template output, and host data default to
`.cloudshell/` under this sample so local CloudShell files stay with the
launcher project.
