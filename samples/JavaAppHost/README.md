# Java App Host Launcher Sample

This sample shows the launcher-style Java hosting pattern. The Java source-file
program in `AppHost/AppHost.java` uses the experimental Java launcher package
from `Launchers/Java/cloudshell-launcher` to declare a ResourceTemplate, then
uses the CloudShell CLI to apply it to the local development host profile. The
launcher declares a Java app plus
Configuration Store and Secrets Vault references so the running JVM process can
consume the same service-binding variables as other language integrations.
It also seeds development Configuration Store settings and Secrets Vault secrets
through create-only resource-definition attributes.

The Java launcher package is intentionally separate from the runtime clients in
`sdk/java/cloudshell`. Runtime clients are used inside Java workloads after
CloudShell starts them; launcher builders are used by App Host programs that
define resources and apply them to a host profile.

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

The Java app resource is declared but not auto-started. Start it from Resource
Manager or through the helper. The helper enables dependency startup, so the
Configuration Store and Secrets Vault resources are started before the Java app:

The generated template seeds `Sample--Message` in the Configuration Store and
`Sample--ApiKey` in the Secrets Vault. Those values materialize into provider
runtime state on create and are not emitted by default template export after
apply. The Java app binds those seeded values into `Sample__Message` and
`Sample__ApiKey` environment variables so the running sample response shows the
configured message and whether a secret was resolved. Passing a secret through
an environment variable is only used here to keep the launcher demo visible;
production workloads should prefer resolving secrets through the Secrets Vault
client instead of exposing secret values in process environment state.

```bash
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5186
```

Generated daemon state and host data default to `.cloudshell/` under this
sample so local CloudShell files stay with the launcher project.
