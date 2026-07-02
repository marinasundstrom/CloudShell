# Java App Host Launcher Sample

This sample shows the launcher-style Java hosting pattern. The Java source-file
program in `AppHost/AppHost.java` uses a small fluent Java authoring surface to
declare a ResourceTemplate, then uses the CloudShell CLI to apply it to the
local development host profile. The launcher declares a Java app plus
Configuration Store and Secrets Vault references so the running JVM process can
consume the same service-binding variables as other language integrations.

The small builder classes in `AppHost.java` are intentionally sample-local.
They are a prototype for a future Java app-host authoring SDK, not the stable
library surface. Keep reusable runtime clients in `sdk/java/cloudshell`; move
launcher builders into a separate Java app-host package only after the Java
authoring shape has settled.

Generate the template:

```bash
./cloudshell.sh template
```

Start or reuse the local-development host profile, then apply the declarations:

```bash
Authentication__Enabled=false ./cloudshell.sh start
```

Open the Web UI:

```bash
./cloudshell.sh open
```

The Java app resource is declared but not auto-started. Start it from Resource
Manager or through the helper:

```bash
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5186
```

Generated daemon state and host data default to `.cloudshell/` under this
sample so local CloudShell files stay with the launcher project.
