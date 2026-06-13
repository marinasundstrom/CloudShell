# Load Balancer

This sample declares a Traefik-backed Load Balancer resource, real public
container images for web/API/Postgres targets, and a replicated API container
app. Applying the load-balancer action writes Traefik dynamic configuration
with backend entries for the target container app instances and starts a
provider-owned Traefik container on the selected Docker host.

```bash
dotnet run --project samples/LoadBalancer/CloudShell.LoadBalancer.csproj -- --urls http://localhost:5011
```

Open `http://127.0.0.1:5011/resources`, start **Web App**, **API Service**,
and **Postgres Replica Set**, then invoke **Apply load balancer configuration** on
**Public Load Balancer**.

For the normal container-app path, **API Service** exposes its own app endpoint
as soon as it starts. Because it has three replicas, CloudShell starts
app-owned ingress for `http://localhost:5081` automatically; no public load
balancer action is required for that endpoint.

```bash
curl http://localhost:5081/
```

The HTTP routes are host-based, so `http://localhost/` is not a configured
public load-balancer route. Use the declared hosts after applying the public
load-balancer configuration:

```bash
curl --resolve app.local:80:127.0.0.1 http://app.local/
curl --resolve api.local:80:127.0.0.1 http://api.local/v1/get
```

The sample exposes Postgres through the TCP entrypoint on `localhost:5432`.
