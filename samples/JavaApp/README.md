# Java App Sample

This sample declares a CloudShell `application.java-app` resource for a small
JDK HTTP server, plus referenced Configuration Store and Secrets Vault
resources. The Java app compiles against the experimental Java SDK under
`sdk/java/cloudshell` and reads service bindings from
`CLOUDSHELL_CONFIGURATION_*` and `CLOUDSHELL_SECRETS_*` environment variables.

Run the launcher in a foreground terminal. The launcher declares the resources,
starts `CloudShell.LocalDevelopmentHost`, and applies the generated template:

```bash
./cloudshell.sh run
```

From a second terminal, inspect resources and open the UI:

```bash
./cloudshell.sh resources
./cloudshell.sh open
```

Start the Java app from Resource Manager, or start it from the helper:

```bash
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5185
```

The helper compiles `App/target/cloudshell-java-app-sample.jar` before starting
the app or launcher-backed host. The Java app can also be built directly:

```bash
./App/build.sh
```

`reset` removes generated launcher and host state plus the built Java jar.
