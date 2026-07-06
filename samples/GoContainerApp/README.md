# Go Container App Sample

This sample uses the Go launcher to declare a Go app as an
`application.container-app`, Configuration Store, Secrets Vault, and resource
identity grants. The launched container app reads both services through
`sdk/go/cloudshell`, using the same default credential chain that other
CloudShell runtime clients use.

Run the launcher against the local development host:

```bash
./cloudshell.sh run-no-auth
```

From a second terminal, inspect resources and start the container app:

```bash
./cloudshell.sh resources
./cloudshell.sh start-app
```

After the app starts, open:

```text
http://localhost:5188
http://localhost:5188/configuration
```

The `/configuration` endpoint resolves the Configuration Store and Secrets
Vault endpoints from the CloudShell-provided runtime environment and
authenticates with the default Go SDK credential chain. The response reports
whether the secret was available without returning the secret value.

The sample container image is built from `App/Dockerfile`. The Docker build
context is the repository root so the Dockerfile can copy both the sample app
and the local Go SDK module. The container listens on port `8080`; CloudShell
maps it to host port `5188` and starts two local replicas.

## Language and SDK versions

- Go 1.22 is the module and Docker build version.
- The app consumes the repository-local `sdk/go/cloudshell` module through the
  `replace` directive in `App/go.mod`; there is no published Go SDK package
  dependency yet.
