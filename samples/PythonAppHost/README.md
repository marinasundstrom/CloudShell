# Python App Host Launcher Sample

This sample shows a Python workload declared from a Python launcher. The
launcher uses the experimental package under `Launchers/Python/cloudshell` to
emit a ResourceTemplate and target `CloudShell.LocalDevelopmentHost`.

Generate the template:

```bash
./cloudshell.sh template
```

Run the local-development host in the foreground, apply the declarations, and
keep the host tied to the launcher command lifetime:

```bash
./cloudshell.sh run-no-auth
```

The Python app resource is declared but not auto-started. Start it from
Resource Manager or through the helper:

```bash
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5188
http://localhost:5188/configuration
```

The sample seeds Configuration Store and Secrets Vault resources, declares a
resource identity for the Python app, grants that identity read access, and
injects the protected service endpoints. The app uses the experimental Python
runtime SDK to read the setting and secret through `DefaultCloudShellCredential`
and reports whether an API key was present without printing the secret value.
