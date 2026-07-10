# Java Container App Sample

This sample uses the Java launcher to declare a Java app as an
`application.container-app`, Configuration Store, Secrets Vault, and resource
identity grants. The launched container app reads both services through
`sdk/java/cloudshell`, using the same default credential chain as other
CloudShell runtime clients.

Generate the template:

```bash
./cloudshell.sh template
```

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
http://localhost:5191
http://localhost:5191/configuration
```

The `/configuration` endpoint resolves Configuration Store and Secrets Vault
endpoints from the CloudShell-provided runtime environment and authenticates
with the default Java SDK credential chain. The response reports whether the
secret was available without returning the secret value.

The sample container image is built from `App/Dockerfile`. The Docker build
context is the repository root so the Dockerfile can copy both the sample app
and the local Java SDK package. The Dockerfile uses Maven to build the app
before packaging the runtime image, so the sample verifies the same build step
that produces the container artifact. The container listens on port `8080`;
CloudShell maps it to host port `5191` and starts two local replicas.

For local helper commands, `./cloudshell.sh build-app` uses the JDK-only
`App/build.sh` script to compile the same app and SDK sources into
`App/target/cloudshell-java-container-app-sample.jar`. This keeps the sample
launcher usable on machines without Maven while the Dockerfile remains the
Maven container packaging proof.

## Language and SDK versions

- Java 21 is the sample runtime and compiler target.
- Maven 3.9.9 is used in the Docker build stage.
- The app consumes the repository-local `sdk/java/cloudshell` sources through
  `App/pom.xml`; there is no published Java SDK package dependency yet.
