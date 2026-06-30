# CloudShell Resource Host Sample

This sample hosts CloudShell UI and the Control Plane in the same ASP.NET Core
process. It adds a custom resource extension and declares three sample resources
at startup.

## Run

From the repository root:

```bash
dotnet run --project samples/CloudShell.ResourceHost --urls http://localhost:5102
```

Open:

```text
http://localhost:5102
```

The sample configuration enables the built-in Identity mode so the in-memory
`alice` user and programmatic resource grant are exercised by default. Open the
sign-in page and use:

```text
Email: alice@example.test
Password: CloudShell123!
```

Useful routes:

```text
http://localhost:5102/resources
http://localhost:5102/api/control-plane/v1/resources
http://localhost:5102/openapi/control-plane-v1.json
```

Browser requests after sign-in use the CloudShell authentication cookie. For a
fully permissive local-development run, disable authentication explicitly:

```bash
dotnet run --project samples/CloudShell.ResourceHost -- --urls http://localhost:5102 --Authentication:Enabled=false
```

In permissive mode, the Control Plane API is unauthenticated and authorization
allows all operations, so command-line inspection works without a cookie:

```bash
curl http://localhost:5102/api/control-plane/v1/resources
```

## Access Model

Alice has the `CloudShell.Reader` role plus programmatic grants for
`resources.read` on `sample:api` and `resources.manage` on `sample:database`.
The reader role grants shell, resource read, and observability read
permissions, but no wildcard resource scope. The programmatic grants make the
guarded Resource Manager view intentionally scoped to API inspection and
database management; `sample:worker` remains hidden from Alice. Activity
created from Alice's browser/API session is audited with the signed-in account
identifier, `alice@example.test`; the programmatic grant principal key remains
`alice`.
Username sign-in is disabled by default, so `alice` is not accepted as a login
identifier unless `Authentication:BuiltInIdentity:AllowUserNameSignIn=true` is
configured.

Use this sample for both cases:

- Default run: authentication is enabled and the UI/API enforce Alice's
  permissions and resource grants.
- Permissive run: pass `--Authentication:Enabled=false` when you want the
  early local-development behavior where every resource and operation is
  visible.

## What To Look For

- The project references `CloudShell.Hosting`, the reusable Razor class library
  that carries CloudShell shell components and static assets.
- `Program.cs` uses the convenience `builder.AddCloudShell()` registration for
  UI and Control Plane together.
- `SampleResourceExtension` contributes a resource provider and resource type.
- `Pages/RegisterSampleResource.razor` is a normal Razor component used by the
  sample resource type registration flow.
- `SampleResourceProvider` exposes sample API, database, and worker resources.
- Sample resources advertise provider-backed lifecycle actions through the
  `resourceActions` API dictionary.
- The startup `DefineResources(...)` block uses manual `.Declare(...)`
  provider-backed declarations for `sample:api`, `sample:database`, and
  `sample:worker`, so they are visible immediately in permissive Resource
  Manager and Control Plane API runs. Authenticated users still see only the
  resources allowed by their grants and scoped role permissions.
- `Program.cs` calls `ConfigureInMemoryIdentity(...)` to register the built-in
  provider with an in-memory ASP.NET Core Identity store and an `alice` test
  user. Alice can sign in with the configured password, is exposed as a user
  principal, and receives `resources.manage` on `sample:database` through the
  programmatic grant model.
- In-memory identity users, roles, claims, and grant-derived resource
  permissions are local-development test state. They are not persisted and are
  cleared when the sample process stops.

Runtime state is stored under `samples/CloudShell.ResourceHost/Data/` and is
ignored by git.
