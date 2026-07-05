# Go Container App Sample

This sample uses the Go launcher to declare a Go app as an
`application.container-app` and a Configuration Store dependency. The launched
container app reads the store through `sdk/go/cloudshell`, using the same
default credential chain that other CloudShell runtime clients use.

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

The `/configuration` endpoint resolves the Configuration Store endpoint from
the CloudShell-provided runtime environment and authenticates with the default
Go SDK credential chain.

The sample container image is built from `App/Dockerfile`. The Docker build
context is the repository root so the Dockerfile can copy both the sample app
and the local Go SDK module. The container listens on port `8080`; CloudShell
maps it to host port `5188` and starts two local replicas.
