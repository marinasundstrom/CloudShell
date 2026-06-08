# Container App Deployment Sample

This sample models a container app deployed next to a local registry resource.
It is intended to show the Control Plane deployment flow without requiring a
real build server.

The resource graph declares:

- `docker:sample`: the local Docker environment.
- `docker:container:sample-registry`: a local registry instance at
  `localhost:5000`.
- `application:sample-api`: a container app that uses the registry and starts
  from the mock image tag `cloudshell/mock-api:20260608.1`.

The sample keeps the registry and app stopped by default. That makes the
revision flow safe to run even when the mock image has not actually been pushed.
When you do start the resources, Docker expects the referenced image tags to
exist in `localhost:5000`.

Run the sample:

```bash
dotnet run --project samples/ContainerAppDeployment/CloudShell.ContainerAppDeployment.csproj -- --urls http://localhost:5007
```

Simulate a new build/deploy:

```bash
samples/ContainerAppDeployment/deploy-mock-image.sh
```

Or pass an explicit app ID and tag:

```bash
samples/ContainerAppDeployment/deploy-mock-image.sh application:sample-api 20260608.2
```

The script posts to:

```text
POST /api/container-apps/v1/{containerAppId}/revisions
```

with a new image tag and `restartIfRunning=false`. The Control Plane updates the
container app image, creates a new app-owned revision, and records resource
events for traceability.
