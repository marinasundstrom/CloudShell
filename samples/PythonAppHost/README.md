# Python App Host Launcher Sample

This sample shows a Python workload declared from a C# launcher. The launcher
uses the built-in `AddPythonApp(...)` Resource Model builder to target
`CloudShell.LocalDevelopmentHost`; it does not add Python-native launcher
support. Python launcher builders are intentionally deferred to the next slice.

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

The sample seeds Configuration Store and Secrets Vault resources, then resolves
those references into process environment variables when the Python app starts.
The app reports whether an API key was present without printing the secret
value.
