# Container Apps

Use the container app resource type for a deployable container workload. These
resources project as `application.container-app`. The container app is the
stable deployment target. It is not the same thing as a Docker container
resource, even when the current host is Local Docker.

Docker or another container-engine provider may expose runtime containers or
replicas as separate resources for inspection and low-level operations, but
deployment automation should target the container app.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [Executable applications](executable-applications.md) and
[ASP.NET Core applications](aspnet-core-applications.md).

## Host Binding

Container apps can be bound to a specific container host resource, such as Local
Docker, or can rely on the configured default container engine. That binding is
host plumbing. Build systems, shell integrations, and users should not need to
know which runtime container instance currently backs the app.

Declare a top-level container app with `AddContainerApplication(...)`:

```csharp
resources
    .AddContainerApplication(
        "application:api",
        "API",
        "team/api:dev",
        registry: "https://registry.example.com")
    .WithContainerEngine("docker:dev");
```

`AddContainer(...)` remains available as the Aspire-compatible shorthand for the
same top-level `application.container-app` resource:

```csharp
resources
    .AddContainer("api", "team/api:dev")
    .WithRegistry("https://registry.example.com");
```

This is intentionally different from `resources.AddDocker().AddContainer(...)`,
which creates a Docker container sub-resource parented under a Docker resource.

## Registry

Container apps can specify a container registry separately from the image name.
The registry defaults to `http://localhost:5000`. Registry values are stored as
URI strings. Runtime orchestrators use the URI authority when they need a
pullable image reference, for example `http://localhost:5000` becomes
`localhost:5000/team/api:dev`.

The registry is projected as the non-secret `container.registry` resource
attribute and is included in workload descriptors. Registry credentials are not
modeled in this attribute; private registry authentication remains
provider-owned configuration.

The Container app registration and configuration tabs expose the registry next
to the image setting. Docker Engine registration/configuration exposes a
registry setting for Docker child-container resources; that setting also
defaults to `http://localhost:5000`.

## Resource Manager Deployment

The Resource Manager overview for a container app includes a deploy image
control. Enter a new image tag and choose whether CloudShell should restart a
running app after the update. The shell calls the same domain
`UpdateResourceImageAsync` operation used by remote clients, then refreshes the
projected container image and revision.

## Image Deployment Procedure

The proposed deployment flow for CloudShell-hosted dev environments is:

1. The build server builds the application image.
2. The build server tags the image with an immutable value, usually the commit
   SHA, build number, or release version.
3. The build server pushes the image to the configured registry.
4. The build action calls the authenticated Container Apps API to create a new
   container app revision that points at the pushed image tag.
5. The Control Plane updates the container app, records a resource event with
   the actor/trigger, creates a new revision value, and asks the provider to
   restart the app when requested.

The API call targets the container app, not an underlying Docker container:

```http
POST /api/container-apps/v1/{containerAppId}/revisions
Authorization: Bearer <control-plane-access-token>
Content-Type: application/json

{
  "image": "team/api:20260608.42",
  "restartIfRunning": true,
  "triggeredBy": "build:20260608.42"
}
```

Authentication is required whenever CloudShell authentication is enabled. A
build action should use a Control Plane credential intended for
service-to-service automation, such as a client secret, client certificate, or
equivalent static credential for local/dev-only environments. The credential
must authorize the build identity to manage the target container app or its
resource group.

## Revisions

The image tag update creates a new app-owned revision. The revision is projected
on the container app resource through `container.revision`; runtime containers
or replicas implement that revision but do not define it.

The Resource Manager overview shows the latest projected revision. A richer
revision history needs a dedicated design because revisions represent commits
of changes to the container app configuration, not just image tags.

This is intentionally similar to Azure Container Apps at the basic concept
level: a deployment produces a revision of the app. CloudShell's MVP keeps the
revision model simple and does not yet model traffic splitting, activation
state, or rollout history as first-class concepts.

## Logs And Events

The `triggeredBy` value from the deployment request is written to the resource
event stream so deployments can be traced back to the build action, user, or
external system that requested them.

Container apps should also expose console logs from the underlying workload as
resource-type-specific logs. Those logs show stdout/stderr from the running
container. They complement, but do not replace, the platform-owned `Resource
events` stream that records who or what changed the resource.
